using System;
using System.Collections.Generic;

namespace RatAssist
{
    public sealed class PingPoint
    {
        public string Id;
        public float X,Y,Z;
        public ulong Attachment=UInt64.MaxValue;
        public float LocalX,LocalY,LocalZ;
        public double CreatedServerTime;
        public float Lifetime;
    }
    public sealed class PingPacket
    {
        public int Version=2;
        public ulong Lobby,Steam,Client,Actor;
        public int Level;
        public List<PingPoint> Points=new List<PingPoint>();
    }
    public static class PingProtocol
    {
        public const string DataKey="ratassist.markers.v2";
        public static bool Accept(PingPacket packet,ulong lobby,ulong sender,int level,ulong viewerClient,ulong ownerClient,ulong ownerActor,Side ownerSide,bool currentOwner)
        {
            return packet!=null && packet.Version==2 && packet.Lobby==lobby && packet.Steam==sender && packet.Level==level &&
                packet.Points!=null && packet.Points.Count<=5 && packet.Client!=viewerClient && currentOwner && ownerSide==Side.Rats &&
                packet.Client==ownerClient && packet.Actor==ownerActor;
        }
        public static bool Fresh(PingPoint point,double serverTime)
        {
            Guid id;
            if(point==null||!Guid.TryParseExact(point.Id,"N",out id)||!TeamPolicy.Finite(point.X)||!TeamPolicy.Finite(point.Y)||!TeamPolicy.Finite(point.Z)||
                !TeamPolicy.Finite(point.LocalX)||!TeamPolicy.Finite(point.LocalY)||!TeamPolicy.Finite(point.LocalZ)||!TeamPolicy.Finite(point.Lifetime)||point.Lifetime<5||point.Lifetime>120||
                Double.IsNaN(point.CreatedServerTime)||Double.IsInfinity(point.CreatedServerTime)||Double.IsNaN(serverTime)||Double.IsInfinity(serverTime)) return false;
            double age=serverTime-point.CreatedServerTime;
            return age>=-5 && age<=point.Lifetime;
        }
    }
    // A marker-only lobby join must never trigger a second game connection.
    public sealed class LobbyJoinGuard
    {
        public ulong Lobby;
        public int Network;
        public bool Pending;
        public double RetryAt;
        private double callbackUntil;
        public bool CanBegin(double now) { return !Pending && now>=RetryAt; }
        public void Begin(ulong lobby,int network,double now)
        { Lobby=lobby; Network=network; Pending=true; RetryAt=now+5; }
        public void Finish(double now) { Pending=false; callbackUntil=now+5; }
        public bool Suppress(ulong entering,int network,bool connected,ulong currentLobby,double now)
        { return Lobby!=0 && entering==Lobby && (Pending||now<callbackUntil||(connected&&Network==network&&currentLobby==Lobby)); }
    }
}
