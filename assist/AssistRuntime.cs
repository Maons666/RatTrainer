using System;
using System.Reflection;
using System.IO;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RatAssist
{
    [Flags] public enum AssistFeature { None=0, Pings=1, TeamMap=2 }
    [DefaultExecutionOrder(30000)]
    public sealed class AssistRuntime : MonoBehaviour
    {
        public static AssistRuntime Instance;
        public AssistFeature Features;
        public Key PingKey = Key.G;
        public float PingLifetime = 30f;
        public int MaxOwnPings = 3;
        public ManualLogSource Log;
        public TeamPings Pings;
        public MapOverlay Map;
        public WorldOverlay World;
        public Texture2D Avatar;
        public Font UiFont;
        private string context;
        private float errorAt;
        public static AssistRuntime Acquire(AssistFeature feature, ManualLogSource log)
        {
            if (Instance == null)
            {
                GameObject root = new GameObject("RatAssist.Local"); UnityEngine.Object.DontDestroyOnLoad(root);
                Instance = root.AddComponent<AssistRuntime>(); Instance.Log = log;
            }
            Instance.Features |= feature; return Instance;
        }
        public void Release(AssistFeature feature)
        {
            Features &= ~feature;
            if (feature == AssistFeature.Pings && Pings != null) Pings.Clear(true);
            if (Features == AssistFeature.None) UnityEngine.Object.Destroy(gameObject);
        }
        private void Awake()
        {
            UiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            Avatar = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RatAssist.rat.png"))
            using (MemoryStream memory = new MemoryStream()) { stream.CopyTo(memory); ImageConversion.LoadImage(Avatar, memory.ToArray()); }
            Avatar.hideFlags = HideFlags.HideAndDontSave; UiFont.hideFlags = HideFlags.HideAndDontSave;
            Pings = new TeamPings(this); World = new WorldOverlay(this); Map = new MapOverlay(this);
        }
        public static NetworkManager Network { get { return NetworkManager.Singleton; } }
        public static PlayerController Local
        {
            get { return Network != null && Network.IsListening && Network.IsConnectedClient && GameManager.instance != null ? GameManager.instance.GetControlledPlayerController() : null; }
        }
        public static Side SideOf(PlayerController player)
        {
            if (player is RatController || player is GhostController) return Side.Rats;
            return player is ChefController ? Side.Chef : Side.None;
        }
        public static bool SameTeam(PlayerController viewer, PlayerController subject)
        {
            return viewer != null && subject != null && subject.IsSpawned && subject.CharacterController != null &&
                TeamPolicy.CanSee(SideOf(viewer), viewer.ControllingClientIdVar.Value, SideOf(subject), subject.ControllingClientIdVar.Value);
        }
        public static bool IsCurrent(PlayerController actor)
        {
            return actor != null && actor.IsSpawned && GameManager.instance != null && actor.ControllingClientIdVar.Value != 999 && actor.ControllingClientIdVar.Value != 1200 && actor.ControllingClientIdVar.Value != 1337 &&
                GameManager.instance.GetControlledPlayerControllerForClient(actor.ControllingClientIdVar.Value) == actor;
        }
        public static string PlayerName(PlayerController player)
        { string value=player==null?"":player.UserNameVar.Value.ToString(); return String.IsNullOrWhiteSpace(value)?"玩家":value.Replace("\n"," ").Replace("\r"," "); }
        public GameScreen Screen
        { get { return GameCanvas.instance == null ? null : GameCanvas.instance.GetScreen<GameScreen>(); } }
        public bool Playing
        { get { return Local != null && GameManager.instance.CurrentGameState.Value == GameState.InGame; } }
        public bool GameVisible
        { get { return Playing && Screen != null && GameCanvas.instance.CurrentScreen == Screen; } }
        private void LateUpdate()
        {
            try
            {
                PlayerController local = Local;
                string next = !Playing ? "" : Network.GetInstanceID()+":"+UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle+":"+local.NetworkObjectId+":"+SideOf(local);
                if (next != context) { context=next; Pings.Clear(true); Map.Reset(); World.Reset(); }
                if (!Playing) return;
                if ((Features & AssistFeature.Pings) != 0) Pings.Tick();
                World.Tick(GameVisible && (Features & AssistFeature.Pings) != 0);
                Map.Tick(GameVisible);
            }
            catch (Exception e)
            {
                if (Time.unscaledTime >= errorAt) { errorAt=Time.unscaledTime+5f; if(Log!=null) Log.LogError("辅助功能："+e); }
            }
        }
        public void Notify(string message) { World.Notify(message); if(Log!=null) Log.LogInfo(message); }
        private void OnDestroy()
        {
            if(Pings!=null) Pings.Clear(true); if(Map!=null) Map.Reset(); if(World!=null) World.Dispose();
            if(Avatar!=null) UnityEngine.Object.Destroy(Avatar); if(UiFont!=null) UnityEngine.Object.Destroy(UiFont); Instance=null;
        }
    }
}
