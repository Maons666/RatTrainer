using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RatTrainer;

namespace RatTrainerDesktop
{
    internal sealed partial class MainForm : Form
    {
        private DesktopPreferences preferences;
        private TrainerOptions model;
        private readonly bool preview;
        private readonly GameConnection link = new GameConnection();
        private readonly List<FeatureRow> features = new List<FeatureRow>();
        private readonly List<ActionButton> actionButtons = new List<ActionButton>();
        private readonly List<Page> pages = new List<Page>();
        private readonly List<ActionButton> navigation = new List<ActionButton>();
        private readonly Timer timer = new Timer { Interval = 600 };
        private Label connection, connectionHint, footer, live, worldStatus, bookmark, diagnostics;
        private TextBox pathBox, log;
        private ActionButton install, launch, apply, off, browse;
        private Switch master;
        private ComboBox hats, backups, itemKind;
        private NumberBox skin, itemCount;
        private Panel content;
        private bool dirty, busy, syncing;
        private int revision;
        private DateTime changedAt, noticeUntil;
        private string session, catalogSignature, backupSignature;
        private DesktopState state;
        private readonly string[] pageNames = Edition.HostOnly
            ? new[] { "AI 厨师", "任务规则", "辅助功能安装", "设置与连接", "关于" }
            : new[] { "生存辅助", "移动与探索", "厨师工具", "任务与沙盒", "外观与存档", "辅助功能安装", "设置与连接", "关于" };
        private bool CanApply { get { return link.IsOnline && !busy && (!Edition.HostOnly || (state != null && state.Host)); } }

