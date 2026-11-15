using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace RatTrainer
{
    public sealed class AppearanceTools
    {
        private readonly Plugin p;
        public AppearanceTools(Plugin plugin) { p = plugin; }
        public string SavePath { get { return Path.Combine(Application.persistentDataPath, "main.sav"); } }
        public string BackupDirectory { get { return Path.Combine(p.DataDirectory, "backups"); } }
        private SharedGameSettings Settings
        {
            get
            {
                if (SettingsScreen.instance == null || SharedGameSettings.Current == null) throw new InvalidOperationException("游戏设置尚未加载");
                return SharedGameSettings.Current;
            }
        }
        public string[] Backups()
        {
            return Directory.Exists(BackupDirectory) ? Directory.GetFiles(BackupDirectory, "*.sav").OrderByDescending(delegate(string s) { return s; }).Take(20).ToArray() : new string[0];
        }
        public string Backup()
        {
            SettingsScreen.SaveSettings();
            string file = SafeFiles.Backup(SavePath, BackupDirectory);
            p.Notify("已备份存档：" + Path.GetFileName(file));
            return file;
        }
        public void Restore(string file)
        {
            string json = SafeFiles.ReadBackup(file, BackupDirectory);
            SharedGameSettings settings = SafeFiles.Parse<SharedGameSettings>(json);
            if (settings == null || settings.UnlockedHats == null || settings.UnlockedHats.Count > 500 || settings.SkinIndex < 0 || settings.SkinIndex > 100)
                throw new InvalidDataException("备份不是有效的 Rat Game 外观存档");
            Backup();
            SettingsScreen.instance.SharedGameSettings = settings;
            SettingsScreen.SaveSettings();
            Sync();
            p.Notify("已恢复选中的存档；恢复前的状态也已自动备份");
        }
        public void UnlockAll()
        {
            if (GameManager.instance == null) throw new InvalidOperationException("游戏管理器尚未加载");
            SharedGameSettings s = Settings;
            Backup();
            int count = 0;
            foreach (RatHatData hat in GameManager.instance.RatHats)
            {
                // musketeer is tied to the separately owned soundtrack; leave that entitlement alone.
                if (hat == null || hat.NameId == "musketeer" || s.UnlockedHats.Contains(hat.NameId)) continue;
                s.UnlockedHats.Add(hat.NameId); count++;
            }
            SettingsScreen.SaveSettings(); Sync();
            p.Notify("已保存 " + count + " 项游戏内帽子解锁；原存档已备份");
        }
        public List<RatHatData> Hats()
        {
            if (GameManager.instance == null) return new List<RatHatData>();
            return GameManager.instance.RatHats.Where(delegate(RatHatData hat) { return hat != null && hat.NameId != "musketeer"; }).ToList();
        }
        public int SkinCount()
        {
            RatController rat = p.LocalPlayer as RatController;
            if (rat != null && rat.RatAnimation != null) return rat.RatAnimation.RatMaterials.Count;
            RatAnimation animation = UnityEngine.Object.FindFirstObjectByType<RatAnimation>();
            return animation != null ? animation.RatMaterials.Count : 0;
        }
        public void SetAppearance(int skin, int hatId)
        {
            SharedGameSettings s = Settings;
            if (skin < 0 || skin >= SkinCount()) throw new InvalidOperationException("请在老鼠角色或外观预览场景中选择肤色");
            if (hatId != -1)
            {
                RatHatData hat = Hats().Find(delegate(RatHatData h) { return h.Id == hatId; });
                if (hat == null) throw new InvalidOperationException("帽子不存在");
                if (!GameManager.instance.HatIsUnlocked(hat.NameId)) throw new InvalidOperationException("请先解锁帽子，或开启临时全帽子预览");
            }
            Backup();
            s.SkinIndex = skin; s.HatIndex = hatId;
            SettingsScreen.SaveSettings(); Sync();
            p.Notify("外观已保存并向房间同步");
        }
        private void Sync()
        {
            if (Plugin.Connected && GameManager.instance != null) GameManager.instance.NotifyServerOfAppearanceUpdate();
            if (GameCanvas.instance != null && GameCanvas.instance.CurrentScreen is CustomizationScreen)
                ((CustomizationScreen)GameCanvas.instance.CurrentScreen).EnterScreen();
        }
    }
}
