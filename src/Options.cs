using System;

namespace RatTrainer
{
    [Serializable]
    public sealed class TrainerOptions
    {
        public int Version = 2;
        public bool Enabled = true;
        public bool InfiniteSprint;
        public bool NoHunger;
        public bool Invincible;
        public bool NoPoison;
        public bool NoStun;
        public bool NoFallDamage;
        public bool RatSpeedEnabled;
        public float RatSpeed = 1.5f;
        public bool JumpEnabled;
        public float Jump = 1.5f;
        public bool ReachEnabled;
        public float Reach = 2f;
        public bool ChefSpeedEnabled;
        public float ChefSpeed = 1.5f;
        public bool GrabEnabled;
        public float Grab = 2f;
        public bool PreviewAllHats;
        public bool AllowGuestRequests;
        public bool UseInGamePanel;
        public bool ShowHud;
        public float UiScale = 1f;
        public string MenuKey = "F6";
        public bool OpenOnLaunch;
        public WorldOptions World = new WorldOptions();

        public void Normalize()
        {
            if (Version < 2) { UseInGamePanel = ShowHud = OpenOnLaunch = false; }
            Version = 2;
            RatSpeed = Limits.Float(RatSpeed, 0.5f, 5f, 1.5f);
            Jump = Limits.Float(Jump, 0.5f, 4f, 1.5f);
            Reach = Limits.Float(Reach, 1f, 5f, 2f);
            ChefSpeed = Limits.Float(ChefSpeed, 0.5f, 4f, 1.5f);
            Grab = Limits.Float(Grab, 1f, 5f, 2f);
            UiScale = Limits.Float(UiScale, 0.75f, 1.5f, 1f);
            if (MenuKey != "F6" && MenuKey != "F8" && MenuKey != "Insert") MenuKey = "F6";
            if (World == null) World = new WorldOptions();
            World.Normalize();
        }

        public void DisableAll()
        {
            Enabled = false;
            InfiniteSprint = NoHunger = Invincible = NoPoison = NoStun = NoFallDamage = false;
            RatSpeedEnabled = JumpEnabled = ReachEnabled = ChefSpeedEnabled = GrabEnabled = false;
            PreviewAllHats = false;
            World = new WorldOptions();
        }

        public void RestrictToHostSandbox()
        {
            bool active = Enabled;
            WorldOptions world = World ?? new WorldOptions();
            DisableAll(); Enabled = active; World = world; World.InfiniteAmmo = false;
            AllowGuestRequests = UseInGamePanel = ShowHud = OpenOnLaunch = false;
            Normalize();
        }
    }

    [Serializable]
    public sealed class WorldOptions
    {
        public bool FastInteraction;
        public float InteractionSpeed = 3f;
        public bool InfiniteAmmo;
        public bool TaskTuning;
        public float TaskScale = 0.5f;
        public bool AiTuning;
        public bool FreezeAi;
        public float AiSpeed = 1f;
        public float AiHearing = 1f;
        public float AiVisionRange = 35f;
        public float AiSpotChance = 0.75f;

        public bool HasGlobalChanges { get { return TaskTuning || AiTuning || FreezeAi; } }
        public bool HasChanges { get { return HasGlobalChanges || FastInteraction || InfiniteAmmo; } }

        public void Normalize()
        {
            InteractionSpeed = Limits.Float(InteractionSpeed, 1f, 10f, 3f);
            TaskScale = Limits.Float(TaskScale, 0.1f, 3f, 0.5f);
            AiSpeed = Limits.Float(AiSpeed, 0.1f, 3f, 1f);
            AiHearing = Limits.Float(AiHearing, 0f, 3f, 1f);
            AiVisionRange = Limits.Float(AiVisionRange, 0f, 100f, 35f);
            AiSpotChance = Limits.Float(AiSpotChance, 0f, 1f, 0.75f);
        }
    }

    public static class Limits
    {
        public static float Float(float value, float min, float max, float fallback)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
        public static int Int(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
    }

    [Serializable]
    public sealed class BridgePacket
    {
        public int Version = 1;
        public string Kind;
        public int Sequence;
        public bool Accepted;
        public bool GlobalAccepted;
        public string Message;
        public string Item;
        public int Count;
        public WorldOptions Options;
    }
}