        public MainForm(bool renderOnly)
        {
            preview = renderOnly;
            preferences = new DesktopPreferences();
            bool loadedPreferences = false;
            if (!preview)
            {
                preferences.GamePath = AppPaths.FindGame();
                string config = AppPaths.Preferences;
                if (!Edition.HostOnly && !File.Exists(config)) config = Path.Combine(AppPaths.UserDirectory, "desktop.json");
                try { if (File.Exists(config)) { preferences = JsonFile.Parse<DesktopPreferences>(JsonFile.Read(config)) ?? preferences; loadedPreferences = true; } } catch { }
            }
            model = preferences.Draft ?? new TrainerOptions(); model.Normalize();
            model.Enabled = false;
            dirty = loadedPreferences; changedAt = DateTime.MaxValue;
            if (Edition.HostOnly) model.RestrictToHostSandbox();
            link.GamePath = preferences.GamePath;
            Text = "Rat Trainer · " + Edition.Name;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(Theme.S(1220), Theme.S(840)); MinimumSize = new Size(Theme.S(1100), Theme.S(720));
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(9.5f, false);
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.F10 && !busy) { TurnOff(); e.SuppressKeyPress = true; } };
            TopMost = !preview && preferences.TopMost; Icon = SystemIcons.Application;
            BuildLayout(); BuildPages(); RouteResizeEdges(this); SelectPage(0); RefreshBindings();
            if (preview) { model.InfiniteSprint = !Edition.HostOnly; model.World.AiTuning = true; RefreshBindings(); Note("离线界面预览 · 未连接游戏", false); return; }
            timer.Tick += delegate { Poll(); }; timer.Start();
            Shown += delegate { Note("首次使用请先安装 / 更新组件，再启动游戏。", false); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy) { e.Cancel = true; Note("组件操作尚未完成，请稍候。", false); return; }
                timer.Stop();
                try { if (link.IsOnline) link.Send(new DesktopRequest { Operation = "off" }, revision); } catch { }
                SavePreferences();
            };
        }
        private static void Place(Control c, int x, int y, int width, int height) { c.SetBounds(Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(height)); }
        private void SelectPage(int index)
        {
            for (int i = 0; i < pages.Count; i++) { pages[i].Visible = i == index; navigation[i].Selected = i == index; navigation[i].Invalidate(); }
            pages[index].BringToFront();
        }
        private void Changed()
        {
            if (syncing) return;
            model.Normalize(); if (Edition.HostOnly) model.RestrictToHostSandbox(); revision++; dirty = true; changedAt = DateTime.UtcNow;
            Note(link.IsOnline ? "设置待确认" + (model.Enabled ? "" : " · 总开关当前关闭") : "设置已暂存 · 连接后可在设置页应用", false);
            RefreshBindings(); SavePreferences();
        }
        private void RefreshBindings()
        {
            syncing = true;
            try
            {
                foreach (FeatureRow row in features) row.RefreshValue(link.IsOnline, state, busy);
                master.Checked = model.Enabled; master.Enabled = !busy && (!Edition.HostOnly || !link.IsOnline || (state != null && state.Host));
                off.Enabled = !busy;
                foreach (ActionButton button in actionButtons)
                {
                    string operation = button.Tag as string;
                    button.Enabled = operation == "diagnostics" ? link.IsOnline && !busy : CanApply;
                    if (operation == "spawn") button.Enabled = CanApply && state != null && state.Connected && state.BridgeAvailable && model.Enabled;
                    if (operation == "heal" || operation == "position.save" || operation == "position.return") button.Enabled = CanApply && state != null && state.Connected;
                }
                if (apply != null) apply.Enabled = CanApply && !link.WaitingForOptions;
                if (browse != null) browse.Enabled = !busy;
                RefreshAuxiliaryStatus();
            }
            finally { syncing = false; }
        }
        private void Poll()
        {
            if (busy || preview) return;
            try
            {
                link.Poll(); state = link.IsOnline ? link.State : null;
                foreach (PendingCommand pending in link.Pending.Values.ToArray())
                {
                    DesktopReply reply = link.Receive(pending); if (reply == null) continue;
                    link.Pending.Remove(pending.Id);
                    if (pending.Operation == "options" && pending.Revision == revision)
                    { dirty = false; if (!reply.Success && state != null) model = JsonFile.Clone(state.Options); }
                    Note(reply.Message, !reply.Success);
                }
                if (state != null)
                {
                    bool first = session != state.Session; session = state.Session;
                    if (!dirty && !link.WaitingForOptions) { model = JsonFile.Clone(state.Options); if (Edition.HostOnly) model.RestrictToHostSandbox(); }
                    if (first && dirty) { changedAt = DateTime.MaxValue; Note("已连接 · 暂存设置可在设置页点击应用", false); }
                    if (dirty && CanApply && !link.WaitingForOptions && changedAt != DateTime.MaxValue && (DateTime.UtcNow-changedAt).TotalMilliseconds >= 450) ApplyOptions();
                    connection.Text = "● 已连接 · " + (state.Connected ? state.Host ? "房主" : "非房主" : "主菜单"); connection.ForeColor = Theme.Green;
                    connectionHint.Text = Edition.HostOnly && !state.Host ? "当前不是房主，沙盒操作已锁定" : Role(state.Role) + " · 组件 " + state.Version;
                    live.Text = "生命 " + Percent(state.Health) + "    饱食 " + Percent(state.Hunger) + "    体力 " + Percent(state.Sprint) + "       位置 " + (String.IsNullOrEmpty(state.Position) ? "—" : state.Position);
                    if (bookmark != null) bookmark.Text = "记录位置：" + state.Bookmark;
                    if (worldStatus != null) worldStatus.Text = state.BridgeStatus + (state.Options.World.HasGlobalChanges && !state.Host && !state.GlobalAccepted ? "\n全局 AI / 任务设置尚未获得控制权。" : "");
                    diagnostics.Text = "组件 v" + state.Version + " · 补丁 " + state.Patches + " · 失败 " + state.PatchErrors.Count + "\n" + state.BridgeStatus + "\n" + (String.IsNullOrEmpty(state.Error) ? "当前组件未报告错误" : state.Error);
                    if (hats != null) Catalog(state);
                    if (DateTime.UtcNow > noticeUntil) { footer.ForeColor = Theme.Text; footer.Text = dirty || link.WaitingForOptions ? "有设置等待游戏确认" : state.Message; }
                }
                else
                {
                    connection.Text = "● 等待游戏连接"; connection.ForeColor = Theme.Muted;
                    connectionHint.Text = Edition.HostOnly ? "仅在自己创建的房间中生效" : "连接后显示角色和实际状态";
                    live.Text = "生命 —    饱食 —    体力 —       离线设置尚未应用";
                    if (worldStatus != null) worldStatus.Text = Edition.HostOnly ? "等待房主连接。离线时可以预设参数。" : "全局 AI、任务和生成功能需要房主协同。";
                    if (DateTime.UtcNow > noticeUntil) { footer.ForeColor = Theme.Muted; footer.Text = link.ReadError ?? "首次使用：安装 / 更新组件 → 启动游戏 → 开启总开关"; }
                }
                RefreshBindings();
            }
            catch (Exception e) { Note("连接状态读取失败：" + e.GetBaseException().Message, true); }
        }
        private void Catalog(DesktopState data)
        {
            string newHats = String.Join("|", data.Hats.Select(delegate(DesktopHat h) { return h.Id + ":" + h.Name; }).ToArray());
            if (newHats != catalogSignature)
            {
                catalogSignature = newHats; hats.Items.Clear(); hats.Items.Add(new HatChoice(-1, "不戴帽子"));
                foreach (DesktopHat item in data.Hats) hats.Items.Add(new HatChoice(item.Id, item.Name));
                hats.SelectedIndex = 0; for (int i=0;i<hats.Items.Count;i++) if (((HatChoice)hats.Items[i]).Id == data.CurrentHat) hats.SelectedIndex = i;
                skin.Maximum = Math.Max(1, data.SkinCount); skin.Value = data.CurrentSkin + 1;
            }
            skin.Maximum = Math.Max(1, data.SkinCount); skin.Enabled = data.SkinCount > 0;
            string newBackups = String.Join("|", data.Backups.ToArray());
            if (newBackups != backupSignature)
            {
                backupSignature = newBackups; object selected = backups.SelectedItem; backups.Items.Clear();
                foreach (string item in data.Backups) backups.Items.Add(item);
                if (selected != null && backups.Items.Contains(selected)) backups.SelectedItem = selected; else if (backups.Items.Count > 0) backups.SelectedIndex = 0;
            }
        }
        private static string Role(string value) { return value == "RatController" ? "老鼠" : value == "ChefController" ? "厨师" : value == "GhostController" ? "幽灵" : "等待角色"; }
        private static string Percent(float value) { return value < 0 ? "—" : Math.Round(value*100).ToString("0") + "%"; }
        private void ApplyOptions()
        {
            if (preview || link.WaitingForOptions || !CanApply) return;
            if (Edition.HostOnly) model.RestrictToHostSandbox();
            Run(delegate { link.Send(new DesktopRequest { Operation = "options", Options = JsonFile.Clone(model) }, revision); Note("设置已发送，等待游戏确认", false); });
        }
        private void Send(DesktopRequest request)
        { if (!preview) Run(delegate { link.Send(request, revision); Note("操作已发送，等待游戏确认", false); }); }
        private void TurnOff()
        {
            model.DisableAll(); model.AllowGuestRequests = false; dirty = false; revision++;
            if (link.IsOnline) Send(new DesktopRequest { Operation = "off" }); else Note("暂存开关已全部关闭", false);
            RefreshBindings(); SavePreferences();
        }
        private void Browse()
        {
            if (preview) return;
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Rat Game 程序|Rat Game.exe", Title = "选择游戏目录中的 Rat Game.exe", CheckFileExists = true })
            {
                if (Directory.Exists(link.GamePath)) dialog.InitialDirectory = link.GamePath;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { if (link.IsOnline) link.Send(new DesktopRequest { Operation = "off" }, revision); } catch { }
                link.GamePath = Path.GetDirectoryName(dialog.FileName); link.State = null; link.IsOnline = false; link.Pending.Clear();
                state = null; session = null; catalogSignature = backupSignature = null;
                preferences.GamePath = link.GamePath; pathBox.Text = link.GamePath; SavePreferences(); Note("游戏目录已更新", false);
            }
        }
        private async Task Deploy(bool remove)
        {
            if (busy || preview) return;
            busy = true; install.Enabled = launch.Enabled = false; RefreshBindings();
            try { Note(remove ? "正在卸载连接组件…" : "正在安装内置组件…", false); string path = link.GamePath; string result = await Task.Run(delegate { return remove ? Installer.Uninstall(path) : Installer.Install(path); }); Note(result, false); }
            catch (Exception e) { Note(e.GetBaseException().Message, true); MessageBox.Show(this, e.GetBaseException().Message, "未完成", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { busy = false; install.Enabled = launch.Enabled = true; RefreshBindings(); }
        }
        private void SavePreferences()
        {
            if (preview) return;
            try { preferences.Draft = model; JsonFile.Write(AppPaths.Preferences, preferences); } catch (Exception e) { if (log != null) log.AppendText("设置保存失败：" + e.Message + Environment.NewLine); }
        }
        private void Run(System.Action action) { try { action(); } catch (Exception e) { Note(e.GetBaseException().Message, true); } }
        private void Note(string text, bool error)
        {
            footer.Text = text; footer.ForeColor = error ? Color.FromArgb(166, 68, 48) : Theme.Text; noticeUntil = DateTime.UtcNow.AddSeconds(5);
            if (log != null) { if (log.TextLength > 24000) log.Text = log.Text.Substring(log.TextLength-16000); log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
        }
        internal void RenderPages(string directory)
        {
            if (!preview) throw new InvalidOperationException("Offline rendering only");
            Directory.CreateDirectory(directory); ShowInTaskbar = false; Opacity = 0; Show();
            for (int i=0; i<pages.Count; i++)
            {
                SelectPage(i); PerformLayout(); Application.DoEvents();
                Control root=Controls[0];
                using (Bitmap image = new Bitmap(root.Width, root.Height)) { root.DrawToBitmap(image, root.ClientRectangle); image.Save(Path.Combine(directory, Edition.Key + "-" + (i+1).ToString("00") + ".png")); }
            }
            Close();
        }
        private sealed class HatChoice
        {
            public int Id; private readonly string name;
            public HatChoice(int id, string label) { Id=id; name=label; }
            public override string ToString() { return name; }
        }
    }
}
