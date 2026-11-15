// Test-only integration driver. Never included in the distributable plugin.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using RatTrainer;
using Unity.Netcode;
using UnityEngine;

[BepInPlugin("local.ratgame.trainer.qa", "Rat Trainer Integration QA", "1.0.0")]
[BepInDependency("local.ratgame.trainer")]
public sealed class QaPlugin : BaseUnityPlugin
{
    private static string root;
    private bool host;
    private IEnumerator<float> routine;
    private float next;
    private readonly List<string> passed = new List<string>();
    private Plugin P { get { return Plugin.Instance; } }
    private GameManager G { get { return GameManager.instance; } }
    private void Awake()
    {
        root=Paths.GameRootPath;
        host=Environment.GetCommandLineArgs().Contains("--rat-qa-host");
        if (!host && !Environment.GetCommandLineArgs().Contains("--rat-qa-client")) { enabled=false; return; }
        if (!Path.GetFileName(root).StartsWith("qa-",StringComparison.Ordinal)) throw new InvalidOperationException("QA must run in an isolated qa-* directory");
        Harmony h=new Harmony("local.ratgame.trainer.qa");
        h.Patch(AccessTools.PropertyGetter(typeof(GameManager),"UseSteamNetwork"),prefix:new HarmonyMethod(typeof(QaPlugin),"NoSteam"));
        h.Patch(AccessTools.PropertyGetter(typeof(SettingsScreen),"settingsPath"),prefix:new HarmonyMethod(typeof(QaPlugin),"SettingsPath"));
        h.Patch(AccessTools.PropertyGetter(typeof(SettingsScreen),"sharedSettingsPath"),prefix:new HarmonyMethod(typeof(QaPlugin),"SavePath"));
        Application.runInBackground=true; Application.targetFrameRate=30;
        P.Options=new TrainerOptions(); P.MenuOpen=false;
        routine=Run();
    }
    public static bool NoSteam(ref bool __result) { __result=false; return false; }
    public static bool SettingsPath(ref string __result) { __result=Path.Combine(root,"qa-settings.json"); return false; }
    public static bool SavePath(ref string __result) { __result=Path.Combine(root,"qa-main.sav"); return false; }
    private void Update()
    {
        if(routine==null || Time.unscaledTime<next) return;
        try
        {
            if(Time.unscaledTime>220f) throw new TimeoutException("Integration run exceeded 220 seconds");
            if(routine.MoveNext()) next=Time.unscaledTime+routine.Current;
            else Finish(null);
        }
        catch(Exception e) { Finish(e.ToString()); }
    }
    private void Check(bool condition,string name)
    {
        if(!condition) throw new Exception("FAIL "+name);
        passed.Add(name); Logger.LogInfo("PASS "+name);
    }
    private void Finish(string error)
    {
        routine=null;
        SafeFiles.WriteAtomic(Path.Combine(root,"qa-result.json"),SafeFiles.Json(new { role=host?"host":"client", passed=passed, error=error }));
        if(error!=null) Logger.LogError(error);
        P.DisableAll(); Application.Quit();
    }
    private IEnumerator<float> Run()
    {
        while(G==null || SettingsScreen.instance==null || GameCanvas.instance==null) yield return 0.5f;
        yield return 2f;
        GameSettings.Current.SkipIntro=true;
        G.RegularTransport.SetConnectionData("127.0.0.1",17890,"127.0.0.1");
        if(host) G.StartAsHost(); else G.StartAsClient();
        while(!Plugin.Connected || G.CurrentGameState.Value!=GameState.InLobby) yield return 0.5f;
        Check(Plugin.IsHost==host,"correct network role");
        if(host)
        {
            while(Plugin.Network.ConnectedClientsIds.Count<2) yield return 0.5f;
            yield return 3f;
            Check(!P.Options.AllowGuestRequests,"guest cooperation defaults off");
            G.ChefClientIdVar.Value=1337; G.GameModeVar.Value=GameMode.Coop;
            G.StartGameServerRpc();
            while(G.CurrentGameState.Value!=GameState.InGame || !(P.LocalPlayer is RatController)) yield return 0.5f;
            yield return 3f;
            P.Options.AllowGuestRequests=true; P.Changed();
            ChefBot bot=UnityEngine.Object.FindFirstObjectByType<ChefBot>();
            Check(bot!=null,"co-op AI spawned");
            float originalSpeed=bot.NormalSpeed;
            while(!P.Bridge.EffectiveGlobal.FreezeAi) yield return 0.5f;
            yield return 1f;
            Check(Mathf.Abs(bot.NormalSpeed-originalSpeed*0.5f)<0.01f,"guest AI speed applied on actual host");
            Check(bot.NavAgent.isStopped,"guest AI freeze applied on actual host");
            Check(P.Bridge.EffectiveGlobal.TaskTuning,"guest task settings applied on host");
            while(P.Bridge.EffectiveGlobal.FreezeAi) yield return 0.5f;
            yield return 2f;
            Check(Mathf.Abs(bot.NormalSpeed-originalSpeed)<0.01f && !bot.NavAgent.isStopped,"AI restored after guest emergency stop");
            // Give the host the existing server-owned chef for role-specific integration tests.
            RatController oldRat=(RatController)P.LocalPlayer;
            oldRat.ControllingClientIdVar.Value=1200;
            ChefController chef=bot.Controller;
            chef.IsBotVar.Value=false; chef.ControllingClientIdVar.Value=Plugin.Network.LocalClientId;
            yield return 1f;
            Check(P.LocalPlayer==chef,"chef role selection");
            float acceleration=chef.Acceleration,grab=chef.MaxGrabDistance;
            P.Options.ChefSpeedEnabled=true; P.Options.ChefSpeed=2f; P.Options.GrabEnabled=true; P.Options.Grab=2f;
            yield return 1f;
            Check(Mathf.Abs(chef.Acceleration-acceleration*2f)<0.01f && Mathf.Abs(chef.MaxGrabDistance-grab*2f)<0.01f,"chef speed and grab multipliers");
            GunInteractable gun=UnityEngine.Object.FindFirstObjectByType<GunInteractable>();
            Check(gun!=null,"gun exists for ammo test");
            gun.GetComponent<PickUpInteractable>().PickupServerRpc(new NetworkBehaviourReference(chef),Plugin.Network.LocalClientId);
            int bullets=gun.BulletCountVar.Value;
            P.Options.World.InfiniteAmmo=true;
            yield return 1f;
            Check(gun.BulletCountVar.Value==99,"real network ammunition refilled");
            P.Options.World.InfiniteAmmo=false; P.Options.ChefSpeedEnabled=false; P.Options.GrabEnabled=false;
            yield return 1f;
            Check(gun.BulletCountVar.Value==bullets,"original ammunition restored");
            Check(Mathf.Abs(chef.Acceleration-acceleration)<0.01f && Mathf.Abs(chef.MaxGrabDistance-grab)<0.01f,"chef parameters restored");
            SafeFiles.WriteAtomic(Path.Combine(root,"host-done.txt"),"done");
            yield return 8f;
        }
        else
        {
            yield return 3f;
            Check(!P.Bridge.Available,"client does not falsely report unsupported host features");
            while(G.CurrentGameState.Value!=GameState.InGame || !(P.LocalPlayer is RatController)) yield return 0.5f;
            RatController rat=(RatController)P.LocalPlayer;
            yield return 2f;
            float speed=rat.Movement.MaxSpeed,jump=rat.Movement.JumpStrength,reach=rat.MaxInteractDistance;
            TrainerOptions o=P.Options;
            o.InfiniteSprint=o.NoHunger=o.Invincible=o.NoPoison=o.NoStun=o.NoFallDamage=true;
            o.RatSpeedEnabled=o.JumpEnabled=o.ReachEnabled=true; o.RatSpeed=2f; o.Jump=1.7f; o.Reach=2f;
            rat.Health=0.2f; rat.IsPoisoned=true; rat.PoisonTimer=0.8f; rat.StunTimer=9f; rat.RatStateVar.Value=RatController.RatState.Stunned;
            rat.SprintAmountVar.Value=0.05f; rat.ChangeHungerAmountServerRpc(-0.75f);
            yield return 3f;
            Check(rat.Health>=0.99f && rat.SprintAmountVar.Value>=0.99f && rat.HungerAmountVar.Value>0.85f,"non-host health sprint and server-synced hunger");
            Check(!rat.IsPoisoned && rat.RatStateVar.Value==RatController.RatState.Normal,"non-host poison and stun removal");
            Check(Mathf.Abs(rat.Movement.MaxSpeed-speed*2f)<0.01f && Mathf.Abs(rat.Movement.JumpStrength-jump*1.7f)<0.01f && Mathf.Abs(rat.MaxInteractDistance-reach*2f)<0.01f,"non-host movement jump and reach");
            rat.StunServerRpc(5f); rat.PoisonMeServerRpc(); rat.KillMeServerRpc();
            yield return 1f;
            Check(rat.RatStateVar.Value==RatController.RatState.Normal && !rat.IsPoisoned,"protection against host RPC effects");
            o.RatSpeedEnabled=o.JumpEnabled=o.ReachEnabled=false;
            yield return 1f;
            Check(Mathf.Abs(rat.Movement.MaxSpeed-speed)<0.01f && Mathf.Abs(rat.Movement.JumpStrength-jump)<0.01f && Mathf.Abs(rat.MaxInteractDistance-reach)<0.01f,"non-host parameters restore when toggled off");
            P.Players.StorePosition(); Vector3 at=rat.CharacterController.transform.position;
            rat.TeleportTo(at+Vector3.up*0.3f,Quaternion.identity); P.Players.ReturnPosition();
            Check(Vector3.Distance(rat.CharacterController.transform.position,at)<0.01f,"non-host position bookmark teleport");
            while(!P.Bridge.Available) yield return 0.5f;
            o.World.FastInteraction=true; o.World.InteractionSpeed=5f; o.World.TaskTuning=true; o.World.TaskScale=0.5f;
            o.World.AiTuning=true; o.World.AiSpeed=0.5f; o.World.AiHearing=0f; o.World.FreezeAi=true; P.Changed();
            yield return 3f;
            Check(P.Bridge.Available && P.Bridge.GlobalAccepted && P.Bridge.EffectiveGlobal.FreezeAi,"guest settings accepted and acknowledged");
            int before=UnityEngine.Object.FindObjectsByType<Spawnable>(FindObjectsSortMode.None).Length;
            P.Bridge.Spawn("food",2);
            yield return 3f;
            Check(UnityEngine.Object.FindObjectsByType<Spawnable>(FindObjectsSortMode.None).Length>=before+2,"guest food spawn replicated from host");
            RatEatable eat=UnityEngine.Object.FindObjectsByType<RatEatable>(FindObjectsSortMode.None).FirstOrDefault(delegate(RatEatable e) { return !e.InstantlyEat && e.IsSpawned; });
            if(eat!=null)
            {
                eat.StartTimedInteractionServerRpc(new NetworkBehaviourReference(rat));
                yield return 0.7f;
                Check(eat==null || !eat.IsSpawned || !eat.IsTimingVar.Value,"guest timed interaction accelerates on host");
            }
            P.MenuOpen=true;
            yield return 1f;
            ScreenCapture.CaptureScreenshot(Path.Combine(root,"trainer-panel.png"));
            yield return 2f;
            Check(P.PatchErrors.Count==0 && P.Error.Length==0,"runtime patch and UI error checks");
            P.DisableAll();
            yield return 4f;
            Check(Mathf.Abs(rat.Movement.MaxSpeed-speed)<0.01f && !P.Bridge.EffectiveGlobal.HasChanges,"emergency stop restores local and remote effects");
            string hostDone=Path.Combine(Path.GetDirectoryName(root),"qa-host","host-done.txt");
            while(!File.Exists(hostDone)) yield return 0.5f;
            yield return 1f;
        }
    }
}
