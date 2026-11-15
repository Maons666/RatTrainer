using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RatTrainer
{
    // Local file IPC. All game operations execute from Plugin.Update on Unity's main thread.
    public sealed class DesktopLink
    {
        private readonly Plugin p;
        private readonly string session = Guid.NewGuid().ToString("N");
        private readonly string root, requests, replies;
        private readonly Dictionary<string, DesktopReply> completed = new Dictionary<string, DesktopReply>();
        private readonly Queue<string> completedOrder = new Queue<string>();
        private float pollAt, stateAt, catalogAt, leaseStart;
        private string controller;
        private bool hostOnlyController;
        private List<DesktopHat> hats = new List<DesktopHat>();
        private List<string> backups = new List<string>();
        private int skins;

        public DesktopLink(Plugin plugin)
        {
            p = plugin;
            root = Path.Combine(p.DataDirectory, "desktop");
            requests = Path.Combine(root, "requests"); replies = Path.Combine(root, "replies");
            Directory.CreateDirectory(requests); Directory.CreateDirectory(replies);
        }
        public void Tick()
        {
            if (Time.unscaledTime >= pollAt)
            {
                pollAt = Time.unscaledTime + 0.1f;
                foreach (string file in Directory.GetFiles(requests, "*.json").OrderBy(delegate(string s) { return s; }).Take(8)) Process(file);
                CheckLease();
            }
            if (Time.unscaledTime >= stateAt) { stateAt = Time.unscaledTime + 0.5f; Publish(); }
        }
        private static bool Fresh(string value, double seconds)
        {
            DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)) return false;
            double age = (DateTime.UtcNow - parsed.ToUniversalTime()).TotalSeconds;
            return age >= -5 && age <= seconds;
        }
        private void CheckLease()
        {
            if (controller != null && hostOnlyController && !Plugin.IsHost)
            {
                controller = null;
                p.DisableAll();
                p.Notify("已退出房主状态，房主沙盒修改已关闭。");
                return;
            }
            if (controller == null || Time.unscaledTime - leaseStart < 10f) return;
            bool alive = false;
            try
            {
                string file = Path.Combine(root, "heartbeat.json");
                if (File.Exists(file) && new FileInfo(file).Length < 4096)
                {
                    DesktopHeartbeat heartbeat = SafeFiles.Parse<DesktopHeartbeat>(SafeFiles.ReadShared(file));
                    alive = heartbeat != null && heartbeat.Client == controller && heartbeat.Session == session && Fresh(heartbeat.Utc, 10);
                }
            }
            catch (IOException) { return; }
            if (!alive)
            {
                controller = null;
                p.DisableAll();
                p.Notify("桌面程序已断开，已关闭修改并恢复参数。");
            }
        }
        private void Process(string file)
        {
            DesktopRequest command = null;
            try
            {
                if (new FileInfo(file).Length > 65536) throw new InvalidDataException("桌面请求过大");
                command = SafeFiles.Parse<DesktopRequest>(SafeFiles.ReadShared(file));
                Guid id, client;
                if (command == null || !Guid.TryParseExact(command.Id, "N", out id) || !Guid.TryParseExact(command.Client, "N", out client))
                    throw new InvalidDataException("桌面请求标识无效");
                DesktopReply reply;
                if (!completed.TryGetValue(command.Id, out reply))
                {
                    reply = new DesktopReply { Id = command.Id, Session = session };
                    try
                    {
                        if (command.Api != 1 || command.Session != session) throw new InvalidOperationException("游戏会话已变化，请重新连接");
                        if (!Fresh(command.SentUtc, 12)) throw new InvalidOperationException("请求已过期，未执行");
                        Execute(command); reply.Success = true; reply.Message = p.Status;
                    }
                    catch (Exception e) { reply.Success = false; reply.Message = e.GetBaseException().Message; }
                    completed[command.Id] = reply; completedOrder.Enqueue(command.Id);
                    while (completedOrder.Count > 128) completed.Remove(completedOrder.Dequeue());
                }
                SafeFiles.WriteAtomic(Path.Combine(replies, command.Id + ".json"), SafeFiles.Json(reply));
                File.Delete(file); stateAt = 0f;
            }
            catch (IOException) { /* A transient file lock is retried on the next poll. */ }
            catch (Exception e)
            {
                p.ReportError("桌面连接", e);
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        private void Execute(DesktopRequest command)
        {
            if (command.Edition == "host" && !Plugin.IsHost && command.Operation != "off" && command.Operation != "diagnostics")
                throw new InvalidOperationException("房主沙盒版只在自己创建的房间中生效；请先成为房主。");
            if (command.Edition == "host" && command.Operation != "options" && command.Operation != "off" && command.Operation != "diagnostics")
                throw new InvalidOperationException("Host sandbox command is not available");
            switch (command.Operation)
            {
                case "options":
                    if (command.Options == null) throw new InvalidOperationException("缺少设置数据");
                    command.Options.Normalize();
                    if (command.Edition == "host") command.Options.RestrictToHostSandbox();
                    p.HostSandboxMode = command.Edition == "host";
                    if (command.Options.World.HasChanges && Plugin.Connected && !p.Bridge.Available)
                        throw new InvalidOperationException("弹药、交互与沙盒功能需要房主安装并开启协同");
                    if (command.Options.AllowGuestRequests && !Plugin.IsHost)
                        throw new InvalidOperationException("只有当前房主可以开启客机协同");
                    p.Options = command.Options;
                    if (!p.Options.UseInGamePanel) p.SetMenu(false);
                    if (!p.Active) { p.Players.Restore(); p.World.Restore(); p.Bridge.ResetRequests(); }
                    p.Changed(); p.Save();
                    controller = command.Client; hostOnlyController = command.Edition == "host"; leaseStart = Time.unscaledTime;
                    p.Notify(p.Active ? "桌面设置已接收；角色功能即时应用，房间功能等待协同同步。" : "总开关已关闭，参数已恢复。");
                    break;
                case "off": p.DisableAll(); controller = null; break;
                case "heal": p.Players.Heal(); break;
                case "position.save": p.Players.StorePosition(); break;
                case "position.return": p.Players.ReturnPosition(); break;
                case "spawn": p.Bridge.Spawn(command.Item, Limits.Int(command.Count, 1, 10)); break;
                case "appearance.unlock": p.Appearance.UnlockAll(); catalogAt = 0f; break;
                case "appearance.apply": p.Appearance.SetAppearance(command.Skin, command.Hat); catalogAt = 0f; break;
                case "backup.create": p.Appearance.Backup(); catalogAt = 0f; break;
                case "backup.restore":
                    if (String.IsNullOrEmpty(command.Backup) || Path.GetFileName(command.Backup) != command.Backup) throw new InvalidOperationException("备份名称无效");
                    p.Appearance.Restore(Path.Combine(p.Appearance.BackupDirectory, command.Backup)); catalogAt = 0f; break;
                case "bridge.detect": p.Bridge.DetectAgain(); break;
                case "diagnostics": p.WriteDiagnostics(); p.Notify("诊断报告已写入游戏目录。"); break;
                default: throw new InvalidOperationException("不支持的桌面操作");
            }
        }
        private void Publish()
        {
            PlayerController player = p.LocalPlayer;
            RatController rat = player as RatController;
            if (Time.unscaledTime >= catalogAt)
            {
                catalogAt = Time.unscaledTime + 2f;
                hats = p.Appearance.Hats().Select(delegate(RatHatData h) { return new DesktopHat { Id = h.Id, Name = h.NameId }; }).ToList();
                backups = p.Appearance.Backups().Select(Path.GetFileName).ToList();
                skins = p.Appearance.SkinCount();
            }
            DesktopState state = new DesktopState
            {
                Session = session, Utc = DateTime.UtcNow.ToString("o"), ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                Connected = Plugin.Connected, Host = Plugin.IsHost, Role = player == null ? "none" : player.GetType().Name,
                BridgeAvailable = p.Bridge.Available, GlobalAccepted = Plugin.IsHost || p.Bridge.GlobalAccepted,
                BridgeStatus = p.Bridge.Status, Options = p.Options, EffectiveWorld = p.Bridge.EffectiveGlobal,
                Health = rat == null ? -1f : rat.Health, Hunger = rat == null ? -1f : rat.HungerAmountVar.Value,
                Sprint = rat == null ? -1f : rat.SprintAmountVar.Value,
                Position = player == null || player.CharacterController == null ? "" : player.CharacterController.transform.position.ToString("F2"),
                Bookmark = p.Players.Bookmark, Patches = p.PatchCount, PatchErrors = p.PatchErrors,
                Error = p.Error, Message = p.Status, Hats = hats, SkinCount = skins, Backups = backups
            };
            if (SettingsScreen.instance != null && SharedGameSettings.Current != null)
            {
                state.CurrentSkin = SharedGameSettings.Current.SkinIndex; state.CurrentHat = SharedGameSettings.Current.HatIndex;
            }
            SafeFiles.WriteAtomic(Path.Combine(root, "status.json"), SafeFiles.Json(state));
        }
    }
}
