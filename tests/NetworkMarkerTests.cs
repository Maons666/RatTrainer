using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using RatAssist;

internal static class NetworkMarkerTests
{
    private static int passed;
    private static void Check(bool ok,string label) { if(!ok) throw new Exception(label); passed++; Console.WriteLine("PASS "+label); }
    private static Peer Add(ulong client,Side side)
    { Peer peer=new Peer(101+client,client,1001+client,side,(int)client+11); Sim.Players.Add(peer.Player); Sim.Members.Add(peer.Steam); return peer; }
    private static void Place(Peer peer)
    { Sim.Use(peer); typeof(TeamPings).GetMethod("Place",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(peer.Pings,new object[] {peer.Player}); }
    private static int Count(Peer viewer,Peer sender)
    { Sim.Use(viewer); return viewer.Pings.Visible().Count(x=>x.Client==sender.Network.LocalClientId); }
    private static void Steps(params Peer[] peers) { foreach(Peer peer in peers) Sim.Step(peer); Sim.Pump(); }
    private static void Inject(ulong from,ulong to,string name,MarkerMessage message)
    { Sim.Wires.Enqueue(new Sim.Wire { From=from,To=to,Name=name,Data=JsonConvert.SerializeObject(message) }); Sim.Pump(); }
    private static PingPacket Packet(Peer peer)
    {
        return new PingPacket { Client=peer.Network.LocalClientId,Actor=peer.Player.NetworkObjectId,Level=3,
            Points={new PingPoint { Id=Guid.NewGuid().ToString("N"),X=1,Y=2,Z=3,CreatedServerTime=100,Lifetime=30 }} };
    }
    private static void Main()
    {
        Sim.Reset(); Peer host=Add(0,Side.Rats),guest=Add(7,Side.Rats);
        Steps(host,guest); Check(guest.Pings.Network.Ready,"host capability handshake uses the existing game connection");
        Place(guest); Check(guest.Pings.Network.Confirmations==0,"sending alone never reports teammate confirmation");
        Steps(guest,host); Check(Count(host,guest)==1&&guest.Pings.Network.Confirmations==1,"host receives and acknowledges guest marker when Steam metadata is blocked");
        Check(guest.App.Message.Contains("队友已收到"),"receipt changes the sender's user-facing status");
        Place(host); Steps(host,guest); Check(Count(guest,host)==1&&host.Pings.Network.Confirmations==1,"guest receives and acknowledges host marker without Steam metadata");
        Sim.BlockMetadata=false; Steps(host,guest); Check(Count(host,guest)==1&&Count(guest,host)==1,"the two transports do not draw duplicate markers");
        Sim.Use(guest); guest.Pings.Clear(true); Steps(host,guest); Check(Count(host,guest)==0&&Count(host,host)==1,"clear travels over the game connection and preserves other players' markers");
        Sim.Reset(); host=Add(0,Side.Chef); Peer a=Add(7,Side.Rats),b=Add(8,Side.Rats),chef=Add(9,Side.Chef);
        Steps(host,a,b,chef); Steps(a,b);
        Place(a); Steps(a,host,b); Check(Count(b,a)==1&&a.Pings.Network.Confirmations==1,"a chef host relays markers between two guest rats");
        Check(Count(host,a)==0&&Count(chef,a)==0,"chef host and chef peer cannot see rat markers");
        Place(host); Steps(host,a,b); Check(Count(host,host)==1&&Count(a,host)==0,"chef's own marker stays local");
        Place(b); Steps(b,host,a); Check(Count(a,b)==1,"guest-to-guest relay works in the reverse direction");
        Sim.Use(a); a.Pings.Clear(true); Steps(a,host,b); Check(Count(b,a)==0&&Count(a,b)==1,"guest clearing is relayed without clearing teammate markers");
        a.App.MaxOwnPings=5;
        for(int i=0;i<5;i++) { Place(a); Steps(a,host,b); }
        Check(Count(b,a)==5&&Sim.FragmentedSends>0,"five markers use the reliable fragmented message mode");
        Sim.Use(a); a.Pings.Clear(true); Steps(a,host,b);
        Sim.DropReceipts=true; Place(a); Steps(a,host,b); Check(Count(b,a)==1&&a.Pings.Network.Confirmations==0,"lost receipts do not fabricate success even when the marker arrived");
        Sim.DropReceipts=false; UnityEngine.Time.unscaledTime+=3; Steps(a,host,b); Check(a.Pings.Network.Confirmations==1,"periodic state refresh recovers a missed receipt");
        Sim.Use(a); a.Pings.Clear(true); Steps(a,host,b);
        PingPacket spoof=Packet(b); int beforeSpoof=Count(a,b),beforeSpoofSends=host.Pings.Network.Sent;
        UnityEngine.Time.unscaledTime+=1;
        Inject(a.Network.LocalClientId,0,NetworkMarkers.RequestName,new MarkerMessage { Kind="state",Id=Guid.NewGuid().ToString("N"),Packet=spoof });
        Check(Count(a,b)==beforeSpoof&&Count(b,b)==1&&host.Pings.Network.Sent==beforeSpoofSends,"a guest cannot publish a state claiming another client's identity");
        PingPacket chefPacket=Packet(host); UnityEngine.Time.unscaledTime+=1;
        Inject(0,0,NetworkMarkers.RequestName,new MarkerMessage { Kind="state",Id=Guid.NewGuid().ToString("N"),Packet=chefPacket });
        Check(Count(a,host)==0,"host rejects a chef's network marker state");
        Sim.Wires.Enqueue(new Sim.Wire { From=7,To=0,Name=NetworkMarkers.RequestName,Data="{broken" }); Sim.Pump();
        Sim.Wires.Enqueue(new Sim.Wire { From=7,To=0,Name=NetworkMarkers.RequestName,Data=new string('x',5000) }); Sim.Pump();
        UnityEngine.Time.unscaledTime+=1; Place(a); Steps(a,host,b); Check(Count(b,a)==1,"malformed and oversized network messages do not stop later valid markers");
        int before=Count(b,a); PingPacket forged=Packet(a);
        Inject(a.Network.LocalClientId,b.Network.LocalClientId,NetworkMarkers.ReplyName,new MarkerMessage { Kind="state",Origin=7,Id=Guid.NewGuid().ToString("N"),Packet=forged });
        Check(Count(b,a)==before,"clients accept forwarded state only from the host");
        b.Player.Side=Side.Chef; Check(Count(b,a)==0,"changing the viewer to chef immediately hides received rat markers"); b.Player.Side=Side.Rats;
        a.Player.IsSpawned=false; Check(Count(b,a)==0,"despawned character markers disappear immediately"); a.Player.IsSpawned=true;
        Peer late=Add(10,Side.Rats); Steps(late); UnityEngine.Time.unscaledTime+=3; Steps(host,a,b,late);
        Check(Count(late,a)==1,"a newly registered teammate receives existing fresh markers");
        Steamworks.SteamClient.IsValid=false; Sim.Use(a); a.Pings.Clear(true); Steps(host,a,b,late);
        Place(a); Steps(host,a,b,late); Check(Count(b,a)==1&&Count(late,a)==1,"game transport works with no Steam lobby or metadata service");
        Sim.Use(b); b.Pings.Network.Dispose(); Check(b.Network.CustomMessagingManager.Handlers.Count==0,"transport unregisters message handlers on disposal");
        Sim.Reset(); host=Add(0,Side.Rats); guest=Add(7,Side.Rats);
        Sim.Step(guest); Sim.Pump(); Place(guest); Check(!guest.Pings.Network.Ready&&Count(guest,guest)==1,"a local marker is retained while waiting for host support");
        Steps(host); UnityEngine.Time.unscaledTime+=3; Steps(guest,host); Steps(guest,host);
        Check(Count(host,guest)==1,"pending local marker is published after the host handshake becomes available");
        Sim.Reset(); host=Add(0,Side.Rats); guest=Add(7,Side.Rats); chef=Add(9,Side.Chef);
        Steps(host,guest,chef); UnityEngine.Time.unscaledTime+=1; Sim.DropReceipts=true; Place(host);
        MarkerMessage outgoing=Sim.Wires.Select(x=>JsonConvert.DeserializeObject<MarkerMessage>(x.Data)).First(x=>x.Kind=="state");
        Sim.Pump(); Sim.DropReceipts=false;
        Inject(9,0,NetworkMarkers.RequestName,new MarkerMessage { Kind="receipt",Origin=0,Id=outgoing.Id,Count=1 });
        Check(host.Pings.Network.Confirmations==0,"a chef who was not sent rat markers cannot confirm them");
        Inject(7,0,NetworkMarkers.RequestName,new MarkerMessage { Kind="receipt",Origin=0,Id=outgoing.Id,Count=99 });
        Check(host.Pings.Network.Confirmations==0,"a receipt cannot claim more points than the sent state");
        Inject(7,0,NetworkMarkers.RequestName,new MarkerMessage { Kind="receipt",Origin=0,Id=Guid.NewGuid().ToString("N"),Count=1 });
        Check(host.Pings.Network.Confirmations==0,"a receipt from another state cannot confirm the current marker");
        Inject(7,0,NetworkMarkers.RequestName,new MarkerMessage { Kind="receipt",Origin=0,Id=outgoing.Id,Count=1 });
        Check(host.Pings.Network.Confirmations==1,"the intended rat receiver can confirm the matching state");
        Sim.Reset(); Sim.EnableNetwork=false; Sim.BlockMetadata=false; host=Add(0,Side.Rats); guest=Add(7,Side.Rats);
        Steps(host,guest); Place(guest); Steps(guest,host);
        Check(Count(host,guest)==1&&!guest.Pings.Network.Ready,"legacy Steam transport remains available without a host game-channel plugin");
        Check(guest.Pings.Network.Confirmations==0,"legacy Steam delivery is not described as a confirmed network receipt");
        Console.WriteLine(passed+" network marker checks passed; transport is simulated, no live multiplayer session was started.");
    }
}
