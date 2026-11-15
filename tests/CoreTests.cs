using System;
using System.IO;
using RatTrainer;

internal static class CoreTests
{
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static void Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        TrainerOptions o=new TrainerOptions();
        o.RatSpeed=Single.NaN; o.Jump=Single.PositiveInfinity; o.Reach=-10f; o.UiScale=100f; o.World.InteractionSpeed=1e10f; o.World.AiSpotChance=-1f;
        o.Normalize();
        Check(o.RatSpeed==1.5f && o.Jump==1.5f && o.Reach==1f && o.UiScale==1.5f && o.World.InteractionSpeed==10f && o.World.AiSpotChance==0f, "invalid and unbounded inputs are normalized");
        o.InfiniteSprint=o.NoHunger=o.Invincible=o.RatSpeedEnabled=true; o.World.FreezeAi=true; o.World.InfiniteAmmo=true;
        o.DisableAll();
        Check(!o.Enabled && !o.InfiniteSprint && !o.NoHunger && !o.Invincible && !o.RatSpeedEnabled && !o.World.HasChanges, "emergency stop clears local and world effects");
        TrainerOptions restored=SafeFiles.Parse<TrainerOptions>(SafeFiles.Json(o));
        Check(!restored.Enabled && restored.RatSpeed==o.RatSpeed && !restored.World.HasChanges, "configuration round trip preserves disabled state");
        bool malformed=false; try { SafeFiles.Parse<TrainerOptions>("{ broken"); } catch { malformed=true; }
        Check(malformed,"corrupt JSON is rejected");
        TrainerOptions injected=SafeFiles.Parse<TrainerOptions>("{\"$type\":\"System.IO.FileInfo, mscorlib\",\"Enabled\":false}");
        Check(injected!=null && !injected.Enabled, "JSON cannot select arbitrary runtime types");
        string save=Path.Combine(root,"main.sav"),backups=Path.Combine(root,"backups");
        SafeFiles.WriteAtomic(save,"{\"UnlockedHats\":[\"frog\"]}");
        string backup=SafeFiles.Backup(save,backups), original=File.ReadAllText(save);
        SafeFiles.WriteAtomic(save,"new content");
        Check(File.ReadAllText(save)=="new content" && File.ReadAllText(backup)==original,"atomic replacement preserves the independent backup");
        SafeFiles.WriteAtomic(save,SafeFiles.ReadBackup(backup,backups));
        Check(File.ReadAllText(save)==original,"backup restore preserves exact original content");
        bool escaped=false; try { SafeFiles.ReadBackup(save,backups); } catch(InvalidOperationException) { escaped=true; }
        Check(escaped,"restore rejects paths outside the backup directory");
        Check(Directory.GetFiles(root,"*.tmp-*").Length==0,"atomic writes leave no temporary files");
        BridgePacket packet=SafeFiles.Parse<BridgePacket>(SafeFiles.Json(new BridgePacket { Kind="settings", Options=new WorldOptions { FreezeAi=true } }));
        Check(packet.Version==1 && packet.Kind=="settings" && packet.Options.FreezeAi,"network settings survive serialization");
        Console.WriteLine(passed+" tests passed.");
    }
}
