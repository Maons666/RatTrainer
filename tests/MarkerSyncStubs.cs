// Offline substitutes for the narrow Steam/Unity surface used by the production marker code.
// No game process, Steam SDK, network connection, or installation is opened by these tests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RatAssist;

internal sealed class Peer
{
    public ulong Steam;
    public PlayerController Player;
    public Unity.Netcode.NetworkManager Network;
    public GameManager Game;
    public SteamNetworkManager LobbyManager=new SteamNetworkManager();
    public AssistRuntime App=new AssistRuntime();
    public TeamPings Pings;
    public Peer(ulong steam,ulong client,ulong actor,Side side,int network)
    {
        Steam=steam; Player=new PlayerController { ControllingClientIdVar=new NetValue<ulong>(client),OwnerClientId=client,NetworkObjectId=actor,Side=side };
        Network=new Unity.Netcode.NetworkManager { InstanceId=network,LocalClientId=client,IsServer=client==0 };
        Game=new GameManager(); LobbyManager.CurrentLobby=new Steamworks.Data.Lobby(900);
        Pings=new TeamPings(App);
        Sim.Peers[client]=this;
    }
}
internal static class Sim
{
    public static Peer Active;
    public static readonly HashSet<ulong> Members=new HashSet<ulong>();
    public static readonly Dictionary<ulong,Dictionary<string,string>> Data=new Dictionary<ulong,Dictionary<string,string>>();
    public static readonly List<PlayerController> Players=new List<PlayerController>();
    public static readonly Dictionary<ulong,Peer> Peers=new Dictionary<ulong,Peer>();
    public static bool EnableNetwork,BlockMetadata,DropReceipts;
    public sealed class Wire { public ulong From,To; public string Name,Data; }
    public static readonly Queue<Wire> Wires=new Queue<Wire>();
    public static int FragmentedSends;
    public static TaskCompletionSource<Steamworks.Data.Lobby?> Join;
    public static MethodInfo Prefix;
    public static int JoinCalls,ReconnectCalls,Leaves;
    public static bool FailNextWrite,FailMembership;
    public static void Use(Peer peer) { Active=peer; GameManager.instance=peer.Game; SteamNetworkManager.instance=peer.LobbyManager; }
    public static void Step(Peer peer)
    { Use(peer); UnityEngine.Time.unscaledTime+=.6f; if(EnableNetwork) peer.Pings.Network.Tick(); peer.Pings.Sharing.Tick(); peer.Pings.Tick(); }
    public static void Pump()
    {
        Peer previous=Active; int budget=1000;
        while(Wires.Count>0)
        {
            if(--budget==0) throw new Exception("Unexpected network message loop");
            Wire wire=Wires.Dequeue(); Peer peer;
            if(!Peers.TryGetValue(wire.To,out peer)||!peer.Network.IsListening||DropReceipts&&wire.Data.Contains("\"receipt\"")) continue;
            Action<ulong,Unity.Netcode.FastBufferReader> callback;
            if(!peer.Network.CustomMessagingManager.Handlers.TryGetValue(wire.Name,out callback)) continue;
            Use(peer); callback(wire.From,new Unity.Netcode.FastBufferReader { Data=wire.Data });
        }
        Use(previous);
    }
    public static void Reset()
    {
        Peers.Clear(); Players.Clear(); Members.Clear(); Data.Clear(); Wires.Clear();
        EnableNetwork=true; BlockMetadata=true; DropReceipts=false; FailMembership=false; FailNextWrite=false;
        UnityEngine.Time.unscaledTime=1; Steamworks.SteamClient.IsValid=true; Prefix=null;
    }
    public static void EnterCallback(ulong id)
    {
        object[] args={new Steamworks.Data.Lobby(id)};
        if(Prefix==null||(bool)Prefix.Invoke(null,args)) ReconnectCalls++;
    }
    public static void CompleteJoin(Peer peer,bool callbackFirst)
    {
        Use(peer); Members.Add(peer.Steam);
        if(callbackFirst) EnterCallback(900);
        Join.SetResult(new Steamworks.Data.Lobby(900));
    }
    public static void Set(ulong member,string key,string value)
    { if(!Data.ContainsKey(member)) Data[member]=new Dictionary<string,string>(); Data[member][key]=value; }
    public static string Get(ulong member,string key)
    { return Data.ContainsKey(member)&&Data[member].ContainsKey(key)?Data[member][key]:""; }
}
public sealed class NetValue<T> { public T Value; public NetValue(T value) { Value=value; } }
public sealed class PlayerController
{
    public NetValue<ulong> ControllingClientIdVar;
    public NetValue<string> UserNameVar=new NetValue<string>("In-game nickname");
    public ulong NetworkObjectId,OwnerClientId;
    public bool IsSpawned=true;
    public Side Side;
    public object CharacterController=new object();
}
public sealed class GameManager
{
    public static GameManager instance;
    public NetValue<int> CurrentLevelIndexVar=new NetValue<int>(3);
    public PlayerController GetControlledPlayerControllerForClient(ulong id) { return Sim.Players.FirstOrDefault(x=>x.ControllingClientIdVar.Value==id); }
}
public sealed class SteamNetworkManager
{
    public static SteamNetworkManager instance;
    public Steamworks.Data.Lobby? CurrentLobby;
    private void OnLobbyEntered(Steamworks.Data.Lobby lobby) { Sim.ReconnectCalls++; }
}
public sealed class GameCamera
{
    public static GameCamera instance=new GameCamera();
    public object Cam=new object();
    public UnityEngine.Ray GetRay() { return new UnityEngine.Ray(); }
}
namespace RatAssist
{
    public sealed class FakeLog
    {
        public readonly List<string> Lines=new List<string>();
        public void LogInfo(string value) { Lines.Add(value); }
        public void LogWarning(string value) { Lines.Add(value); }
    }
    public sealed class FakeScreen { public FakeMenu EmoteMenu; }
    public sealed class FakeMenu { public bool IsActive; }
    public sealed class AssistRuntime
    {
        public static Unity.Netcode.NetworkManager Network { get { return Sim.Active.Network; } }
        public static PlayerController Local { get { return Sim.Active.Player; } }
        public bool Playing=true,GameVisible;
        public float PingLifetime=30;
        public int MaxOwnPings=3;
        public UnityEngine.InputSystem.Key PingKey;
        public FakeScreen Screen=new FakeScreen();
        public FakeLog Log=new FakeLog();
        public string Message;
        public static Side SideOf(PlayerController actor) { return actor==null?Side.None:actor.Side; }
        public static bool IsCurrent(PlayerController actor)
        { return actor!=null&&actor.IsSpawned&&GameManager.instance.GetControlledPlayerControllerForClient(actor.ControllingClientIdVar.Value)==actor; }
        public static bool SameTeam(PlayerController viewer,PlayerController owner)
        { return viewer!=null&&owner!=null&&owner.IsSpawned&&TeamPolicy.CanSee(viewer.Side,viewer.ControllingClientIdVar.Value,owner.Side,owner.ControllingClientIdVar.Value); }
        public void Notify(string text) { Message=text; }
    }
}
namespace UnityEngine
{
    public static class Time { public static float unscaledTime=1; }
    public static class Application { public static bool isFocused=true; }
    public static class Mathf { public static float Clamp(float x,float min,float max) { return Math.Min(max,Math.Max(min,x)); } }
    public struct Ray { }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 operator+(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator*(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
    }
    public sealed class Transform
    {
        public Vector3 Position;
        public Vector3 TransformPoint(Vector3 point) { return point+Position; }
        public Vector3 InverseTransformPoint(Vector3 point) { return point+Position*-1; }
    }
    public sealed class Collider { public T GetComponentInParent<T>() where T:class { return null; } }
    public struct RaycastHit { public Collider collider; public Vector3 point,normal; public float distance; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public const int DefaultRaycastLayers=0;
        public static RaycastHit[] RaycastAll(Ray ray,float distance,int layers,QueryTriggerInteraction trigger)
        { return new[] {new RaycastHit { collider=new Collider(),point=new Vector3(10,2,20),normal=new Vector3(0,1,0),distance=3 }}; }
    }
}
namespace UnityEngine.InputSystem
{
    public enum Key { G }
    public sealed class KeyControl { public bool wasPressedThisFrame,isPressed; }
    public sealed class Keyboard
    {
        public static Keyboard current;
        public KeyControl leftShiftKey=new KeyControl(),rightShiftKey=new KeyControl(),leftCtrlKey=new KeyControl(),rightCtrlKey=new KeyControl();
        public KeyControl this[Key key] { get { return new KeyControl(); } }
    }
}
namespace Unity.Netcode
{
    public sealed class NetworkTime { public double Time=100; }
    public sealed class NetworkManager
    {
        public const ulong ServerClientId=0;
        public ulong LocalClientId;
        public bool IsServer;
        public IEnumerable<ulong> ConnectedClientsIds { get { return Sim.Peers.Where(x=>x.Value.Network.IsListening).Select(x=>x.Key).ToArray(); } }
        public CustomMessagingManager CustomMessagingManager=new CustomMessagingManager();
        public bool IsListening=true,IsConnectedClient=true;
        public int InstanceId;
        public NetworkTime ServerTime=new NetworkTime();
        public SpawnManager SpawnManager=new SpawnManager();
        public int GetInstanceID() { return InstanceId; }
    }
    public sealed class SpawnManager { public Dictionary<ulong,NetworkObject> SpawnedObjects=new Dictionary<ulong,NetworkObject>(); }
    public sealed class NetworkObject { public bool IsSpawned; public ulong NetworkObjectId; public UnityEngine.Transform transform=new UnityEngine.Transform(); }
    public enum NetworkDelivery { ReliableSequenced,ReliableFragmentedSequenced }
    public sealed class CustomMessagingManager
    {
        public readonly Dictionary<string,Action<ulong,FastBufferReader>> Handlers=new Dictionary<string,Action<ulong,FastBufferReader>>();
        public void RegisterNamedMessageHandler(string name,Action<ulong,FastBufferReader> handler) { Handlers.Add(name,handler); }
        public void UnregisterNamedMessageHandler(string name) { Handlers.Remove(name); }
        public void SendNamedMessage(string name,ulong target,FastBufferWriter writer,NetworkDelivery delivery)
        {
            if(writer.Data.Length*2>1200&&delivery!=NetworkDelivery.ReliableFragmentedSequenced) throw new InvalidOperationException("Message exceeds simulated unfragmented MTU");
            if(delivery==NetworkDelivery.ReliableFragmentedSequenced) Sim.FragmentedSends++;
            Sim.Wires.Enqueue(new Sim.Wire { From=Sim.Active.Network.LocalClientId,To=target,Name=name,Data=writer.Data });
        }
    }
    public struct FastBufferWriter : IDisposable
    {
        public string Data;
        public FastBufferWriter(int size,Unity.Collections.Allocator allocator) { Data=null; }
        public void WriteValueSafe(string data) { Data=data; }
        public void Dispose() { }
    }
    public struct FastBufferReader
    {
        public string Data;
        public int Length { get { return Data.Length*2+4; } }
        public void ReadValueSafe(out string data,bool ascii) { data=Data; }
    }
}
namespace Unity.Collections { public enum Allocator { Temp } }
namespace Steamworks
{
    public struct SteamId
    {
        public ulong Value;
        public static implicit operator ulong(SteamId value) { return value.Value; }
        public static implicit operator SteamId(ulong value) { return new SteamId { Value=value }; }
    }
    public struct Friend { public SteamId Id; public string Name { get { return "Different Steam display name"; } } }
    public static class SteamClient
    {
        public static bool IsValid=true;
        public static SteamId SteamId { get { return Sim.Active.Steam; } }
    }
    public static class SteamMatchmaking
    {
        public static Task<Data.Lobby?> JoinLobbyAsync(SteamId id)
        { Sim.JoinCalls++; Sim.Join=new TaskCompletionSource<Data.Lobby?>(); return Sim.Join.Task; }
    }
}
namespace Steamworks.Data
{
    public struct Lobby
    {
        public Steamworks.SteamId Id;
        public Lobby(ulong id) { Id=id; }
        public IEnumerable<Steamworks.Friend> Members
        {
            get
            {
                if(Sim.FailMembership) throw new InvalidOperationException("Simulated Steam read failure");
                if(!Sim.Members.Contains(Sim.Active.Steam)) return new Steamworks.Friend[0];
                return Sim.Members.Select(x=>new Steamworks.Friend { Id=x }).ToArray();
            }
        }
        public void SetMemberData(string key,string value)
        {
            if(Sim.FailNextWrite) { Sim.FailNextWrite=false; throw new InvalidOperationException("Simulated retryable write failure"); }
            if(Sim.Members.Contains(Sim.Active.Steam)) Sim.Set(Sim.Active.Steam,key,value);
        }
        public string GetMemberData(Steamworks.Friend member,string key)
        { return !Sim.BlockMetadata&&Sim.Members.Contains(Sim.Active.Steam)?Sim.Get(member.Id,key):""; }
        public void Leave() { Sim.Leaves++; Sim.Members.Remove(Sim.Active.Steam); }
    }
}
namespace HarmonyLib
{
    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void Patch(MethodInfo method,HarmonyMethod prefix)
        { if(method==null) throw new MissingMethodException(); Sim.Prefix=prefix.Method; }
        public void UnpatchSelf() { Sim.Prefix=null; }
    }
    public sealed class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(Type type,string name) { Method=AccessTools.Method(type,name); }
    }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type,string name)
        { return type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance); }
    }
}
