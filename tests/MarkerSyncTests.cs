using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using RatAssist;

internal static class MarkerSyncTests
{
    private static int passed;
    private static void Check(bool ok,string label) { if(!ok) throw new Exception(label); passed++; Console.WriteLine("PASS "+label); }
    private static void Place(Peer peer)
    { Sim.Use(peer); typeof(TeamPings).GetMethod("Place",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(peer.Pings,new object[] {peer.Player}); }
    private static bool Sees(Peer viewer,Peer sender)
    { Sim.Use(viewer); return viewer.Pings.Visible().Any(x=>x.Client==sender.Player.ControllingClientIdVar.Value); }
    private static void Main()
    {
        Peer host=new Peer(101,0,1001,Side.Rats,11),guest=new Peer(102,7,1002,Side.Rats,22);
        Sim.Players.Add(host.Player); Sim.Players.Add(guest.Player); Sim.Members.Add(host.Steam);
        Sim.Use(guest); guest.LobbyManager.CurrentLobby.Value.SetMemberData(PingProtocol.DataKey,"ignored");
        Check(Sim.Get(guest.Steam,PingProtocol.DataKey)=="","reproduces invisible metadata for invited player who is not a Steam lobby member");
        Sim.Step(host); Place(host); Sim.Step(guest);
        Check(Sim.JoinCalls==1&&!guest.Pings.Sharing.Ready,"friend-invite connection requests missing lobby membership");
        Sim.Step(guest); Check(Sim.JoinCalls==1,"in-flight membership join does not repeat");
        Place(guest); Check(Sees(guest,guest)&&!Sees(host,guest),"own marker is retained while lobby membership is pending");
        Sim.CompleteJoin(guest,true); Check(Sim.ReconnectCalls==0,"membership callback does not restart the game connection");
        Sim.Step(guest); Sim.Step(host); Sim.Step(guest);
        Check(guest.Pings.Sharing.Ready&&Sees(host,guest)&&Sees(guest,host),"host and invited guest receive each other's markers after membership completes");
        Check(host.Player.UserNameVar.Value!=new Steamworks.Friend().Name&&Sees(host,guest),"different display-name representations do not reject live teammate markers");
        Sim.Use(guest); Sim.EnterCallback(900); Check(Sim.ReconnectCalls==0,"late duplicate callback also preserves the game connection");
        Sim.EnterCallback(901); Check(Sim.ReconnectCalls==1,"an unrelated lobby entry remains available to the game");
        int joined=Sim.JoinCalls; Sim.Step(host); Sim.Step(guest); Check(Sim.JoinCalls==joined,"already joined players do not repeatedly join the lobby");
        string good=Sim.Get(guest.Steam,PingProtocol.DataKey);
        Action<Action<PingPacket>,string> reject=delegate(Action<PingPacket> mutate,string message)
        {
            PingPacket packet=JsonConvert.DeserializeObject<PingPacket>(good); mutate(packet);
            Sim.Set(guest.Steam,PingProtocol.DataKey,JsonConvert.SerializeObject(packet)); Sim.Step(host); Check(!Sees(host,guest),message);
        };
        reject(p=>p.Lobby=901,"markers from another lobby are rejected");
        reject(p=>p.Steam=999,"metadata sender must match the Steam member record");
        reject(p=>p.Level=4,"markers from another shared level are rejected");
        reject(p=>p.Actor=999,"markers from a previous character object are rejected");
        reject(p=>p.Client=0,"a remote record cannot claim the viewer's client");
        reject(p=>p.Version=1,"old protocol is rejected with an explicit version boundary");
        reject(p=>p.Points[0].X=Single.NaN,"non-finite coordinates are rejected");
        reject(p=>p.Points[0].CreatedServerTime=Double.NaN,"non-finite creation times are rejected");
        reject(p=>p.Points[0].CreatedServerTime=69,"expired network-time markers are rejected");
        reject(p=>p.Points[0].CreatedServerTime=106,"far-future network-time markers are rejected");
        reject(p=>p.Points[0].Lifetime=121,"out-of-range lifetimes are rejected");
        reject(p=>p.Points.AddRange(Enumerable.Repeat(p.Points[0],5)),"oversized marker lists are rejected");
        Sim.Set(guest.Steam,PingProtocol.DataKey,"{broken"); Sim.Step(host); Check(!Sees(host,guest),"malformed peer JSON does not stop the receiver");
        Sim.Set(guest.Steam,PingProtocol.DataKey,new string('x',4097)); Sim.Step(host); Check(!Sees(host,guest),"oversized peer metadata is ignored");
        Sim.Set(guest.Steam,PingProtocol.DataKey,good); Sim.Step(host); Check(Sees(host,guest),"valid peer data recovers after malformed metadata");
        guest.Player.OwnerClientId=99; Sim.Step(host); Check(Sees(host,guest),"current controlling-player identity remains valid independently of object ownership"); guest.Player.OwnerClientId=7;
        guest.Player.IsSpawned=false; Sim.Step(host); Check(!Sees(host,guest),"despawned characters cannot leave visible markers"); guest.Player.IsSpawned=true;
        guest.Player.Side=Side.Chef; Sim.Step(host); Check(!Sees(host,guest),"rats never accept a chef's remote marker");
        Sim.Use(guest); guest.Pings.Clear(true); Sim.Step(guest); Place(guest);
        Check(Sees(guest,guest)&&!Sees(guest,host)&&Sim.Get(guest.Steam,PingProtocol.DataKey)=="","chef sees own markers and publishes no coordinates");
        guest.Player.Side=Side.Rats; Sim.Use(guest); guest.Pings.Clear(true); Sim.Step(guest);
        Sim.FailNextWrite=true; Place(guest); Sim.Step(guest); Sim.Step(host); Check(!Sees(host,guest),"failed metadata write is not treated as delivered");
        UnityEngine.Time.unscaledTime+=2; Sim.Step(guest); Sim.Step(host); Check(Sees(host,guest),"retry publishes the retained marker after a transient write failure");
        Sim.Use(guest); guest.Pings.Clear(true); Sim.Step(host); Check(!Sees(host,guest)&&Sees(host,host),"clearing own markers preserves the other player's markers");
        Sim.FailMembership=true; UnityEngine.Time.unscaledTime+=2; Sim.Step(guest); Check(!guest.Pings.Sharing.Ready,"Steam membership read failures are contained inside sharing");
        Sim.FailMembership=false; UnityEngine.Time.unscaledTime+=2; Sim.Step(guest); Check(guest.Pings.Sharing.Ready,"sharing recovers after a Steam membership read failure");
        LobbyJoinGuard guard=new LobbyJoinGuard(); guard.Begin(900,22,10);
        Check(guard.Suppress(900,99,false,0,100)&&!guard.Suppress(901,22,true,900,100),"pending joins suppress only their own callback even after disconnect");
        guard.Finish(100); Check(guard.Suppress(900,99,false,0,102)&&!guard.Suppress(900,99,false,0,106),"retired join callbacks have a bounded completion window");
        Check(guard.Suppress(900,22,true,900,1000)&&!guard.Suppress(900,23,true,900,1000),"duplicate callback guard is scoped to the original network session");
        Check(guard.CanBegin(106),"a settled failed join can be retried");
        Console.WriteLine(passed+" marker synchronization checks passed; no game or Steam connection was started.");
    }
}
