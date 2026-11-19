using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RatAssist
{
    public sealed class PingMark
    {
        public PingPoint Point;
        public ulong Client,Actor;
        public Side Side;
        public Vector3 Position
        {
            get
            {
                NetworkObject attached;
                if(Point.Attachment!=UInt64.MaxValue && AssistRuntime.Network!=null && AssistRuntime.Network.SpawnManager!=null &&
                    AssistRuntime.Network.SpawnManager.SpawnedObjects.TryGetValue(Point.Attachment,out attached) && attached!=null && attached.IsSpawned)
                    return attached.transform.TransformPoint(new Vector3(Point.LocalX,Point.LocalY,Point.LocalZ));
                return new Vector3(Point.X,Point.Y,Point.Z);
            }
        }
        public PlayerController Owner { get { return GameManager.instance==null?null:GameManager.instance.GetControlledPlayerControllerForClient(Client); } }
    }
    public sealed class TeamPings
    {
        private readonly AssistRuntime app;
        public readonly SteamLobbySharing Sharing;
        public readonly NetworkMarkers Network;
        private readonly List<PingMark> own=new List<PingMark>();
        private readonly List<PingMark> remote=new List<PingMark>();
        private readonly Dictionary<ulong,List<PingMark>> networkRemote=new Dictionary<ulong,List<PingMark>>();
        private readonly JsonSerializerSettings json=new JsonSerializerSettings { TypeNameHandling=TypeNameHandling.None,MaxDepth=8,MissingMemberHandling=MissingMemberHandling.Ignore };
        private Lobby? advertisedLobby;
        private float nextRead,nextKey,nextPublish,nextRefresh,nextDiagnostic,pendingAt;
        private bool dirty,wasReady,wasNetworkReady,awaitingReceipt;
        private string stateId,lastSteamPayload,lastReject="none";
        private int steamMembers,steamPayloads,steamAccepted,networkAccepted,rejections;
        private static double ServerTime { get { return AssistRuntime.Network==null?0:AssistRuntime.Network.ServerTime.Time; } }
        public TeamPings(AssistRuntime runtime) { app=runtime; Sharing=new SteamLobbySharing(runtime); Network=new NetworkMarkers(this); }
        public void Clear(bool announce)
        {
            own.Clear(); remote.Clear(); networkRemote.Clear();
            if(announce) Network.Clear();
            if(announce && advertisedLobby.HasValue && SteamClient.IsValid)
            { try { advertisedLobby.Value.SetMemberData(PingProtocol.DataKey,""); } catch(Exception) { } }
            advertisedLobby=null; dirty=wasReady=wasNetworkReady=awaitingReceipt=false;
            stateId=lastSteamPayload=null; nextRead=nextPublish=nextRefresh=0;
        }
        public void Dispose() { Clear(true); Network.Dispose(); Sharing.Dispose(); }
        private Lobby? CurrentLobby
        { get { return SteamLobbySharing.CurrentLobby; } }
        public void Tick()
        {
            PlayerController local=AssistRuntime.Local;
            double now=ServerTime;
            if(own.RemoveAll(delegate(PingMark mark) { return !PingProtocol.Fresh(mark.Point,now); })>0) NewState(false);
            if(Sharing.Ready&&!wasReady) dirty=true;
            if(Network.Ready&&!wasNetworkReady) dirty=true;
            wasReady=Sharing.Ready; wasNetworkReady=Network.Ready;
            if(Network.Ready&&own.Count>0&&Time.unscaledTime>=nextRefresh) dirty=true;
            if(dirty&&(Sharing.Ready||Network.Ready)&&Time.unscaledTime>=nextPublish) Publish();
            Keyboard keyboard=Keyboard.current;
            if(app.GameVisible && Application.isFocused && keyboard!=null && keyboard[app.PingKey].wasPressedThisFrame && Time.unscaledTime>=nextKey && (app.Screen.EmoteMenu==null||!app.Screen.EmoteMenu.IsActive))
            {
                nextKey=Time.unscaledTime+.4f;
                if(keyboard.leftCtrlKey.isPressed||keyboard.rightCtrlKey.isPressed)
                { Diagnostics(true); app.Notify("标点同步："+(Network.Ready?"游戏通道已连接":"等待房主标点插件")+" · 队友确认 "+Network.Confirmations+" · 已记录诊断日志"); }
                else if(keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed)
                { own.Clear(); NewState(false); Publish(); app.Notify("已清除自己的标点"); }
                else Place(local);
            }
            if(awaitingReceipt&&Time.unscaledTime-pendingAt>8)
            { awaitingReceipt=false; app.Notify("尚未收到队友确认 · 请让房主和队友更新标点插件；Ctrl+G 查看同步状态"); Diagnostics(true); }
            Diagnostics(false);
            if(Time.unscaledTime<nextRead) return;
            nextRead=Time.unscaledTime+.5f; ReadTeam(local);
        }
        private void Place(PlayerController local)
        {
            if(local==null || GameCamera.instance==null || GameCamera.instance.Cam==null) return;
            RaycastHit[] hits=Physics.RaycastAll(GameCamera.instance.GetRay(),1000f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            Array.Sort(hits,delegate(RaycastHit a,RaycastHit b) { return a.distance.CompareTo(b.distance); });
            foreach(RaycastHit hit in hits)
            {
                if(hit.collider==null || hit.collider.GetComponentInParent<PlayerController>()==local) continue;
                Vector3 at=hit.point+hit.normal*.02f;
                if(!TeamPolicy.Finite(at.x)||!TeamPolicy.Finite(at.y)||!TeamPolicy.Finite(at.z)) continue;
                float life=TeamPolicy.Finite(app.PingLifetime)?Mathf.Clamp(app.PingLifetime,5,120):30f;
                PingPoint point=new PingPoint { Id=Guid.NewGuid().ToString("N"),X=at.x,Y=at.y,Z=at.z,CreatedServerTime=ServerTime,Lifetime=life };
                NetworkObject attachment=hit.collider.GetComponentInParent<NetworkObject>();
                if(attachment!=null&&attachment.IsSpawned)
                { Vector3 relative=attachment.transform.InverseTransformPoint(at); point.Attachment=attachment.NetworkObjectId; point.LocalX=relative.x; point.LocalY=relative.y; point.LocalZ=relative.z; }
                own.Add(new PingMark { Point=point,Client=local.ControllingClientIdVar.Value,Actor=local.NetworkObjectId,Side=AssistRuntime.SideOf(local) });
                while(own.Count>app.MaxOwnPings) own.RemoveAt(0);
                NewState(AssistRuntime.SideOf(local)==Side.Rats);
                app.Notify(AssistRuntime.SideOf(local)==Side.Chef?"已标记 · 仅自己可见":"已标记 · 等待队友接收确认");
                Publish(); return;
            }
            app.Notify("准心未命中实体表面，未创建标点");
        }
        private void Publish()
        {
            dirty=true;
            Lobby? lobby=CurrentLobby;
            PlayerController local=AssistRuntime.Local;
            // Chef marks never leave this computer.
            if(local==null||AssistRuntime.SideOf(local)!=Side.Rats||Time.unscaledTime<nextPublish) return;
            nextPublish=Time.unscaledTime+.5f;
            if(stateId==null) NewState(false);
            PingPacket packet=new PingPacket { Lobby=lobby.HasValue?(ulong)lobby.Value.Id:0,Steam=SteamClient.IsValid?(ulong)SteamClient.SteamId:0,Client=local.ControllingClientIdVar.Value,Actor=local.NetworkObjectId,
                Level=GameManager.instance.CurrentLevelIndexVar.Value,Points=own.Select(delegate(PingMark x) { return x.Point; }).ToList() };
            bool sent=Network.Publish(stateId,packet);
            nextRefresh=Time.unscaledTime+3;
            if(sent) dirty=false;
            if(!Sharing.Ready||!lobby.HasValue) return;
            string payload=JsonConvert.SerializeObject(packet,json);
            if(payload.Length>3500) { app.Notify("标点内容超过限制，未同步"); return; }
            try
            {
                if(payload!=lastSteamPayload) lobby.Value.SetMemberData(PingProtocol.DataKey,payload);
                advertisedLobby=lobby; lastSteamPayload=payload; dirty=false;
            }
            catch(Exception e) { nextPublish=Time.unscaledTime+2; if(app.Log!=null) app.Log.LogWarning("标点发送暂未完成，将重试："+e.GetBaseException().Message); }
        }
        private void ReadTeam(PlayerController local)
        {
            remote.Clear(); Lobby? lobby=CurrentLobby;
            if(local==null || AssistRuntime.SideOf(local)!=Side.Rats || !lobby.HasValue || !Sharing.Ready) return;
            Friend[] members;
            try { members=lobby.Value.Members.ToArray(); steamMembers=members.Length; }
            catch(Exception e) { Reject("steam-members:"+e.GetType().Name); return; }
            foreach(Friend member in members)
            {
                if(member.Id==SteamClient.SteamId) continue;
                try
                {
                    string payload=lobby.Value.GetMemberData(member,PingProtocol.DataKey);
                    if(String.IsNullOrEmpty(payload)) continue;
                    steamPayloads++;
                    if(payload.Length>4096) { Reject("steam-payload-size"); continue; }
                    PingPacket packet=JsonConvert.DeserializeObject<PingPacket>(payload,json);
                    if(packet==null) { Reject("steam-null-packet"); continue; }
                    PlayerController owner=GameManager.instance.GetControlledPlayerControllerForClient(packet.Client);
                    if(owner==null||!PingProtocol.Accept(packet,(ulong)lobby.Value.Id,(ulong)member.Id,GameManager.instance.CurrentLevelIndexVar.Value,
                        local.ControllingClientIdVar.Value,owner.ControllingClientIdVar.Value,owner.NetworkObjectId,AssistRuntime.SideOf(owner),AssistRuntime.IsCurrent(owner))||
                        !AssistRuntime.SameTeam(local,owner)) { Reject("steam:"+(CheckPacket(packet,packet.Client)??"lobby-member-or-self")); continue; }
                    // Display names and local active-scene names are not stable network identities.
                    foreach(PingPoint point in packet.Points)
                    {
                        if(!PingProtocol.Fresh(point,ServerTime)) { Reject("steam-point-expired-or-invalid"); continue; }
                        remote.Add(new PingMark { Point=point,Client=packet.Client,Actor=packet.Actor,Side=Side.Rats });
                        steamAccepted++;
                    }
                }
                catch(Exception e) { Reject("steam-read:"+e.GetType().Name); }
            }
        }
        private void NewState(bool notify)
        {
            stateId=Guid.NewGuid().ToString("N"); dirty=true; awaitingReceipt=notify; pendingAt=Time.unscaledTime;
            Network.Begin(stateId);
        }
        public string CheckPacket(PingPacket packet,ulong sender)
        {
            if(packet==null||packet.Version!=2||packet.Points==null||packet.Points.Count>5) return "packet-format";
            if(!app.Playing||GameManager.instance==null) return "not-playing";
            if(packet.Client!=sender) return "sender-client";
            if(packet.Level!=GameManager.instance.CurrentLevelIndexVar.Value) return "level";
            PlayerController actor=GameManager.instance.GetControlledPlayerControllerForClient(sender);
            if(!AssistRuntime.IsCurrent(actor)) return "actor-missing";
            if(actor.NetworkObjectId!=packet.Actor) return "actor-id";
            if(AssistRuntime.SideOf(actor)!=Side.Rats) return "sender-not-rat";
            return null;
        }
        public int ReceiveNetwork(PingPacket packet)
        {
            if(packet==null) return -1;
            string rejection=CheckPacket(packet,packet.Client);
            PlayerController local=AssistRuntime.Local;
            if(rejection!=null) { Reject("network-receive:"+rejection); return -1; }
            if(local==null||AssistRuntime.SideOf(local)!=Side.Rats||packet.Client==local.ControllingClientIdVar.Value) return -1;
            List<PingMark> marks=new List<PingMark>();
            foreach(PingPoint point in packet.Points)
            {
                if(!PingProtocol.Fresh(point,ServerTime)) { Reject("network-point-expired-or-invalid"); continue; }
                marks.Add(new PingMark { Point=point,Client=packet.Client,Actor=packet.Actor,Side=Side.Rats });
            }
            networkRemote[packet.Client]=marks; networkAccepted+=marks.Count;
            return marks.Count;
        }
        public void ClearNetwork(ulong sender) { networkRemote.Remove(sender); }
        public void Confirmed(int count)
        {
            if(awaitingReceipt) { app.Notify("队友已收到标点 · "+count+" 人确认"); awaitingReceipt=false; }
            Trace("队友接收确认："+count+"；状态="+stateId);
        }
        public void Trace(string message) { if(app.Log!=null) app.Log.LogInfo(message); }
        public void Reject(string reason) { rejections++; lastReject=reason; }
        private void Diagnostics(bool force)
        {
            if(!force&&Time.unscaledTime<nextDiagnostic) return;
            nextDiagnostic=Time.unscaledTime+10;
            Trace("[标点诊断 1.0.2] network="+Network.Ready+"; sent="+Network.Sent+"; receivedMarks="+networkAccepted+
                "; confirmedPeers="+Network.Confirmations+"; steamReady="+Sharing.Ready+"; steamMembers="+steamMembers+
                "; steamPayloads="+steamPayloads+"; steamAccepted="+steamAccepted+"; dropped="+rejections+"; lastDrop="+lastReject+
                "; host="+(AssistRuntime.Network!=null&&AssistRuntime.Network.IsServer)+
                "; client="+(AssistRuntime.Network==null?"none":AssistRuntime.Network.LocalClientId.ToString())+
                "; level="+(GameManager.instance==null?-1:GameManager.instance.CurrentLevelIndexVar.Value)+
                "; side="+AssistRuntime.SideOf(AssistRuntime.Local)+"; own="+own.Count+"; visible="+Visible().Count());
        }
        public IEnumerable<PingMark> Visible()
        {
            PlayerController local=AssistRuntime.Local;
            if(local==null) yield break;
            HashSet<string> shown=new HashSet<string>();
            foreach(PingMark mark in own.Concat(networkRemote.Values.SelectMany(delegate(List<PingMark> marks) { return marks; })).Concat(remote))
            {
                PlayerController owner=mark.Owner;
                if(!AssistRuntime.IsCurrent(owner)||owner.NetworkObjectId!=mark.Actor||!AssistRuntime.SameTeam(local,owner)||
                    !TeamPolicy.CanSee(AssistRuntime.SideOf(local),local.ControllingClientIdVar.Value,mark.Side,mark.Client)||
                    !PingProtocol.Fresh(mark.Point,ServerTime)||!shown.Add(mark.Point.Id)) continue;
                yield return mark;
            }
        }
    }
}
