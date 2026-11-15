using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RatTrainer
{
    public sealed class HostBridge
    {
        private const string RequestName = "RatTrainer.v1.request";
        private const string ReplyName = "RatTrainer.v1.reply";
        private sealed class Peer
        {
            public WorldOptions Options = new WorldOptions();
            public float LastSeen;
            public float NextSpawn;
        }
        private readonly Plugin p;
        private NetworkManager manager;
        private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        private readonly Dictionary<ulong, float> lastRequest = new Dictionary<ulong, float>();
        private readonly WorldOptions empty = new WorldOptions();
        private WorldOptions reportedGlobal = new WorldOptions();
        private ulong? globalOwner;
        private float nextSend, replyTime = -100f;
        private int probes, sequence;
        private bool permission, hostFound;
        public bool GlobalAccepted;
        public string Status = "未连接房间";

        public HostBridge(Plugin plugin) { p = plugin; }
        public bool Available { get { return Plugin.IsHost || (permission && Time.unscaledTime - replyTime < 6f); } }
        public WorldOptions EffectiveGlobal
        {
            get
            {
                if (!p.Active || !Plugin.Connected) return empty;
                if (!Plugin.IsHost) return Available ? reportedGlobal : empty;
                if (p.Options.World.HasGlobalChanges) return p.Options.World;
                Peer peer;
                if (p.Options.AllowGuestRequests && globalOwner.HasValue && peers.TryGetValue(globalOwner.Value, out peer)) return peer.Options;
                return empty;
            }
        }

        public WorldOptions ForPlayer(PlayerController player)
        {
            if (player == null || !p.Active || !Plugin.IsHost) return empty;
            if (player.ControllingClientIdVar.Value == Plugin.Network.LocalClientId) return p.Options.World;
            Peer peer;
            if (p.Options.AllowGuestRequests && peers.TryGetValue(player.ControllingClientIdVar.Value, out peer) && Time.unscaledTime - peer.LastSeen < 6f) return peer.Options;
            return empty;
        }

        public void Tick()
        {
            NetworkManager current = Plugin.Network;
            if (current != manager || current == null || !current.IsListening)
            {
                Dispose();
                if (current == null || !current.IsListening) { Status = "未连接房间"; return; }
                manager = current;
                manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestName, OnRequest);
                manager.CustomMessagingManager.RegisterNamedMessageHandler(ReplyName, OnReply);
                probes = 0; nextSend = 0f;
            }
            if (!Plugin.Connected) return;
            if (manager.IsServer)
            {
                foreach (ulong id in peers.Keys.ToArray())
                    if (!p.Active || !p.Options.AllowGuestRequests || Time.unscaledTime - peers[id].LastSeen > 6f || !manager.ConnectedClientsIds.Contains(id)) peers.Remove(id);
                if (globalOwner.HasValue && (!peers.ContainsKey(globalOwner.Value) || !peers[globalOwner.Value].Options.HasGlobalChanges)) globalOwner = null;
                if (!globalOwner.HasValue)
                    foreach (KeyValuePair<ulong, Peer> pair in peers) if (pair.Value.Options.HasGlobalChanges) { globalOwner = pair.Key; break; }
                Status = !p.Active ? "房主 · 总开关已关闭" : p.Options.AllowGuestRequests ? "房主 · 已允许客机协同" : "房主 · 本机功能可用";
                return;
            }
            if (Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + 2f;
            if (hostFound || probes < 3)
            {
                probes++;
                SendRequest(new BridgePacket { Kind = hostFound ? "settings" : "hello", Options = p.Active ? p.Options.World : new WorldOptions() });
                if (!hostFound) Status = "检测房主协同支持（" + probes + "/3）";
            }
            else Status = "房主未安装协同插件；本机功能仍可用";
            if (hostFound && Time.unscaledTime - replyTime >= 6f) { permission = false; Status = "房主协同响应超时"; }
        }

        public void MarkDirty() { nextSend = Mathf.Min(nextSend, Time.unscaledTime + 0.3f); }
        public void DetectAgain() { probes = 0; nextSend = 0f; p.Notify("将重新检测房主协同支持"); }
        public void ResetRequests()
        {
            peers.Clear(); globalOwner = null; reportedGlobal = new WorldOptions();
            if (manager != null && manager.IsListening && !manager.IsServer && hostFound)
                SendRequest(new BridgePacket { Kind = "settings", Options = new WorldOptions() });
            nextSend = 0f;
        }
        public void Spawn(string item, int count)
        {
            if (!p.Active) throw new InvalidOperationException("请先开启总开关");
            if (!Available) throw new InvalidOperationException("此功能需要房主安装修改器并开启客机协同");
            if (Plugin.IsHost) p.Notify(p.World.Spawn(manager.LocalClientId, item, count));
            else { SendRequest(new BridgePacket { Kind = "spawn", Item = item, Count = Limits.Int(count, 1, 10) }); p.Notify("物品生成请求已发送，等待房主确认"); }
        }

        private void SendRequest(BridgePacket packet)
        {
            if (manager == null || !manager.IsListening) return;
            packet.Sequence = ++sequence;
            Send(RequestName, NetworkManager.ServerClientId, packet);
        }
        private void Send(string name, ulong destination, BridgePacket packet)
        {
            string json = SafeFiles.Json(packet);
            using (FastBufferWriter writer = new FastBufferWriter(json.Length * 2 + 16, Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                manager.CustomMessagingManager.SendNamedMessage(name, destination, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private static BridgePacket Read(FastBufferReader reader)
        {
            if (reader.Length > 8192) throw new InvalidOperationException("协同消息过大");
            string json; reader.ReadValueSafe(out json, false);
            BridgePacket packet = SafeFiles.Parse<BridgePacket>(json);
            if (packet == null || packet.Version != 1) throw new InvalidOperationException("协同协议版本不匹配");
            return packet;
        }
        private void OnRequest(ulong sender, FastBufferReader reader)
        {
            if (manager == null || !manager.IsServer || !manager.ConnectedClientsIds.Contains(sender)) return;
            float previous;
            if (lastRequest.TryGetValue(sender, out previous) && Time.unscaledTime - previous < 0.15f) return;
            lastRequest[sender] = Time.unscaledTime;
            try
            {
                BridgePacket request = Read(reader);
                if (request.Kind != "hello" && request.Kind != "settings" && request.Kind != "spawn") return;
                bool allowed = p.Active && p.Options.AllowGuestRequests;
                string message = allowed ? "房主协同已连接" : "房主已安装修改器，但尚未开启客机协同";
                bool globalAccepted = false;
                Peer peer;
                if (allowed)
                {
                    if (!peers.TryGetValue(sender, out peer)) { peer = new Peer(); peers[sender] = peer; }
                    peer.LastSeen = Time.unscaledTime;
                    if (request.Kind == "spawn")
                    {
                        if (Time.unscaledTime < peer.NextSpawn) message = "请等待 3 秒后再生成物品";
                        else
                        {
                            peer.NextSpawn = Time.unscaledTime + 3f;
                            try { message = p.World.Spawn(sender, request.Item, request.Count); }
                            catch (Exception e) { message = "生成失败：" + e.Message; }
                        }
                    }
                    else
                    {
                        peer.Options = request.Options ?? new WorldOptions(); peer.Options.Normalize();
                        if (!peer.Options.HasGlobalChanges && globalOwner == sender) globalOwner = null;
                        if (peer.Options.HasGlobalChanges && !globalOwner.HasValue && !p.Options.World.HasGlobalChanges) globalOwner = sender;
                    }
                    globalAccepted = !peer.Options.HasGlobalChanges || (!p.Options.World.HasGlobalChanges && globalOwner == sender);
                    if (!globalAccepted) message = "个人功能已同步；全局 AI／任务设置正由房主或其他客机控制";
                }
                Send(ReplyName, sender, new BridgePacket
                {
                    Kind = request.Kind == "spawn" ? "spawnReply" : "reply", Sequence = request.Sequence,
                    Accepted = allowed, GlobalAccepted = globalAccepted, Message = message, Options = EffectiveGlobal
                });
            }
            catch (Exception e) { p.ReportError("协同请求", e); }
        }
        private void OnReply(ulong sender, FastBufferReader reader)
        {
            if (manager == null || manager.IsServer || sender != NetworkManager.ServerClientId) return;
            try
            {
                BridgePacket packet = Read(reader);
                if (packet.Kind != "reply" && packet.Kind != "spawnReply") return;
                hostFound = true; replyTime = Time.unscaledTime; permission = packet.Accepted;
                GlobalAccepted = packet.GlobalAccepted;
                reportedGlobal = packet.Options ?? new WorldOptions(); reportedGlobal.Normalize();
                Status = packet.Message;
                if (packet.Kind == "spawnReply") p.Notify(packet.Message);
            }
            catch (Exception e) { p.ReportError("协同响应", e); }
        }

        public void Dispose()
        {
            if (manager != null && manager.CustomMessagingManager != null)
            {
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(RequestName);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(ReplyName);
            }
            manager = null; peers.Clear(); lastRequest.Clear(); globalOwner = null;
            permission = hostFound = GlobalAccepted = false; replyTime = -100f; reportedGlobal = new WorldOptions();
        }
    }
}
