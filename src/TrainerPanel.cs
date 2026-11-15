using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace RatTrainer
{
    public sealed class TrainerPanel
    {
        private readonly Plugin p;
        private readonly string[] pages = { "生存辅助", "移动与探索", "厨师工具", "任务与沙盒", "外观与存档", "设置与诊断" };
        private readonly Vector2[] scroll = new Vector2[6];
        private Rect window = new Rect(90, 70, 900, 650);
        private GUIStyle frame, heading, body, muted, button, selected, check, note, badge, hud;
        private Font font;
        private bool initialized;
        private int page, itemKind, itemCount = 3, chosenSkin, chosenHat, chosenBackup;
        private string[] backupFiles = new string[0];
        private float nextBackupRefresh;
        private float lastWidth;
        private readonly Color accent = new Color(0.96f, 0.76f, 0.25f);
        private readonly Color ink = new Color(0.91f, 0.93f, 0.94f);

        public TrainerPanel(Plugin plugin) { p = plugin; }
        private Texture2D Texture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1); texture.hideFlags = HideFlags.HideAndDontSave; texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
        private void Init()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" }, 18);
            frame = new GUIStyle(GUI.skin.window);
            frame.normal.background = Texture(new Color(0.075f, 0.086f, 0.10f, 0.99f));
            frame.onNormal.background = frame.normal.background;
            frame.padding = new RectOffset(0, 0, 0, 0);
            frame.border = new RectOffset(0, 0, 0, 0);
            heading = new GUIStyle(GUI.skin.label) { font = font, fontSize = 25, fontStyle = FontStyle.Bold };
            heading.normal.textColor = ink;
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true, richText = false };
            body.normal.textColor = ink;
            muted = new GUIStyle(body) { fontSize = 13 }; muted.normal.textColor = new Color(0.60f, 0.66f, 0.72f);
            note = new GUIStyle(body) { fontSize = 14, padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(0, 0, 2, 12) };
            note.normal.background = Texture(new Color(0.12f, 0.15f, 0.18f)); note.normal.textColor = new Color(0.78f, 0.83f, 0.88f);
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, padding = new RectOffset(12, 12, 9, 9), margin = new RectOffset(0, 8, 4, 4), wordWrap = true };
            button.normal.background = Texture(new Color(0.16f, 0.19f, 0.23f)); button.normal.textColor = ink;
            button.border = new RectOffset(0, 0, 0, 0);
            button.hover.background = Texture(new Color(0.24f, 0.28f, 0.33f)); button.hover.textColor = Color.white;
            button.active.background = Texture(new Color(0.32f, 0.29f, 0.18f)); button.active.textColor = Color.white;
            selected = new GUIStyle(button); selected.normal.background = Texture(new Color(0.29f, 0.25f, 0.14f)); selected.normal.textColor = accent;
            check = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 9, 9) };
            check.onNormal.background = selected.normal.background; check.onNormal.textColor = accent;
            check.onHover.background = selected.normal.background; check.onHover.textColor = accent;
            badge = new GUIStyle(muted) { fontSize = 14 }; badge.normal.textColor = accent;
            hud = new GUIStyle(note) { fontSize = 13 };
            initialized = true;
        }
        public void Draw()
        {
            if (!initialized) Init();
            Matrix4x4 old = GUI.matrix;
            Color oldColor = GUI.color, oldBackground = GUI.backgroundColor;
            GUI.color = Color.white; GUI.backgroundColor = Color.white;
            float scale = Mathf.Min(p.Options.UiScale, Mathf.Max(0.65f, Screen.width / 980f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                float w = Screen.width / scale, h = Screen.height / scale;
                if (!p.MenuOpen)
                {
                    if (p.Options.ShowHud) GUI.Label(new Rect(16, 16, 270, 58), "Rat Trainer  " + (p.Active ? "就绪" : "已关闭") + "\n" + p.Options.MenuKey + " 面板   F10 全部关闭", hud);
                    return;
                }
                window.width = Mathf.Min(900f, w - 20f); window.height = Mathf.Min(650f, h - 20f);
                if (lastWidth != w) { window.x = Mathf.Max(10f, (w - window.width) / 2f); window.y = Mathf.Max(10f, (h - window.height) / 2f); lastWidth = w; }
                window.x = Mathf.Clamp(window.x, 0f, w - window.width); window.y = Mathf.Clamp(window.y, 0f, h - window.height);
                GUI.depth = -1000;
                window = GUI.Window(664203, window, DrawWindow, "", frame);
            }
            finally { GUI.matrix = old; GUI.color = oldColor; GUI.backgroundColor = oldBackground; }
        }

        private void DrawWindow(int id)
        {
            GUI.Label(new Rect(22, 14, 400, 38), "RAT / 游戏工具箱", heading);
            GUI.Label(new Rect(24, 51, 500, 23), ConnectionText(), muted);
            if (GUI.Button(new Rect(window.width - 282, 18, 108, 36), p.Active ? "总开关：开" : "总开关：关", p.Active ? selected : button))
            {
                p.Options.Enabled = !p.Options.Enabled;
                if (!p.Active) { p.Players.Restore(); p.World.Restore(); p.Bridge.ResetRequests(); }
                p.Changed();
            }
            if (GUI.Button(new Rect(window.width - 168, 18, 104, 36), "全部关闭", button)) p.DisableAll();
            if (GUI.Button(new Rect(window.width - 54, 18, 34, 36), "×", button)) p.SetMenu(false);
            for (int i = 0; i < pages.Length; i++)
                if (GUI.Button(new Rect(16, 92 + i * 51, 164, 43), pages[i], page == i ? selected : button)) page = i;
            GUI.Label(new Rect(24, window.height - 152, 156, 75), "F7 记录位置\nF9 返回位置\nF10 全部关闭", muted);
            GUI.Label(new Rect(24, window.height - 58, 160, 28), "v1.3.0 · 备用面板", muted);
            GUILayout.BeginArea(new Rect(200, 90, window.width - 222, window.height - 160));
            scroll[page] = GUILayout.BeginScrollView(scroll[page]);
            switch (page)
            {
                case 0: Survival(); break;
                case 1: Explore(); break;
                case 2: Chef(); break;
                case 3: Sandbox(); break;
                case 4: Appearance(); break;
                default: Settings(); break;
            }
            GUILayout.Space(18);
            GUILayout.EndScrollView(); GUILayout.EndArea();
            GUI.Label(new Rect(202, window.height - 63, window.width - 224, 52), p.Status, note);
            GUI.DragWindow(new Rect(0, 0, window.width - 300, 75));
        }
        private string ConnectionText()
        {
            PlayerController player = p.LocalPlayer;
            string role = player is RatController ? "老鼠" : player is ChefController ? "厨师" : player is GhostController ? "幽灵" : "等待角色";
            return (Plugin.Connected ? (Plugin.IsHost ? "房主" : "客机 / 非房主") : "未连接房间") + "  ·  " + role;
        }
        private void Title(string text, string hint)
        {
            GUILayout.Label(text, heading); GUILayout.Space(2); GUILayout.Label(hint, note);
        }
        private void Toggle(string label, ref bool value, string hint)
        {
            bool next = GUILayout.Toggle(value, (value ? "●  " : "○  ") + label, check);
            if (next != value) { value = next; p.Changed(); }
            if (!String.IsNullOrEmpty(hint)) GUILayout.Label(hint, muted);
            GUILayout.Space(3);
        }
        private void Slider(string label, ref float value, float min, float max, string unit)
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(label, body); GUILayout.FlexibleSpace(); GUILayout.Label(value.ToString("0.00") + unit, badge, GUILayout.Width(85)); GUILayout.EndHorizontal();
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(22));
            if (Mathf.Abs(next - value) > 0.0001f) { value = next; p.Changed(); }
        }
        private void Section(string text) { GUILayout.Space(15); GUILayout.Label(text, body); GUILayout.Space(4); }
        private void ActionButton(string label, Action action, bool enabled)
        {
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            if (GUILayout.Button(label, button)) p.Run(action);
            GUI.enabled = previous;
        }
        private bool Support()
        {
            GUILayout.Label(p.Bridge.Status, note);
            if (!p.Bridge.Available) GUILayout.Label("此组功能需要房主安装同一修改器，并在设置页开启“允许客机协同”。", muted);
            return p.Bridge.Available && p.Active;
        }
        private void Survival()
        {
            Title("生存辅助", "本机功能 · 非房主也可使用。作用于自己控制的老鼠。");
            TrainerOptions o = p.Options;
            Toggle("无限冲刺体力", ref o.InfiniteSprint, "保持冲刺条，清除耗尽后的等待。");
            Toggle("保持饱食", ref o.NoHunger, "通过游戏接口定时补充饱食度，向房间同步。");
            Toggle("生命保护", ref o.Invincible, "保护活着的老鼠；不能解除被抓，也不会复活已生成的幽灵。");
            Toggle("免疫中毒", ref o.NoPoison, "清除毒素和中毒计时。");
            Toggle("免疫眩晕", ref o.NoStun, "保持可行动状态。");
            Toggle("免疫跌落惩罚", ref o.NoFallDamage, "移除高处落地触发的眩晕。");
            ActionButton("立即恢复生命、体力与饱食", p.Players.Heal, p.Active && p.LocalPlayer is RatController);
        }
        private void Explore()
        {
            Title("移动与探索", "本机功能 · 角色移动由自己的客户端同步。关闭开关会恢复原始参数。");
            TrainerOptions o = p.Options;
            Toggle("老鼠移动倍率", ref o.RatSpeedEnabled, "同时调整加速度与最高速度。");
            Slider("移动速度", ref o.RatSpeed, 0.5f, 5f, "×");
            Toggle("跳跃倍率", ref o.JumpEnabled, "建议配合免疫跌落惩罚使用。");
            Slider("跳跃力度", ref o.Jump, 0.5f, 4f, "×");
            Toggle("扩大交互距离", ref o.ReachEnabled, "仍受物体碰撞、遮挡和交互条件限制。");
            Slider("交互范围", ref o.Reach, 1f, 5f, "×");
            Section("位置书签");
            GUILayout.Label(p.Players.Bookmark, note);
            GUILayout.BeginHorizontal();
            ActionButton("记录位置 · F7", p.Players.StorePosition, p.Active && p.LocalPlayer != null);
            ActionButton("返回位置 · F9", p.Players.ReturnPosition, p.Active && p.LocalPlayer != null);
            GUILayout.EndHorizontal();
            GUILayout.Label("书签只在当前地图和当前角色有效。被抓、藏入物品或死亡时不能传送。", muted);
        }
        private void Chef()
        {
            Title("厨师工具", "速度与抓取范围是本机功能，非房主厨师也可使用。");
            TrainerOptions o = p.Options;
            Toggle("厨师移动倍率", ref o.ChefSpeedEnabled, "调整站立与蹲行加速度。");
            Slider("厨师速度", ref o.ChefSpeed, 0.5f, 4f, "×");
            Toggle("扩大抓取距离", ref o.GrabEnabled, "仍需准星对准可抓取目标。");
            Slider("抓取范围", ref o.Grab, 1f, 5f, "×");
            Section("弹药同步");
            bool allowed = Support(), previous = GUI.enabled; GUI.enabled = allowed;
            Toggle("无限弹药", ref o.World.InfiniteAmmo, "房主为你当前持有的枪补充真实弹药；关闭后恢复开启前的弹药数。");
            GUI.enabled = previous;
        }
        private void Sandbox()
        {
            Title("任务与沙盒", "房主直接使用；非房主通过协同通道使用。AI 与任务设置会影响整局。");
            bool allowed = Support(), previous = GUI.enabled; GUI.enabled = allowed;
            WorldOptions o = p.Options.World;
            Toggle("长按交互加速", ref o.FastInteraction, "只加快自己正在进行的长按交互。");
            Slider("交互速度", ref o.InteractionSpeed, 1f, 10f, "×");
            Toggle("调整任务工作量", ref o.TaskTuning, "厕所、铃铛、蜡烛、警报、推瓶、放水、啤酒与燃气任务。电脑谜题数量保持生成时的值。");
            Slider("次数 / 时间比例", ref o.TaskScale, 0.1f, 3f, "×");
            Toggle("调整 AI 厨师", ref o.AiTuning, "只影响机器人厨师，不影响其他真人玩家。");
            Slider("AI 移速", ref o.AiSpeed, 0.1f, 3f, "×");
            Slider("AI 听觉范围", ref o.AiHearing, 0f, 3f, "×");
            Slider("AI 视觉距离", ref o.AiVisionRange, 0f, 100f, "m");
            Slider("看见时识别概率", ref o.AiSpotChance, 0f, 1f, "");
            Toggle("暂停 AI 厨师", ref o.FreezeAi, "停止 AI 决策与移动；关闭后恢复。");
            GUI.enabled = previous;
            if (!Plugin.IsHost && o.HasGlobalChanges && p.Bridge.Available && !p.Bridge.GlobalAccepted)
                GUILayout.Label("全局设置尚未获得控制权；个人交互和弹药设置仍可独立同步。", note);
            Section("生成物品");
            itemKind = GUILayout.Toolbar(itemKind, new[] { "食物", "杂物", "子弹", "枪" }, button);
            GUILayout.Label("数量：" + itemCount + "  ·  单次最多 10，本局最多保留 50", muted);
            itemCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(itemCount, 1, 10));
            ActionButton("在角色附近生成", delegate { p.Bridge.Spawn(new[] { "food", "item", "bullet", "gun" }[itemKind], itemCount); }, allowed && Plugin.Connected);
        }
        private void Appearance()
        {
            Title("外观与存档", "本机功能 · 写入外观前自动备份。关闭总开关不会撤销已经保存的外观。");
            Toggle("临时开放游戏内帽子选择", ref p.Options.PreviewAllHats, "不改动解锁列表；关闭后恢复正常解锁判断。");
            ActionButton("解锁游戏内帽子并保存", p.Appearance.UnlockAll, GameManager.instance != null);
            List<RatHatData> hats = p.Appearance.Hats();
            List<string> names = new List<string> { "不戴帽子" };
            foreach (RatHatData hat in hats) names.Add(hat.NameId);
            Section("选择外观");
            chosenHat = Mathf.Clamp(chosenHat, 0, names.Count - 1);
            chosenHat = GUILayout.SelectionGrid(chosenHat, names.ToArray(), 3, button);
            int count = p.Appearance.SkinCount();
            if (count > 0)
            {
                chosenSkin = Mathf.Clamp(chosenSkin, 0, count - 1);
                GUILayout.Label("肤色：" + (chosenSkin + 1) + " / " + count, body);
                chosenSkin = Mathf.RoundToInt(GUILayout.HorizontalSlider(chosenSkin, 0, Mathf.Max(1, count - 1)));
                chosenSkin = Mathf.Clamp(chosenSkin, 0, count - 1);
                ActionButton("应用并保存外观", delegate { p.Appearance.SetAppearance(chosenSkin, chosenHat == 0 ? -1 : hats[chosenHat - 1].Id); }, true);
            }
            else GUILayout.Label("进入老鼠角色或游戏自带的外观页面后，可选择肤色。", muted);
            Section("存档备份与恢复");
            ActionButton("立即创建备份", delegate { p.Appearance.Backup(); nextBackupRefresh = 0f; }, SettingsScreen.instance != null);
            if (Time.unscaledTime >= nextBackupRefresh) { backupFiles = p.Appearance.Backups(); nextBackupRefresh = Time.unscaledTime + 3f; }
            if (backupFiles.Length > 0)
            {
                chosenBackup = Mathf.Clamp(chosenBackup, 0, backupFiles.Length - 1);
                List<string> labels = new List<string>(); foreach (string file in backupFiles) labels.Add(Path.GetFileNameWithoutExtension(file));
                chosenBackup = GUILayout.SelectionGrid(chosenBackup, labels.ToArray(), 1, button);
                ActionButton("恢复选中的备份", delegate { p.Appearance.Restore(backupFiles[chosenBackup]); nextBackupRefresh = 0f; }, SettingsScreen.instance != null);
            }
            else GUILayout.Label("还没有备份。", muted);
            GUILayout.Label("帽子列表不包含单独商品绑定的专属内容。", muted);
        }
        private void Settings()
        {
            Title("设置与诊断", "配置自动保存。全局沙盒开关与客机授权每次启动都保持关闭。");
            Toggle("显示角落提示", ref p.Options.ShowHud, "面板关闭时显示快捷键。");
            Toggle("启动时打开面板", ref p.Options.OpenOnLaunch, "下次启动生效。");
            Slider("界面缩放", ref p.Options.UiScale, 0.75f, 1.5f, "×");
            GUILayout.Label("面板快捷键", body);
            string[] keys = { "F6", "F8", "Insert" };
            int key = Array.IndexOf(keys, p.Options.MenuKey), next = GUILayout.Toolbar(Mathf.Max(0, key), keys, button);
            if (next != key) { p.Options.MenuKey = keys[next]; p.Changed(); }
            Section("房主协同");
            GUILayout.Label(p.Bridge.Status, note);
            bool previous = GUI.enabled; GUI.enabled = Plugin.IsHost;
            Toggle("允许客机协同", ref p.Options.AllowGuestRequests, "让装有同一修改器的客机使用弹药、长按交互、AI、任务和物品生成功能。离线时不可开启。");
            GUI.enabled = previous;
            ActionButton("重新检测房主支持", p.Bridge.DetectAgain, Plugin.Connected && !Plugin.IsHost);
            Section("运行状态");
            GUILayout.Label("目标版本：Steam Build 26403247\n已安装补丁：" + p.PatchCount + "  ·  失败：" + p.PatchErrors.Count + "\n" + ConnectionText(), body);
            if (p.Error.Length > 0) GUILayout.Label(p.Error, note);
            foreach (string error in p.PatchErrors) GUILayout.Label(error, muted);
            ActionButton("写入诊断报告", delegate { p.WriteDiagnostics(); p.Notify("诊断报告已保存到 BepInEx/config/RatTrainer/diagnostics.json"); }, true);
            GUILayout.Label("F10 会关闭全部修改并恢复可还原参数。物品生成、任务已完成进度和已保存外观不会自动回退。", muted);
        }
    }
}
