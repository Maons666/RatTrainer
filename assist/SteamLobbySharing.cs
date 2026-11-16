using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace RatAssist
{
    public sealed class SteamLobbySharing
    {
        private static SteamLobbySharing hookOwner;
        private readonly AssistRuntime app;
        private readonly LobbyJoinGuard guard=new LobbyJoinGuard();
        private Harmony harmony;
        private Task<Lobby?> joining;
        private float nextCheck,nextError;
        public bool Ready { get; private set; }
        public SteamLobbySharing(AssistRuntime runtime) { app=runtime; }
        public static Lobby? CurrentLobby
        { get { return SteamClient.IsValid&&SteamNetworkManager.instance!=null?SteamNetworkManager.instance.CurrentLobby:null; } }
        private static int NetworkId { get { return AssistRuntime.Network==null?0:AssistRuntime.Network.GetInstanceID(); } }
        private static bool Connected { get { return AssistRuntime.Network!=null&&AssistRuntime.Network.IsListening&&AssistRuntime.Network.IsConnectedClient; } }
        public void Tick()
        {
            try { TickCore(); }
            catch(Exception e)
            {
                Ready=false;
                if(Time.unscaledTime>=nextError) { nextError=Time.unscaledTime+10; Log("标点同步暂不可用："+e.GetBaseException().Message); }
            }
        }
        private void TickCore()
        {
            Lobby? lobby=CurrentLobby;
            ulong current=lobby.HasValue?(ulong)lobby.Value.Id:0;
            if(joining!=null&&joining.IsCompleted)
            {
                try
                {
                    Lobby? result=joining.GetAwaiter().GetResult();
                    if(result.HasValue&&(!Connected||NetworkId!=guard.Network||current!=guard.Lobby)) result.Value.Leave();
                    else if(!result.HasValue) Log("未能加入标点同步大厅，稍后重试。");
                }
                catch(Exception e) { Log("标点同步大厅加入失败："+e.GetBaseException().Message); }
                joining=null; guard.Finish(Time.unscaledTime); nextCheck=0;
            }
            if(!Connected||!app.Playing||AssistRuntime.SideOf(AssistRuntime.Local)!=Side.Rats||!lobby.HasValue)
            { Ready=false; return; }
            if(Time.unscaledTime<nextCheck) return;
            nextCheck=Time.unscaledTime+1;
            bool member=lobby.Value.Members.Any(delegate(Friend friend) { return friend.Id==SteamClient.SteamId; });
            if(member)
            {
                if(!Ready) Log("阵营标点同步已就绪；使用大厅成员数据收发标点。");
                Ready=true; return;
            }
            Ready=false;
            if(!guard.CanBegin(Time.unscaledTime)) return;
            // Steam friend-invite joins in this build connect the game without joining the lobby.
            // Install the callback guard BEFORE requesting membership, so the game's OnLobbyEntered
            // handler cannot restart the already-established game connection.
            if(harmony==null)
            {
                harmony=new Harmony("local.ratgame.mapmarkers.lobby"); hookOwner=this;
                try { harmony.Patch(AccessTools.Method(typeof(SteamNetworkManager),"OnLobbyEntered"),prefix:new HarmonyMethod(typeof(SteamLobbySharing),"BeforeLobbyEntered")); }
                catch { harmony=null; hookOwner=null; throw; }
            }
            guard.Begin(current,NetworkId,Time.unscaledTime);
            try { joining=SteamMatchmaking.JoinLobbyAsync(lobby.Value.Id); Log("正在补全 Steam 好友邀请房间的标点同步连接。"); }
            catch(Exception e) { guard.Finish(Time.unscaledTime); Log("标点同步请求失败："+e.GetBaseException().Message); }
        }
        private static bool BeforeLobbyEntered(Lobby __0)
        {
            SteamLobbySharing owner=hookOwner;
            if(owner==null) return true;
            Lobby? lobby=CurrentLobby;
            return !owner.guard.Suppress((ulong)__0.Id,NetworkId,Connected,lobby.HasValue?(ulong)lobby.Value.Id:0,Time.unscaledTime);
        }
        private void Log(string message) { if(app.Log!=null) app.Log.LogInfo(message); }
        public void Dispose()
        {
            Ready=false;
            // Plugins are removed with the game closed. Keep an in-flight callback guarded until
            // shutdown rather than allowing it to reconnect the game during teardown.
            if(joining!=null&&!joining.IsCompleted) return;
            if(harmony!=null) harmony.UnpatchSelf();
            if(hookOwner==this) hookOwner=null;
        }
    }
}
