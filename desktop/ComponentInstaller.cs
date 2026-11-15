using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RatTrainerDesktop
{
    internal static partial class Installer
    {
        internal static Action<string> GameClosedCheck=RequireClosed;
        private const string AssistCore=@"BepInEx\plugins\RatAssist\RatAssist.Core.dll";
        private const string MarkersFile=@"BepInEx\plugins\RatAssist\RatAssist.Markers.dll";
        private const string MapFile=@"BepInEx\plugins\RatAssist\RatAssist.TeamMap.dll";
        private static readonly string[] components={ "trainer","markers","map" };
        private static string ComponentFile(string component)
        {
            if(component=="trainer") return PluginFile;
            if(component=="markers") return MarkersFile;
            if(component=="map") return MapFile;
            throw new ArgumentException("未知组件");
        }
        private static string Name(string component) { return component=="markers"?"地图标点":component=="map"?"阵营实时地图":"游戏连接组件"; }
        private static bool Same(string a,string b) { return String.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
        private static bool IsKnownPlugin(string relative) { return Same(relative,PluginFile)||Same(relative,AssistCore)||Same(relative,MarkersFile)||Same(relative,MapFile); }
        private static bool IsRuntime(string relative)
        { return Same(relative,"winhttp.dll")||Same(relative,"doorstop_config.ini")||Same(relative,".doorstop_version")||Same(Path.GetDirectoryName(relative),@"BepInEx\core"); }
        private static bool Wanted(string relative,string component)
        { return IsRuntime(relative)||Same(relative,ComponentFile(component))||(component!="trainer"&&Same(relative,AssistCore)); }
        public static bool IsInstalled(string gamePath,string component)
        {
            string root=Path.GetFullPath(gamePath);
            return File.Exists(InRoot(root,ComponentFile(component))) && (component=="trainer"||File.Exists(InRoot(root,AssistCore)));
        }
        private static InstallRecord ReadRecord(string root)
        {
            string path=InRoot(root,".rat-trainer-install.json");
            InstallRecord record=File.Exists(path)?JsonFile.Parse<InstallRecord>(JsonFile.Read(path)):new InstallRecord();
            if(record==null||record.Files==null) throw new InvalidDataException("安装记录损坏，未修改文件。");
            if(record.Components==null) record.Components=new List<string>();
            foreach(InstalledFile file in record.Files)
            {
                string full=InRoot(root,file.Relative);
                string normalized=full.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1);
                if(!IsKnownPlugin(normalized)&&!IsRuntime(normalized)) throw new InvalidDataException("安装记录含非组件路径，未修改文件。");
                file.Relative=normalized;
                if(file.Backup!=null)
                {
                    string backup=InRoot(root,file.Backup);
                    string allowed=InRoot(root,@"BepInEx\config\RatTrainer\install-backups")+Path.DirectorySeparatorChar;
                    if(!backup.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装备份路径无效。");
                }
            }
            foreach(string component in components)
                if(record.Files.Any(delegate(InstalledFile file) { return Same(file.Relative,ComponentFile(component)); })&&!record.Components.Contains(component)) record.Components.Add(component);
            record.Components=record.Components.Where(delegate(string component) { return components.Contains(component); }).Distinct().ToList();
            return record;
        }
        public static string Install(string gamePath) { return InstallComponent(gamePath,"trainer"); }
        public static string InstallComponent(string gamePath,string component)
        {
            string selected=ComponentFile(component);
            string root=Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar); GameClosedCheck(root);
            if(root.Any(delegate(char c) { return c>127; })) throw new InvalidOperationException("请通过 Steam 将游戏安装到英文路径后再安装插件。");
            if(!Same(FileHash(InRoot(root,@"Rat Game_Data\Managed\Assembly-CSharp.dll")),GameHash)) throw new InvalidOperationException("当前游戏版本不是已适配的 Build 26403247。");
            InstallRecord record=ReadRecord(root);
            List<PlannedFile> planned=new List<PlannedFile>();
            using(Stream payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("RatTrainer.Payload.zip"))
            using(ZipArchive archive=new ZipArchive(payload,ZipArchiveMode.Read))
            {
                PackageManifest manifest=JsonFile.Parse<PackageManifest>(Encoding.UTF8.GetString(Entry(archive,"package-manifest.json")));
                if(manifest==null||manifest.Files==null||!manifest.Files.Any(delegate(PackageFile f) { return Same(f.Relative,selected); })) throw new InvalidDataException("内置组件不完整。");
                string core=InRoot(root,@"BepInEx\core\BepInEx.dll");
                if(File.Exists(core)) { if(!Same(FileHash(core),Hash(Entry(archive,@"BepInEx\core\BepInEx.dll")))) throw new InvalidOperationException("已有其他版本的 BepInEx，未覆盖。"); }
                else if(File.Exists(InRoot(root,"winhttp.dll"))) throw new InvalidOperationException("已有其他 winhttp.dll 加载器，未覆盖。");
                foreach(PackageFile file in manifest.Files)
                {
                    if(!Wanted(file.Relative,component)) continue;
                    string target=InRoot(root,file.Relative); byte[] bytes=Entry(archive,file.Relative);
                    if(!Same(Hash(bytes),file.Hash)) throw new InvalidDataException("内置文件校验失败："+file.Relative);
                    if(File.Exists(target))
                    {
                        if(Same(FileHash(target),file.Hash))
                        { InstalledFile prior=record.Files.Find(delegate(InstalledFile old) { return Same(old.Relative,file.Relative); }); if(prior!=null) prior.InstalledHash=file.Hash; continue; }
                        if(!IsKnownPlugin(file.Relative)) throw new InvalidOperationException("现有加载器文件有改动，未覆盖："+file.Relative);
                    }
                    planned.Add(new PlannedFile { Relative=file.Relative,Target=target,Hash=file.Hash,Bytes=bytes });
                }
            }
            List<PlannedFile> changed=new List<PlannedFile>();
            try
            {
                foreach(PlannedFile file in planned)
                {
                    if(File.Exists(file.Target))
                    {
                        file.Backup=@"BepInEx\config\RatTrainer\install-backups\"+Guid.NewGuid().ToString("N")+".bak";
                        string backup=InRoot(root,file.Backup); Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(file.Target,backup);
                    }
                    changed.Add(file); Directory.CreateDirectory(Path.GetDirectoryName(file.Target)); File.WriteAllBytes(file.Target,file.Bytes);
                    InstalledFile prior=record.Files.Find(delegate(InstalledFile old) { return Same(old.Relative,file.Relative); });
                    if(prior==null) record.Files.Add(new InstalledFile { Relative=file.Relative,InstalledHash=file.Hash,Backup=file.Backup }); else prior.InstalledHash=file.Hash;
                }
                if(!record.Components.Contains(component)) record.Components.Add(component);
                record.Version=2; record.GamePath=root; record.GameAssemblyHash=GameHash;
                JsonFile.Write(InRoot(root,".rat-trainer-install.json"),record);
            }
            catch
            {
                foreach(PlannedFile file in changed.AsEnumerable().Reverse())
                    if(file.Backup!=null) File.Copy(InRoot(root,file.Backup),file.Target,true); else if(File.Exists(file.Target)) File.Delete(file.Target);
                throw;
            }
            return Name(component)+"已安装 / 更新。下次启动游戏自动加载。"+(component=="trainer"?"":"无需开启 Trainer 或总开关。");
        }
        public static string Uninstall(string gamePath) { return UninstallComponent(gamePath,"trainer"); }
        public static string UninstallComponent(string gamePath,string component)
        {
            ComponentFile(component);
            string root=Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar); GameClosedCheck(root);
            string recordPath=InRoot(root,".rat-trainer-install.json");
            if(!File.Exists(recordPath)) return "没有本工具的安装记录，未删除文件。";
            InstallRecord record=ReadRecord(root); List<string> remaining=record.Components.Where(delegate(string value) { return value!=component; }).ToList();
            InstalledFile selected=record.Files.Find(delegate(InstalledFile file) { return Same(file.Relative,ComponentFile(component)); });
            if(selected!=null&&File.Exists(InRoot(root,selected.Relative))&&!Same(FileHash(InRoot(root,selected.Relative)),selected.InstalledHash))
                return Name(component)+"文件已被修改，未卸载；所需的共享组件也已保留。";
            string plugins=InRoot(root,@"BepInEx\plugins");
            bool other=Directory.Exists(plugins)&&Directory.GetFiles(plugins,"*.dll",SearchOption.AllDirectories).Any(delegate(string file)
            {
                string relative=file.Substring(root.Length+1);
                InstalledFile owned=record.Files.Find(delegate(InstalledFile entry) { return Same(entry.Relative,relative); });
                return !IsKnownPlugin(relative)||owned==null||!Same(FileHash(file),owned.InstalledHash);
            });
            List<InstalledFile> remove=record.Files.Where(delegate(InstalledFile file)
            { return !remaining.Any(delegate(string owner) { return Wanted(file.Relative,owner); }) && !(other&&IsRuntime(file.Relative)); }).ToList();
            foreach(InstalledFile file in remove) if(file.Backup!=null&&!File.Exists(InRoot(root,file.Backup))) throw new InvalidDataException("备份缺失，未卸载："+file.Backup);
            int skipped=0;
            foreach(InstalledFile file in remove)
            {
                string target=InRoot(root,file.Relative);
                if(File.Exists(target))
                {
                    if(!Same(FileHash(target),file.InstalledHash)) { skipped++; continue; }
                    if(file.Backup!=null) File.Copy(InRoot(root,file.Backup),target,true); else File.Delete(target);
                }
                record.Files.Remove(file);
            }
            if(!record.Files.Any(delegate(InstalledFile file) { return Same(file.Relative,ComponentFile(component)); })) record.Components=remaining;
            if(record.Files.Count==0) File.Delete(recordPath); else JsonFile.Write(recordPath,record);
            return skipped>0?Name(component)+"卸载完成；保留了已被修改的文件。":Name(component)+"已卸载。其他插件、配置与备份已保留。";
        }
    }
}
