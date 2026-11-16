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
        private readonly List<PingMark> own=new List<PingMark>();
        private readonly List<PingMark> remote=new List<PingMark>();
        private readonly JsonSerializerSettings json=new JsonSerializerSettings { TypeNameHandling=TypeNameHandling.None,MaxDepth=8,MissingMemberHandling=MissingMemberHandling.Ignore };
        private Lobby? advertisedLobby;
        private float nextRead,nextKey,nextPublish;
        private bool dirty,wasReady;
        private static double ServerTime { get { return AssistRuntime.Network==null?0:AssistRuntime.Network.ServerTime.Time; } }
        public TeamPings(AssistRuntime runtime) { app=runtime; Sharing=new SteamLobbySharing(runtime); }
        public void Clear(bool announce)
        {
            own.Clear(); remote.Clear();
            if(announce && advertisedLobby.HasValue && SteamClient.IsValid)
            { try { advertisedLobby.Value.SetMemberData(PingProtocol.DataKey,""); } catch(Exception) { } }
            advertisedLobby=null; dirty=wasReady=false; nextRead=nextPublish=0;
        }
        public void Dispose() { Clear(true); Sharing.Dispose(); }
        private Lobby? CurrentLobby
        { get { return SteamLobbySharing.CurrentLobby; } }
        public void Tick()
        {
            PlayerController local=AssistRuntime.Local;
            double now=ServerTime;
            if(own.RemoveAll(delegate(PingMark mark) { return !PingProtocol.Fresh(mark.Point,now); })>0) dirty=true;
            if(Sharing.Ready&&!wasReady) dirty=true;
            wasReady=Sharing.Ready;
            if(dirty&&Sharing.Ready&&Time.unscaledTime>=nextPublish) Publish();
            Keyboard keyboard=Keyboard.current;
            if(app.GameVisible && Application.isFocused && keyboard!=null && keyboard[app.PingKey].wasPressedThisFrame && Time.unscaledTime>=nextKey && (app.Screen.EmoteMenu==null||!app.Screen.EmoteMenu.IsActive))
            {
                nextKey=Time.unscaledTime+.4f;
                if(keyboard.leftShiftKey.isPressed||keyboard.rightShiftKey.isPressed)
                { own.Clear(); Publish(); app.Notify("已清除自己的标点"); }
                else Place(local);
            }
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
                Publish(); app.Notify(AssistRuntime.SideOf(local)==Side.Chef?"已标记 · 仅自己可见":Sharing.Ready?"已提交阵营同步 · 队友需更新标点插件":CurrentLobby.HasValue?"已标记 · 正在连接标点同步":"已标记 · 当前为本机标点"); return;
            }
            app.Notify("准心未命中实体表面，未创建标点");
        }
        private void Publish()
        {
            dirty=true;
            Lobby? lobby=CurrentLobby;
            PlayerController local=AssistRuntime.Local;
            // Chef marks never leave this computer.
            if(!Sharing.Ready||!lobby.HasValue||local==null||AssistRuntime.SideOf(local)!=Side.Rats||Time.unscaledTime<nextPublish) return;
            nextPublish=Time.unscaledTime+.5f;
            PingPacket packet=new PingPacket { Lobby=(ulong)lobby.Value.Id,Steam=(ulong)SteamClient.SteamId,Client=local.ControllingClientIdVar.Value,Actor=local.NetworkObjectId,
                Level=GameManager.instance.CurrentLevelIndexVar.Value,Points=own.Select(delegate(PingMark x) { return x.Point; }).ToList() };
            string payload=JsonConvert.SerializeObject(packet,json);
            if(payload.Length>3500) { app.Notify("标点内容超过限制，未同步"); return; }
            try { lobby.Value.SetMemberData(PingProtocol.DataKey,payload); advertisedLobby=lobby; dirty=false; }
            catch(Exception e) { nextPublish=Time.unscaledTime+2; if(app.Log!=null) app.Log.LogWarning("标点发送暂未完成，将重试："+e.GetBaseException().Message); }
        }
        private void ReadTeam(PlayerController local)
        {
            remote.Clear(); Lobby? lobby=CurrentLobby;
            if(local==null || AssistRuntime.SideOf(local)!=Side.Rats || !lobby.HasValue || !Sharing.Ready) return;
            foreach(Friend member in lobby.Value.Members)
            {
                if(member.Id==SteamClient.SteamId) continue;
                string payload=lobby.Value.GetMemberData(member,PingProtocol.DataKey);
                if(String.IsNullOrEmpty(payload)||payload.Length>4096) continue;
                try
                {
                    PingPacket packet=JsonConvert.DeserializeObject<PingPacket>(payload,json);
                    if(packet==null) continue;
                    PlayerController owner=GameManager.instance.GetControlledPlayerControllerForClient(packet.Client);
                    if(owner==null||!PingProtocol.Accept(packet,(ulong)lobby.Value.Id,(ulong)member.Id,GameManager.instance.CurrentLevelIndexVar.Value,
                        local.ControllingClientIdVar.Value,owner.ControllingClientIdVar.Value,owner.NetworkObjectId,AssistRuntime.SideOf(owner),AssistRuntime.IsCurrent(owner))||
                        owner.OwnerClientId!=packet.Client||!AssistRuntime.SameTeam(local,owner)) continue;
                    // Display names and local active-scene names are not stable network identities.
                    foreach(PingPoint point in packet.Points)
                    {
                        if(!PingProtocol.Fresh(point,ServerTime)) continue;
                        remote.Add(new PingMark { Point=point,Client=packet.Client,Actor=packet.Actor,Side=Side.Rats });
                    }
                }
                catch(JsonException) { }
            }
        }
        public IEnumerable<PingMark> Visible()
        {
            PlayerController local=AssistRuntime.Local;
            if(local==null) yield break;
            foreach(PingMark mark in own.Concat(remote))
            {
                PlayerController owner=mark.Owner;
                if(!AssistRuntime.IsCurrent(owner)||owner.NetworkObjectId!=mark.Actor||!AssistRuntime.SameTeam(local,owner)||
                    !TeamPolicy.CanSee(AssistRuntime.SideOf(local),local.ControllingClientIdVar.Value,mark.Side,mark.Client)||
                    !PingProtocol.Fresh(mark.Point,ServerTime)) continue;
                yield return mark;
            }
        }
    }
}
