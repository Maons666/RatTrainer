using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RatTrainer
{
    [BepInPlugin("local.ratgame.trainer", "Rat Trainer", "1.3.0")]
    [BepInProcess("Rat Game.exe")]
    [DefaultExecutionOrder(20000)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public TrainerOptions Options;
        public PlayerFeatures Players;
        public WorldFeatures World;
        public HostBridge Bridge;
        public AppearanceTools Appearance;
        public TrainerPanel Panel;
        public DesktopLink Desktop;
        public bool MenuOpen;
        public bool HostSandboxMode;
        public string Status = "等待 RatTrainer.exe 连接";
        public string Error = "";
        public int PatchCount;
        public readonly System.Collections.Generic.List<string> PatchErrors = new System.Collections.Generic.List<string>();
        private Harmony harmony;
        private float saveAt = -1f, diagnosticAt, errorAt;
        private CursorLockMode previousLock;
        private bool previousCursor;
        private bool previousRunInBackground;
        public string DataDirectory;
        public string OptionsPath { get { return Path.Combine(DataDirectory, "options.json"); } }
        public static NetworkManager Network { get { return NetworkManager.Singleton; } }
        public static bool Connected { get { return Network != null && Network.IsListening && Network.IsConnectedClient; } }
        public static bool IsHost { get { return Connected && Network.IsServer; } }
        public PlayerController LocalPlayer
        {
            get
            {
                if (!Connected || GameManager.instance == null) return null;
                return GameManager.instance.GetControlledPlayerController();
            }
        }

        private void Awake()
        {
            Instance = this;
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            DataDirectory = Path.Combine(Paths.ConfigPath, "RatTrainer");
            Directory.CreateDirectory(DataDirectory);
            Options = new TrainerOptions();
            if (File.Exists(OptionsPath))
            {
                try { Options = SafeFiles.Parse<TrainerOptions>(File.ReadAllText(OptionsPath)) ?? new TrainerOptions(); }
                catch (Exception e) { Error = "配置读取失败，已使用默认值：" + e.Message; }
            }
            Options.Normalize();
            // Reversible local preferences persist; global world control never activates on launch.
            Options.World = new WorldOptions();
            Options.AllowGuestRequests = false;
            if (!Options.UseInGamePanel) Options.DisableAll();
            Players = new PlayerFeatures(this);
            World = new WorldFeatures(this);
            Bridge = new HostBridge(this);
            Appearance = new AppearanceTools(this);
            Panel = new TrainerPanel(this);
            Desktop = new DesktopLink(this);
            harmony = new Harmony("local.ratgame.trainer");
            Hooks.Install(harmony, this);
            MenuOpen = Options.UseInGamePanel && Options.OpenOnLaunch;
            Logger.LogInfo("Rat Trainer 1.3.0 ready; patches=" + PatchCount + "; failed=" + PatchErrors.Count);
            Save();
        }

        private void Update()
        {
            try
            {
                Keyboard k = Keyboard.current;
                Key menu = Options.MenuKey == "F8" ? Key.F8 : Options.MenuKey == "Insert" ? Key.Insert : Key.F6;
                if (Options.UseInGamePanel && k != null && k[menu].wasPressedThisFrame) SetMenu(!MenuOpen);
                if (k != null && k.f10Key.wasPressedThisFrame) DisableAll();
                if (k != null && k.escapeKey.wasPressedThisFrame && MenuOpen) SetMenu(false);
                if (!HostSandboxMode && !MenuOpen && k != null)
                {
                    if (k.f7Key.wasPressedThisFrame) Players.StorePosition();
                    if (k.f9Key.wasPressedThisFrame) Players.ReturnPosition();
                }
                try { Desktop.Tick(); } catch (Exception e) { ReportError("桌面连接", e); }
                Players.Tick();
                Bridge.Tick();
                World.Tick();
                if (saveAt >= 0f && Time.unscaledTime >= saveAt) Save();
                if (Time.unscaledTime >= diagnosticAt) { diagnosticAt = Time.unscaledTime + 2f; WriteDiagnostics(); }
            }
            catch (Exception e) { ReportError("运行", e); }
        }

        private void LateUpdate()
        {
            if (MenuOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        private void OnGUI() { if (Panel != null && (Options.UseInGamePanel || Options.ShowHud)) Panel.Draw(); }
        private void OnApplicationQuit() { Save(); }
        private void OnDestroy()
        {
            try
            {
                if (Players != null) Players.Restore();
                if (World != null) World.Restore();
                if (Bridge != null) Bridge.Dispose();
                if (harmony != null) harmony.UnpatchSelf();
            }
            catch (Exception e) { Logger.LogError(e); }
            Instance = null;
            Application.runInBackground = previousRunInBackground;
        }

        public bool IsLocal(PlayerController player)
        {
            return player != null && Connected && player.IsSpawned && player.IsOwner && player.ControllingClientIdVar.Value == Network.LocalClientId;
        }
        public bool Active { get { return Options.Enabled; } }
        public void SetMenu(bool open)
        {
            if (MenuOpen == open) return;
            if (open) { previousLock = Cursor.lockState; previousCursor = Cursor.visible; }
            else { Cursor.lockState = previousLock; Cursor.visible = previousCursor; }
            MenuOpen = open;
        }
        public void Changed() { Options.Normalize(); saveAt = Time.unscaledTime + 0.5f; Bridge.MarkDirty(); }
        public void Save()
        {
            try { Options.Normalize(); SafeFiles.WriteAtomic(OptionsPath, SafeFiles.Json(Options)); saveAt = -1f; }
            catch (Exception e) { ReportError("保存配置", e); }
        }
        public void DisableAll()
        {
            Options.DisableAll();
            Options.AllowGuestRequests = false;
            Players.Restore();
            World.Restore();
            Bridge.ResetRequests();
            Save();
            Notify("全部关闭；移动参数已恢复。已生成物品和已保存外观不会撤销。");
        }
        public void Notify(string message) { Status = message; Logger.LogInfo(message); }
        public void ReportError(string action, Exception e)
        {
            Error = action + "：" + e.GetBaseException().Message;
            if (Time.unscaledTime >= errorAt) { errorAt = Time.unscaledTime + 5f; Logger.LogError(action + ": " + e); }
        }
        public void Run(Action action)
        {
            try { action(); } catch (Exception e) { ReportError("操作失败", e); Notify("操作未完成，请查看设置页的诊断信息。"); }
        }
        public void WriteDiagnostics()
        {
            PlayerController player = LocalPlayer;
            RatController rat = player as RatController;
            SafeFiles.WriteAtomic(Path.Combine(DataDirectory, "diagnostics.json"), SafeFiles.Json(new
            {
                version = "1.3.0", utc = DateTime.UtcNow.ToString("o"), connected = Connected,
                host = IsHost, role = player == null ? "none" : player.GetType().Name,
                menu = MenuOpen, enabled = Active, patches = PatchCount, patchErrors = PatchErrors,
                bridge = Bridge.Status, bridgeAvailable = Bridge.Available,
                health = rat == null ? -1f : rat.Health,
                hunger = rat == null ? -1f : rat.HungerAmountVar.Value,
                sprint = rat == null ? -1f : rat.SprintAmountVar.Value,
                speed = rat == null ? -1f : rat.Movement.MaxSpeed,
                jump = rat == null ? -1f : rat.Movement.JumpStrength,
                position = player == null ? "" : player.CharacterController.transform.position.ToString("F3"),
                ratBaseline = Players.BaselineDescription, world = Bridge.EffectiveGlobal,
                status = Status, error = Error
            }));
        }
    }
}
