using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RatAssist
{
    public sealed class MarkerMessage
    {
        public int Version=1;
        public string Kind,Id;
        public ulong Origin,Peer;
        public int Count;
        public PingPacket Packet;
    }

    // Uses the game's established connection. The host validates and forwards only to rats.
    // A receipt is produced by a receiving plugin, not by the send API.
    public sealed class NetworkMarkers : IDisposable
    {
        public const string RequestName="RatAssist.markers.1.request",ReplyName="RatAssist.markers.1.reply";
        private sealed class Delivery
        {
            public string Id;
            public int Count;
            public readonly HashSet<ulong> Targets=new HashSet<ulong>();
        }
        private readonly TeamPings owner;
        private readonly JsonSerializerSettings json=new JsonSerializerSettings { TypeNameHandling=TypeNameHandling.None,MaxDepth=10 };
        private NetworkManager manager;
        private readonly Dictionary<ulong,float> peers=new Dictionary<ulong,float>(),lastState=new Dictionary<ulong,float>();
        private readonly Dictionary<ulong,Delivery> deliveries=new Dictionary<ulong,Delivery>();
        private readonly HashSet<ulong> confirmations=new HashSet<ulong>();
        private float nextHello,helloAt=-100,pruneAt,errorAt;
        private string ownId;
        public int Received,Sent;
        public int Confirmations { get { return confirmations.Count; } }
        public bool Ready { get { return manager!=null&&manager.IsListening&&manager.IsConnectedClient&&(manager.IsServer||Time.unscaledTime-helloAt<8); } }
        public NetworkMarkers(TeamPings pings) { owner=pings; }
        public void Tick()
        {
            try
            {
                NetworkManager current=AssistRuntime.Network;
                if(current!=manager||current==null||!current.IsListening)
                {
                    Dispose();
                    if(current==null||!current.IsListening||current.CustomMessagingManager==null) return;
                    manager=current;
                    manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestName,OnRequest);
                    manager.CustomMessagingManager.RegisterNamedMessageHandler(ReplyName,OnReply);
                    nextHello=0;
                }
                if(!manager.IsConnectedClient) return;
                if(manager.IsServer)
                {
                    if(Time.unscaledTime<pruneAt) return;
                    pruneAt=Time.unscaledTime+1;
                    foreach(ulong id in peers.Keys.ToArray())
                        if(!manager.ConnectedClientsIds.Contains(id)||Time.unscaledTime-peers[id]>8)
                        { peers.Remove(id); deliveries.Remove(id); lastState.Remove(id); }
                }
                else if(Time.unscaledTime>=nextHello)
                { nextHello=Time.unscaledTime+2; Send(RequestName,NetworkManager.ServerClientId,new MarkerMessage { Kind="hello" }); }
            }
            catch(Exception e) { Error(e); }
        }
        public void Begin(string id) { ownId=id; confirmations.Clear(); }
        public bool Publish(string id,PingPacket packet)
        {
            if(!Ready) return false;
            try
            {
                if(ownId!=id) Begin(id);
                if(manager.IsServer) Route(manager.LocalClientId,id,packet);
                else Send(RequestName,NetworkManager.ServerClientId,new MarkerMessage { Kind="state",Id=id,Packet=packet });
                return true;
            }
            catch(Exception e) { Error(e); return false; }
        }
        public void Clear()
        {
            ownId=null; confirmations.Clear();
            if(!Ready) return;
            try
            {
                if(manager.IsServer) RouteClear(manager.LocalClientId);
                else Send(RequestName,NetworkManager.ServerClientId,new MarkerMessage { Kind="clear" });
            }
            catch(Exception e) { Error(e); }
        }
        private bool Rat(ulong id)
        {
            PlayerController player=GameManager.instance==null?null:GameManager.instance.GetControlledPlayerControllerForClient(id);
            return AssistRuntime.IsCurrent(player)&&AssistRuntime.SideOf(player)==Side.Rats;
        }
        private bool CurrentPeer(ulong id)
        { float seen; return peers.TryGetValue(id,out seen)&&Time.unscaledTime-seen<8&&manager.ConnectedClientsIds.Contains(id); }
        private void Route(ulong sender,string id,PingPacket packet)
        {
            Guid parsed;
            string rejection=owner.CheckPacket(packet,sender);
            if(!Guid.TryParseExact(id,"N",out parsed)||rejection!=null)
            { owner.Reject("network-host:"+(rejection??"invalid-state-id")); return; }
            Delivery delivery;
            if(!deliveries.TryGetValue(sender,out delivery)||delivery.Id!=id)
            { delivery=new Delivery { Id=id,Count=packet.Points.Count }; deliveries[sender]=delivery; }
            if(sender!=manager.LocalClientId&&Rat(manager.LocalClientId))
            {
                int count=owner.ReceiveNetwork(packet);
                if(count>=0) Receipt(sender,manager.LocalClientId,id,count);
            }
            foreach(ulong target in peers.Keys.ToArray())
            {
                if(target==sender||target==manager.LocalClientId||!CurrentPeer(target)||!Rat(target)) continue;
                delivery.Targets.Add(target);
                Send(ReplyName,target,new MarkerMessage { Kind="state",Id=id,Origin=sender,Packet=packet });
            }
        }
        private void RouteClear(ulong sender)
        {
            deliveries.Remove(sender); owner.ClearNetwork(sender);
            foreach(ulong target in peers.Keys.ToArray())
                if(target!=sender&&target!=manager.LocalClientId&&CurrentPeer(target)&&Rat(target))
                    Send(ReplyName,target,new MarkerMessage { Kind="clear",Origin=sender });
        }
        private void OnRequest(ulong sender,FastBufferReader reader)
        {
            if(manager==null||!manager.IsServer||!manager.ConnectedClientsIds.Contains(sender)) return;
            try
            {
                MarkerMessage message=Read(reader);
                if(message.Kind=="hello")
                {
                    float seen;
                    if(peers.TryGetValue(sender,out seen)&&Time.unscaledTime-seen<.5f) return;
                    peers[sender]=Time.unscaledTime;
                    Send(ReplyName,sender,new MarkerMessage { Kind="helloAck" });
                }
                else if(message.Kind=="state")
                {
                    float last;
                    if(lastState.TryGetValue(sender,out last)&&Time.unscaledTime-last<.2f) return;
                    lastState[sender]=Time.unscaledTime;
                    Route(sender,message.Id,message.Packet);
                }
                else if(message.Kind=="clear") RouteClear(sender);
                else if(message.Kind=="receipt")
                {
                    Delivery pending;
                    if(deliveries.TryGetValue(message.Origin,out pending)&&pending.Id==message.Id&&pending.Targets.Contains(sender)&&
                        CurrentPeer(sender)&&Rat(sender)&&message.Count>=0&&message.Count<=pending.Count)
                        Receipt(message.Origin,sender,message.Id,message.Count);
                }
            }
            catch(Exception e) { Error(e); }
        }
        private void OnReply(ulong sender,FastBufferReader reader)
        {
            if(manager==null||manager.IsServer||sender!=NetworkManager.ServerClientId) return;
            try
            {
                MarkerMessage message=Read(reader);
                if(message.Kind=="helloAck")
                {
                    if(!Ready) owner.Trace("游戏联机标点通道已连接。");
                    helloAt=Time.unscaledTime;
                }
                else if(message.Kind=="state"&&message.Packet!=null&&message.Origin==message.Packet.Client)
                {
                    int count=owner.ReceiveNetwork(message.Packet);
                    if(count>=0)
                    { Received++; Send(RequestName,NetworkManager.ServerClientId,new MarkerMessage { Kind="receipt",Origin=message.Origin,Id=message.Id,Count=count }); }
                }
                else if(message.Kind=="clear") owner.ClearNetwork(message.Origin);
                else if(message.Kind=="receipt"&&message.Origin==manager.LocalClientId) Confirm(message.Peer,message.Id,message.Count);
            }
            catch(Exception e) { Error(e); }
        }
        private void Receipt(ulong origin,ulong receiver,string id,int count)
        {
            if(origin==manager.LocalClientId) Confirm(receiver,id,count);
            else if(manager.ConnectedClientsIds.Contains(origin))
                Send(ReplyName,origin,new MarkerMessage { Kind="receipt",Origin=origin,Peer=receiver,Id=id,Count=count });
        }
        private void Confirm(ulong receiver,string id,int count)
        {
            if(id!=ownId||receiver==manager.LocalClientId||count<=0||!Rat(receiver)||!confirmations.Add(receiver)) return;
            owner.Confirmed(confirmations.Count);
        }
        private void Send(string name,ulong target,MarkerMessage message)
        {
            string data=JsonConvert.SerializeObject(message,json);
            if(data.Length>4096) throw new InvalidOperationException("marker-message-too-large");
            using(FastBufferWriter writer=new FastBufferWriter(data.Length*2+16,Allocator.Temp))
            {
                writer.WriteValueSafe(data);
                manager.CustomMessagingManager.SendNamedMessage(name,target,writer,NetworkDelivery.ReliableFragmentedSequenced);
                Sent++;
            }
        }
        private MarkerMessage Read(FastBufferReader reader)
        {
            if(reader.Length>8208) throw new InvalidOperationException("marker-message-too-large");
            string data; reader.ReadValueSafe(out data,false);
            if(data==null||data.Length>4096) throw new InvalidOperationException("marker-message-too-large");
            MarkerMessage message=JsonConvert.DeserializeObject<MarkerMessage>(data,json);
            if(message==null||message.Version!=1) throw new InvalidOperationException("marker-message-version");
            return message;
        }
        private void Error(Exception e)
        { if(Time.unscaledTime>=errorAt) { errorAt=Time.unscaledTime+5; owner.Reject("network-error:"+e.GetBaseException().GetType().Name); } }
        public void Dispose()
        {
            if(manager!=null&&manager.CustomMessagingManager!=null)
            {
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(RequestName);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(ReplyName);
            }
            manager=null; peers.Clear(); deliveries.Clear(); lastState.Clear(); confirmations.Clear(); ownId=null; helloAt=-100;
        }
    }
}
