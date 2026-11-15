using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using RatTrainer;

namespace RatTrainerDesktop
{
    internal sealed partial class MainForm
    {
        private Page NewPage(string title, string description, ArtKind art)
        { Page page = new Page(title, description, art); pages.Add(page); content.Controls.Add(page); page.Visible = false; return page; }
        private void Feature(Page page, string title, string hint, string flag, string number, decimal min, decimal max, decimal step, string unit, bool room, bool host)
        {
            FeatureRow row = new FeatureRow(delegate { return model; }, Changed, title, hint, flag, number, min, max, step, unit, room, host);
            features.Add(row); page.Add(row);
        }
        private void Toggle(Page page, string title, string hint, string flag, bool room)
        { Feature(page, title, hint, flag, null, 0, 1, 1, "", room, false); }
        private ActionButton Action(string text, string operation)
        {
            ActionButton button = Theme.Button(text); button.Width = Theme.S(Math.Max(144, text.Length*14+28)); button.Tag = operation;
            button.Click += delegate { Send(new DesktopRequest { Operation = operation }); }; actionButtons.Add(button); return button;
        }
        private ComboBox Combo(int width)
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface,
                ForeColor = Theme.Text, Font = Theme.Font(10.5f, false), Width = Theme.S(width), Height = Theme.S(36), IntegralHeight = false, DropDownHeight = Theme.S(250) };
        }
        private NumberBox Number(decimal min, decimal max, decimal value)
        { return new NumberBox { Minimum = min, Maximum = max, Value = value, Increment = 1 }; }
        private void BuildPages()
        {
            if (Edition.HostOnly) BuildHostPages(); else BuildAllPages();
            BuildAuxiliaryPage();
            BuildSettings();
            BuildAboutPage();
        }
        private void BuildAllPages()
        {
            Page survival = NewPage("小老鼠，放心跑。", "体力、饱食与生命保护。作用于自己的老鼠，非房主也可使用。", ArtKind.Rats);
            AddSurvival(survival, true);
            Page movement = NewPage("去厨房的每个角落。", "调整移动和交互范围，或为当前位置留下书签。", ArtKind.Rat);
            AddMovement(movement, true);
            Page chef = NewPage("轮到厨师出场。", "移动与抓取作用于自己；真实弹药需要房主支持。", ArtKind.Chef);
            AddChef(chef);
            Toggle(chef, "无限弹药", "补充当前持有枪械的弹药；关闭后恢复开启前数量", "World.InfiniteAmmo", true);
            chef.Info("非房主使用无限弹药，需要房主安装同版组件并开启“允许客机协同”。", 56);
            Page sandbox = NewPage("让厨房换个玩法。", "调整任务工作量和机器人厨师，再生成一点新道具。", ArtKind.Robot);
            worldStatus = sandbox.Info("全局 AI、任务与物品生成，需要房主协同。", 44);
            AddTasks(sandbox, false); sandbox.Section("机器人厨师"); AddAi(sandbox); sandbox.Section("物品生成"); AddSpawn(sandbox);
            BuildAppearance();
        }
        private void BuildHostPages()
        {
            Page ai = NewPage("今天，厨师听你的。", "为自己创建的房间调整机器人厨师。离开房主状态后自动关闭。", ArtKind.Robot);
            worldStatus = ai.Info("等待房主连接 · 可先调整参数，连接后应用", 44);
            AddAi(ai);
            Page tasks = NewPage("任务，按你的节奏。", "调整整局任务工作量，或加快自己的长按交互。", ArtKind.Rats);
            AddTasks(tasks, false);
            tasks.Info("任务比例覆盖厕所、推瓶、放水、啤酒、铃铛、蜡烛、警报与燃气。电脑谜题数量保留原值。", 68);

        }
        private void AddSurvival(Page page, bool complete)
        {
            Toggle(page, "无限冲刺体力", "保持冲刺条，清除耗尽后的等待", "InfiniteSprint", false);
            Toggle(page, "保持饱食", "定期补充饱食度，并同步到房间", "NoHunger", false);
            Toggle(page, "生命保护", "保护活着的老鼠；不解除被抓或复活幽灵", "Invincible", false);
            if (complete)
            {
                Toggle(page, "免疫中毒", "清除毒素与中毒计时", "NoPoison", false);
                Toggle(page, "免疫眩晕", "保持可行动状态", "NoStun", false);
                Toggle(page, "免疫跌落惩罚", "移除高处落地触发的眩晕", "NoFallDamage", false);
            }
            page.Actions(Action("恢复当前老鼠状态", "heal"));
        }
        private void AddMovement(Page page, bool position)
        {
            Feature(page, "老鼠移动速度", "同时调整加速度与最高速度", "RatSpeedEnabled", "RatSpeed", .5m, 5m, .1m, "倍", false, false);
            Feature(page, "跳跃力度", "关闭开关后恢复原来的跳跃参数", "JumpEnabled", "Jump", .5m, 4m, .1m, "倍", false, false);
            Feature(page, "交互范围", "仍受碰撞、遮挡与物品条件限制", "ReachEnabled", "Reach", 1m, 5m, .1m, "倍", false, false);
            if (position)
            {
                page.Section("位置书签"); bookmark = page.Info("尚未记录位置 · 仅限当前地图与角色", 48);
                page.Actions(Action("记录位置 · F7", "position.save"), Action("返回位置 · F9", "position.return"));
                page.Info("被抓、藏入物品或死亡时不能传送。", 44);
            }
        }
        private void AddChef(Page page)
        {
            Feature(page, "厨师移动速度", "调整站立与蹲行的移动参数", "ChefSpeedEnabled", "ChefSpeed", .5m, 4m, .1m, "倍", false, false);
            Feature(page, "抓取范围", "仍需准星对准可抓取目标", "GrabEnabled", "Grab", 1m, 5m, .1m, "倍", false, false);
        }
        private void AddTasks(Page page, bool ammo)
        {
            Feature(page, "长按交互加速", "只加快自己正在进行的长按交互", "World.FastInteraction", "World.InteractionSpeed", 1, 10, .5m, "倍", true, false);
            Feature(page, "任务工作量", "0.5 倍需要一半工作量；2 倍需要两倍", "World.TaskTuning", "World.TaskScale", .1m, 3m, .1m, "倍", true, false);
            if (ammo) Toggle(page, "无限弹药", "作用于自己持有的枪械，关闭后恢复原数量", "World.InfiniteAmmo", true);
        }
        private void AddAi(Page page)
        {
            Toggle(page, "暂停 AI 厨师", "停止机器人决策与移动，关闭后继续", "World.FreezeAi", true);
            Toggle(page, "自定义 AI 参数", "统一启用下方四项感知与移动设置", "World.AiTuning", true);
            Feature(page, "AI 移速", "相对于游戏原始移速", null, "World.AiSpeed", .1m, 3m, .1m, "倍", true, false);
            Feature(page, "AI 听觉范围", "0 表示关闭这一项听觉感知", null, "World.AiHearing", 0, 3, .1m, "倍", true, false);
            Feature(page, "AI 视觉距离", "调整机器人可以看见的距离", null, "World.AiVisionRange", 0, 100, 1, "米", true, false);
            Feature(page, "发现老鼠的概率", "0 到 1，数值越大越容易发现", null, "World.AiSpotChance", 0, 1, .05m, "", true, false);
            ActionButton explore = Theme.Button("轻松探索预设"); explore.Width = Theme.S(156);
            explore.Click += delegate { model.World.AiTuning = true; model.World.FreezeAi = false; model.World.AiSpeed = .5f; model.World.AiHearing = .4f; model.World.AiVisionRange = 15f; model.World.AiSpotChance = .25f; Changed(); };
            ActionButton reset = Theme.Button("恢复 AI 原值"); reset.Width = Theme.S(156);
            reset.Click += delegate { model.World.AiTuning = model.World.FreezeAi = false; Changed(); };
            page.Actions(explore, reset);
        }
        private void AddSpawn(Page page)
        {
            itemKind = Combo(230); itemKind.Items.AddRange(new object[] { "食物", "杂物", "子弹", "枪" }); itemKind.SelectedIndex = 0;
            itemCount = Number(1, 10, 3);
            page.Section("选择种类与数量"); page.Actions(itemKind, itemCount);
            ActionButton spawn = Theme.Button("在角色附近生成"); spawn.Primary = true; spawn.Width = Theme.S(196); spawn.Tag = "spawn"; actionButtons.Add(spawn);
            spawn.Click += delegate { Send(new DesktopRequest { Operation = "spawn", Item = new[] { "food", "item", "bullet", "gun" }[itemKind.SelectedIndex], Count = (int)itemCount.Value }); };
            page.Actions(spawn);
            page.Info("每次生成 1–10 个物品。同局最多保留 50 个由修改器生成的物品，关闭修改不会删除它们。", 68);
            if (Edition.HostOnly) page.Info("进入对局、取得角色并开启总开关后，生成按钮会解锁。", 48);
        }
        private void BuildAppearance()
        {
            Page page = NewPage("换顶帽子，再出发。", "预览外观、管理游戏内帽子，并为存档保留备份。", ArtKind.Rats);
            Toggle(page, "临时开放帽子选择", "不写入永久解锁列表；关闭后恢复正常判断", "PreviewAllHats", false);
            page.Actions(Action("解锁游戏内帽子并保存", "appearance.unlock"));
            page.Section("帽子与肤色"); hats = Combo(300); hats.Items.Add(new HatChoice(-1, "不戴帽子")); hats.SelectedIndex = 0; skin = Number(1, 1, 1);
            ActionButton equip = Theme.Button("应用外观"); equip.Width = Theme.S(128); equip.Tag = "appearance.apply"; actionButtons.Add(equip);
            equip.Click += delegate { HatChoice hat = hats.SelectedItem as HatChoice; Send(new DesktopRequest { Operation = "appearance.apply", Skin = (int)skin.Value-1, Hat = hat == null ? -1 : hat.Id }); };
            page.Actions(hats, skin, equip); page.Info("数字为肤色编号。角色与帽子列表由游戏读取；不包含单独商品绑定的专属帽子。", 56);
            page.Section("存档备份"); page.Actions(Action("立即备份", "backup.create"));
            backups = Combo(460); ActionButton restore = Theme.Button("恢复选中备份"); restore.Width = Theme.S(160); restore.Tag = "backup.restore"; actionButtons.Add(restore);
            restore.Click += delegate { if (backups.SelectedItem == null) { Note("请先选择备份", true); return; } Send(new DesktopRequest { Operation = "backup.restore", Backup = backups.SelectedItem.ToString() }); };
            page.Actions(backups, restore);
            ActionButton folder = Theme.Button("打开备份文件夹"); folder.Width = Theme.S(176); folder.Click += delegate { if (!preview) Run(delegate { AppPaths.OpenFolder(Path.Combine(link.GamePath, @"BepInEx\config\RatTrainer\backups")); }); }; page.Actions(folder);
            page.Info("解锁、保存外观与恢复备份之前，都会先备份当前存档。", 48);
        }
        private void BuildSettings()
        {
            Page page = NewPage("准备好，再进厨房。", "安装组件、选择目录、管理连接。更新与卸载前请先关闭游戏。", Edition.HostOnly ? ArtKind.Robot : ArtKind.Rat);
            page.Section("游戏目录");
            TableLayoutPanel path = new TableLayoutPanel { Height = Theme.S(52), ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, Theme.S(12)) };
            path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Theme.S(136)));
            pathBox = new TextBox { Text = link.GamePath, ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(10f, false) };
            pathBox.Dock=DockStyle.Top; pathBox.Margin=new Padding(0,Theme.S(7),Theme.S(12),0);
            browse = Theme.Button("选择目录"); browse.Width=Theme.S(124); browse.Anchor=AnchorStyles.Top|AnchorStyles.Right; browse.Margin=Padding.Empty; browse.Click += delegate { Browse(); };
            path.Controls.Add(pathBox,0,0); path.Controls.Add(browse,1,0); page.Add(path);
            apply = Theme.Button("将暂存设置应用到游戏"); apply.Width = Theme.S(236); apply.Primary = true; apply.Click += delegate { ApplyOptions(); }; page.Actions(apply);
            if (!Edition.HostOnly) Feature(page, "允许客机协同", "由房主开启，允许同版客机使用房间功能", "AllowGuestRequests", null, 0, 1, 1, "", false, true);
            if (!Edition.HostOnly) page.Actions(Action("重新检测房主支持", "bridge.detect"));
            page.Section("窗口偏好");
            TableLayoutPanel topRow = new TableLayoutPanel { Height = Theme.S(72), ColumnCount=2, RowCount=1, BackColor = Theme.Surface, Padding=new Padding(Theme.S(20),0,Theme.S(20),0), Margin = new Padding(0, 0, 0, Theme.S(8)) };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Theme.S(92)));
            Label topLabel = Theme.Label("窗口置顶", 11f, Theme.Text, true); topLabel.Dock=DockStyle.Fill; topLabel.TextAlign=ContentAlignment.MiddleLeft; topRow.Controls.Add(topLabel,0,0);
            Switch top = new Switch { Checked = preferences.TopMost, AccessibleName = "窗口置顶", Anchor=AnchorStyles.Right, Margin=Padding.Empty }; topRow.Controls.Add(top,1,0);
            top.CheckedChanged += delegate { preferences.TopMost = top.Checked; if (!preview) TopMost = top.Checked; SavePreferences(); }; page.Add(topRow);
            if (!Edition.HostOnly) Toggle(page, "游戏内备用面板", "启用后可按 F6 打开，平时使用桌面窗口即可", "UseInGamePanel", false);
            if (!Edition.HostOnly) Toggle(page, "游戏内快捷键提示", "只显示角落提示，不打开游戏内面板", "ShowHud", false);
            page.Info("关闭窗口会发送“全部关闭”。异常退出后，游戏组件约 10 秒内关闭由桌面程序启用的修改。", 64);
            page.Section("组件与诊断"); diagnostics = page.Info("连接后显示组件版本、补丁加载情况与游戏反馈。", 88);
            ActionButton uninstall = Theme.Button("卸载游戏连接组件"); uninstall.Width = Theme.S(204); uninstall.Click += async delegate { await Deploy(true); };
            page.Actions(Action("写入诊断报告", "diagnostics"), uninstall);
            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = Theme.S(160), BackColor = Theme.Surface, ForeColor = Theme.Muted, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(9f, false) }; page.Add(log);
        }
    }
}
