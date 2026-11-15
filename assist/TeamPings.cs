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
    public sealed class PingPoint
    {
        public string Id;
        public float X,Y,Z;
        public ulong Attachment=UInt64.MaxValue;
        public float LocalX,LocalY,LocalZ;
        public long CreatedUtc;
        public float Lifetime;
    }
    public sealed class PingPacket
    {
        public int Version=1;
        public ulong Lobby,Client,Actor;
        public string Scene;
        public List<PingPoint> Points=new List<PingPoint>();
    }
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
        private const string DataKey="ratassist.markers.v1";
        private readonly AssistRuntime app;
        private readonly List<PingMark> own=new List<PingMark>();
        private readonly List<PingMark> remote=new List<PingMark>();
        private readonly JsonSerializerSettings json=new JsonSerializerSettings { TypeNameHandling=TypeNameHandling.None,MaxDepth=8,MissingMemberHandling=MissingMemberHandling.Ignore };
        private Lobby? advertisedLobby;
        private float nextRead,nextKey;
        public TeamPings(AssistRuntime runtime) { app=runtime; }
        public void Clear(bool announce)
        {
            own.Clear(); remote.Clear();
            if(announce && advertisedLobby.HasValue && SteamClient.IsValid)
            { try { advertisedLobby.Value.SetMemberData(DataKey,""); } catch(Exception) { } }
            advertisedLobby=null;
        }
        private Lobby? CurrentLobby
        { get { return SteamClient.IsValid && SteamNetworkManager.instance!=null ? SteamNetworkManager.instance.CurrentLobby : null; } }
        public void Tick()
        {
            PlayerController local=AssistRuntime.Local;
            long now=DateTime.UtcNow.Ticks;
            bool changed=own.RemoveAll(delegate(PingMark mark) { return !TeamPolicy.Fresh(mark.Point.CreatedUtc,now,mark.Point.Lifetime); })>0;
            if(changed) Publish();
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
                PingPoint point=new PingPoint { Id=Guid.NewGuid().ToString("N"),X=at.x,Y=at.y,Z=at.z,CreatedUtc=DateTime.UtcNow.Ticks,Lifetime=life };
                NetworkObject attachment=hit.collider.GetComponentInParent<NetworkObject>();
                if(attachment!=null&&attachment.IsSpawned)
                { Vector3 relative=attachment.transform.InverseTransformPoint(at); point.Attachment=attachment.NetworkObjectId; point.LocalX=relative.x; point.LocalY=relative.y; point.LocalZ=relative.z; }
                own.Add(new PingMark { Point=point,Client=local.ControllingClientIdVar.Value,Actor=local.NetworkObjectId,Side=AssistRuntime.SideOf(local) });
                while(own.Count>app.MaxOwnPings) own.RemoveAt(0);
                Publish(); app.Notify(AssistRuntime.SideOf(local)==Side.Chef?"已标记 · 仅自己可见":CurrentLobby.HasValue?"已标记 · 安装标点插件的小鼠队友可见":"已标记 · 当前为本机标点"); return;
            }
            app.Notify("准心未命中实体表面，未创建标点");
        }
        private void Publish()
        {
            Lobby? lobby=CurrentLobby;
            PlayerController local=AssistRuntime.Local;
            // Chef marks never leave this computer.
            if(!lobby.HasValue || local==null || AssistRuntime.SideOf(local)!=Side.Rats) return;
            PingPacket packet=new PingPacket { Lobby=(ulong)lobby.Value.Id,Client=local.ControllingClientIdVar.Value,Actor=local.NetworkObjectId,
                Scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,Points=own.Select(delegate(PingMark x) { return x.Point; }).ToList() };
            string payload=JsonConvert.SerializeObject(packet,json);
            if(payload.Length>3500) { app.Notify("标点内容超过限制，未同步"); return; }
            lobby.Value.SetMemberData(DataKey,payload); advertisedLobby=lobby;
        }
        private void ReadTeam(PlayerController local)
        {
            remote.Clear(); Lobby? lobby=CurrentLobby;
            if(local==null || AssistRuntime.SideOf(local)!=Side.Rats || !lobby.HasValue) return;
            string scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            foreach(Friend member in lobby.Value.Members)
            {
                if(member.Id==SteamClient.SteamId) continue;
                string payload=lobby.Value.GetMemberData(member,DataKey);
                if(String.IsNullOrEmpty(payload)||payload.Length>4096) continue;
                try
                {
                    PingPacket packet=JsonConvert.DeserializeObject<PingPacket>(payload,json);
                    if(packet==null||packet.Version!=1||packet.Lobby!=(ulong)lobby.Value.Id||packet.Scene!=scene||packet.Points==null||packet.Points.Count>5) continue;
                    PlayerController owner=GameManager.instance.GetControlledPlayerControllerForClient(packet.Client);
                    if(!AssistRuntime.IsCurrent(owner)||owner.NetworkObjectId!=packet.Actor||!AssistRuntime.SameTeam(local,owner)||packet.Client==local.ControllingClientIdVar.Value) continue;
                    // Tie the Steam-owned member record to the game's Steam display name as well as the live controller.
                    if(!String.Equals(owner.UserNameVar.Value.ToString(),member.Name,StringComparison.Ordinal)) continue;
                    foreach(PingPoint point in packet.Points)
                    {
                        Guid id;
                        if(point==null||!Guid.TryParseExact(point.Id,"N",out id)||!TeamPolicy.Finite(point.X)||!TeamPolicy.Finite(point.Y)||!TeamPolicy.Finite(point.Z)||
                            !TeamPolicy.Finite(point.LocalX)||!TeamPolicy.Finite(point.LocalY)||!TeamPolicy.Finite(point.LocalZ)||!TeamPolicy.Finite(point.Lifetime)||point.Lifetime<5||point.Lifetime>120||
                            !TeamPolicy.Fresh(point.CreatedUtc,DateTime.UtcNow.Ticks,point.Lifetime)) continue;
                        remote.Add(new PingMark { Point=point,Client=packet.Client,Actor=packet.Actor,Side=Side.Rats });
                    }
                }
                catch(JsonException) { }
            }
        }
        public IEnumerable<PingMark> Visible()
        {
            PlayerController local=AssistRuntime.Local;
            foreach(PingMark mark in own.Concat(remote))
            {
                PlayerController owner=mark.Owner;
                if(!AssistRuntime.IsCurrent(owner)||owner.NetworkObjectId!=mark.Actor||!AssistRuntime.SameTeam(local,owner)||
                    !TeamPolicy.CanSee(AssistRuntime.SideOf(local),local.ControllingClientIdVar.Value,mark.Side,mark.Client)||
                    !TeamPolicy.Fresh(mark.Point.CreatedUtc,DateTime.UtcNow.Ticks,mark.Point.Lifetime)) continue;
                yield return mark;
            }
        }
    }
}
