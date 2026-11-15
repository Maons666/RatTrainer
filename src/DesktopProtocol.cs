using System;
using System.Collections.Generic;

namespace RatTrainer
{
    [Serializable]
    public sealed class DesktopRequest
    {
        public int Api = 1;
        public string Id;
        public string Client;
        public string Session;
        public string SentUtc;
        public string Operation;
        public string Edition = "all";
        public TrainerOptions Options;
        public string Item;
        public int Count;
        public int Skin;
        public int Hat = -1;
        public string Backup;
    }
    [Serializable]
    public sealed class DesktopReply
    {
        public string Id;
        public string Session;
        public bool Success;
        public string Message;
    }
    [Serializable]
    public sealed class DesktopHeartbeat
    {
        public string Client;
        public string Session;
        public string Utc;
    }
    [Serializable]
    public sealed class DesktopHat
    {
        public int Id;
        public string Name;
    }
    [Serializable]
    public sealed class DesktopState
    {
        public int Api = 1;
        public string Version = "1.3.0";
        public string Session;
        public string Utc;
        public int ProcessId;
        public bool Connected;
        public bool Host;
        public string Role;
        public bool BridgeAvailable;
        public bool GlobalAccepted;
        public string BridgeStatus;
        public TrainerOptions Options;
        public WorldOptions EffectiveWorld;
        public float Health = -1f;
        public float Hunger = -1f;
        public float Sprint = -1f;
        public string Position;
        public string Bookmark;
        public int Patches;
        public List<string> PatchErrors = new List<string>();
        public string Error;
        public string Message;
        public List<DesktopHat> Hats = new List<DesktopHat>();
        public int SkinCount;
        public int CurrentSkin;
        public int CurrentHat = -1;
        public List<string> Backups = new List<string>();
    }
}
