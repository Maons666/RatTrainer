using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using RatTrainer;

namespace RatTrainerDesktop
{
    internal static class JsonFile
    {
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 24 }; }
        public static string Serialize(object value) { return Serializer().Serialize(value); }
        public static T Parse<T>(string value) { return Serializer().Deserialize<T>(value); }
        public static T Clone<T>(T value) { return Parse<T>(Serialize(value)); }
        public static string Read(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }
        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static bool Recent(string utc, double seconds)
        {
            DateTime time;
            if (!DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) return false;
            double age = (DateTime.UtcNow - time.ToUniversalTime()).TotalSeconds;
            return age >= -5 && age <= seconds;
        }
    }

    public sealed class DesktopPreferences
    {
        public string GamePath = @"A:\Steam\steamapps\common\rat game";
        public bool TopMost;
        public TrainerOptions Draft = new TrainerOptions();
    }

    internal sealed class PendingCommand
    {
        public string Id;
        public string Operation;
        public DateTime Sent;
        public int Revision;
    }

    internal sealed class GameConnection
    {
        public readonly string ClientId = Guid.NewGuid().ToString("N");
        public string GamePath;
        public DesktopState State;
        public bool IsOnline;
        public string ReadError;
        private DateTime heartbeatAt;
        public readonly Dictionary<string, PendingCommand> Pending = new Dictionary<string, PendingCommand>();
        public string Root { get { return Path.Combine(GamePath, @"BepInEx\config\RatTrainer\desktop"); } }
        public bool WaitingForOptions
        {
            get { foreach (PendingCommand item in Pending.Values) if (item.Operation == "options" || item.Operation == "off") return true; return false; }
        }
        public void Poll()
        {
            IsOnline = false; ReadError = null;
            try
            {
                string path = Path.Combine(Root, "status.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 262144) return;
                DesktopState incoming = JsonFile.Parse<DesktopState>(JsonFile.Read(path));
                if (incoming == null || incoming.Api != 1 || incoming.Options == null || !JsonFile.Recent(incoming.Utc, 4)) return;
                Version version;
                if (!Version.TryParse(incoming.Version, out version) || version < new Version(1, 3, 0))
                { ReadError = "请关闭游戏后，点击安装 / 更新组件，再启动游戏。"; return; }
                Guid session;
                if (!Guid.TryParseExact(incoming.Session, "N", out session)) return;
                incoming.Options.Normalize();
                State = incoming; IsOnline = true;
                if (DateTime.UtcNow >= heartbeatAt)
                {
                    heartbeatAt = DateTime.UtcNow.AddSeconds(2);
                    JsonFile.Write(Path.Combine(Root, "heartbeat.json"), new DesktopHeartbeat { Client = ClientId, Session = State.Session, Utc = DateTime.UtcNow.ToString("o") });
                }
            }
            catch (IOException) { }
            catch (Exception e) { ReadError = e.Message; }
        }
        public string Send(DesktopRequest request, int revision)
        {
            if (!IsOnline || State == null) throw new InvalidOperationException("请先安装连接组件并启动游戏，等待状态显示“已连接”。");
            request.Id = Guid.NewGuid().ToString("N"); request.Client = ClientId;
            request.Edition = Edition.Key;
            if (Edition.HostOnly && !State.Host && request.Operation != "off" && request.Operation != "diagnostics")
                throw new InvalidOperationException("请先创建自己的房间；房主沙盒版仅在房主状态下可操作。");
            request.Session = State.Session; request.SentUtc = DateTime.UtcNow.ToString("o");
            string name = DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-" + request.Id + ".json";
            JsonFile.Write(Path.Combine(Root, "requests", name), request);
            Pending[request.Id] = new PendingCommand { Id = request.Id, Operation = request.Operation, Sent = DateTime.UtcNow, Revision = revision };
            return request.Id;
        }
        public DesktopReply Receive(PendingCommand command)
        {
            string path = Path.Combine(Root, "replies", command.Id + ".json");
            if (File.Exists(path))
            {
                DesktopReply reply = JsonFile.Parse<DesktopReply>(JsonFile.Read(path));
                File.Delete(path);
                if (reply != null && reply.Id == command.Id) return reply;
            }
            if ((DateTime.UtcNow - command.Sent).TotalSeconds > 13)
                return new DesktopReply { Id = command.Id, Success = false, Message = "游戏没有确认请求。请查看连接状态；未将此操作标记为成功。" };
            return null;
        }
    }

    internal static class AppPaths
    {
        public static string UserDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RatTrainer"); } }
        public static string Preferences { get { return Path.Combine(UserDirectory, "desktop-" + Edition.Key + ".json"); } }
        public static string FindGame()
        {
            string known = @"A:\Steam\steamapps\common\rat game";
            if (File.Exists(Path.Combine(known, "Rat Game.exe"))) return known;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string steam = key == null ? null : key.GetValue("SteamPath") as string;
                    if (!String.IsNullOrEmpty(steam))
                    {
                        string direct = Path.Combine(steam, @"steamapps\common\rat game");
                        if (File.Exists(Path.Combine(direct, "Rat Game.exe"))) return direct;
                        string libraries = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                        if (File.Exists(libraries))
                            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                            {
                                string candidate = Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), @"steamapps\common\rat game");
                                if (File.Exists(Path.Combine(candidate, "Rat Game.exe"))) return candidate;
                            }
                    }
                }
            }
            catch (Exception) { }
            return known;
        }
        public static void OpenFolder(string path)
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("文件夹尚未创建：" + path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
    }
}
