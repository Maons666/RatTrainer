using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace RatTrainerDesktop
{
    public sealed class PackageFile { public string Relative; public string Hash; }
    public sealed class PackageManifest { public string Version; public List<PackageFile> Files = new List<PackageFile>(); }
    public sealed class InstalledFile { public string Relative; public string InstalledHash; public string Backup; }
    public sealed class InstallRecord
    {
        public int Version = 1;
        public string GamePath;
        public string GameAssemblyHash;
        public List<InstalledFile> Files = new List<InstalledFile>();
        public List<string> Components = new List<string>();
    }
    internal sealed class PlannedFile { public string Relative, Target, Hash, Backup; public byte[] Bytes; }

    internal static partial class Installer
    {
        public const string GameHash = "517DD87905AC3E2DCF845384C440293AEAAA82773F6977964AD459070A21E1C1";
        private const string PluginFile = @"BepInEx\plugins\RatTrainer\RatTrainer.dll";
        private static string InRoot(string root, string relative)
        {
            if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("安装清单包含无效路径");
            string result = Path.GetFullPath(Path.Combine(root, relative));
            if (!result.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装路径超出游戏目录");
            return result;
        }
        private static string Hash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }
        private static string FileHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        private static byte[] Entry(ZipArchive archive, string name)
        {
            ZipArchiveEntry item = archive.GetEntry(name.Replace('\\', '/'));
            if (item == null || item.Length > 32 * 1024 * 1024) throw new InvalidDataException("内置组件缺失或过大：" + name);
            using (Stream input = item.Open())
            using (MemoryStream output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
        }
        private static void RequireClosed(string root)
        {
            if (!File.Exists(InRoot(root, "Rat Game.exe"))) throw new InvalidOperationException("请选择包含 Rat Game.exe 的游戏安装目录。");
            foreach (Process process in Process.GetProcessesByName("Rat Game"))
            {
                using (process)
                {
                    string executable;
                    try { executable = process.MainModule.FileName; }
                    catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("检测到运行中的 Rat Game。请先关闭游戏。"); }
                    catch (InvalidOperationException) { continue; }
                    if (String.Equals(executable, InRoot(root, "Rat Game.exe"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("请先关闭 Rat Game，再安装或更新连接组件。");
                }
            }
        }

    }
}
