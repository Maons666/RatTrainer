using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RatTrainer
{
    public static class SafeFiles
    {
        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 12,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static string Json(object value) { return JsonConvert.SerializeObject(value, Formatting.Indented, JsonSettings); }
        public static T Parse<T>(string json) { return JsonConvert.DeserializeObject<T>(json, JsonSettings); }

        public static string ReadShared(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }

        public static void WriteAtomic(string path, string text)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static string Backup(string source, string directory)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("存档尚未创建", source);
            Directory.CreateDirectory(directory);
            string result = Path.Combine(directory, "main-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".sav");
            File.Copy(source, result, false);
            return result;
        }

        public static string ReadBackup(string file, string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(file);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("备份必须位于修改器的 backups 文件夹中");
            if (new FileInfo(full).Length > 1024 * 1024) throw new InvalidDataException("存档文件过大");
            return File.ReadAllText(full, Encoding.UTF8);
        }
    }
}
