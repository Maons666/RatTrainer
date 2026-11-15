using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using RatTrainerDesktop;

internal static class AssistInstallerTests
{
    private static readonly List<string> results=new List<string>();
    private static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    private static void Check(bool condition,string name) { if(!condition) throw new Exception(name); results.Add("PASS "+name); }
    private static T Field<T>(object value,string name) { return (T)value.GetType().GetField(name,Hidden).GetValue(value); }
    private static void Call(Control value,string method,object argument) { value.GetType().GetMethod(method,Hidden).Invoke(value,new object[] {argument}); }
    private static IEnumerable<Control> Descendants(Control root) { foreach(Control child in root.Controls) { yield return child; foreach(Control nested in Descendants(child)) yield return nested; } }
    [STAThread]
    private static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            PayloadTest();
            foreach(float scale in new[] {1f,1.25f,1.5f}) UiTest(output,scale);
            InstallTest(args[1]);
            results.Add(results.Count+" checks passed. Game detection and live installation were not run.");
            File.WriteAllLines(Path.Combine(output,"regression.txt"),results.ToArray()); Console.WriteLine(results.Last()); return 0;
        }
        catch(Exception e) { results.Add("FAIL "+e); File.WriteAllLines(Path.Combine(output,"regression.txt"),results.ToArray()); Console.WriteLine(e); return 1; }
    }
    private static void PayloadTest()
    {
        using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("RatTrainer.Payload.zip"))
        using(ZipArchive archive=new ZipArchive(stream,ZipArchiveMode.Read))
        {
            string[] plugins=archive.Entries.Where(e=>e.FullName.StartsWith("BepInEx/plugins/",StringComparison.OrdinalIgnoreCase)).Select(e=>e.FullName).OrderBy(x=>x).ToArray();
            Check(plugins.Length==3&&plugins.All(x=>x.StartsWith("BepInEx/plugins/RatAssist/")),"only the two auxiliary plugins and their shared core are bundled");
            Check(!archive.Entries.Any(e=>e.FullName.EndsWith("RatTrainer.dll",StringComparison.OrdinalIgnoreCase)),"trainer backend is absent from standalone payload");
            Check(archive.GetEntry("winhttp.dll")!=null&&archive.GetEntry("BepInEx/core/BepInEx.dll")!=null,"loader is embedded for first-time installation");
            using(StreamReader reader=new StreamReader(archive.GetEntry("package-manifest.json").Open()))
            {
                PackageManifest manifest=JsonFile.Parse<PackageManifest>(reader.ReadToEnd());
                foreach(PackageFile file in manifest.Files)
                {
                    using(Stream entry=archive.GetEntry(file.Relative.Replace('\\','/')).Open())
                    using(SHA256 hash=SHA256.Create()) Check(BitConverter.ToString(hash.ComputeHash(entry)).Replace("-","")==file.Hash,"payload hash "+file.Relative);
                }
            }
        }
    }
    private static void Contained(Control control,string message) { Check(control.Parent.ClientRectangle.Contains(control.Bounds),message); }
    private static void Layout(MainForm form,string label)
    {
        var cards=Descendants(form).OfType<InstallCard>().ToArray();
        Check(cards.Length==2,label+" exactly two install cards");
        foreach(InstallCard card in cards)
        {
            foreach(Control child in card.Controls) Contained(child,label+" card control "+child.Text);
            Check(!card.Install.Bounds.IntersectsWith(card.Status.Bounds)&&!card.Status.Bounds.IntersectsWith(card.Remove.Bounds),label+" install, status and uninstall do not overlap");
        }
        Contained(Field<TextBox>(form,"pathBox"),label+" path field contained"); Contained(Field<ActionButton>(form,"browse"),label+" directory picker contained");
        var header=Field<TableLayoutPanel>(form,"shellHeader"); foreach(Control c in header.Controls) Contained(c,label+" header control");
        for(int i=0;i<header.Controls.Count;i++) for(int j=i+1;j<header.Controls.Count;j++) Check(!header.Controls[i].Bounds.IntersectsWith(header.Controls[j].Bounds),label+" header separation");
        Check(form.ClientSize==form.Size,label+" no extra system frame space");
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle,int attribute,out int value,int size);
    private static void UiTest(string output,float scale)
    {
        Theme.Dpi=scale;
        using(MainForm form=new MainForm(true))
        {
            form.Opacity=0; form.ShowInTaskbar=false; form.Show(); Application.DoEvents(); Layout(form,scale+" initial");
            if(scale==1f) { Control root=form.Controls[0]; using(Bitmap image=new Bitmap(root.Width,root.Height)) { root.DrawToBitmap(image,root.ClientRectangle); image.Save(Path.Combine(output,"installer.png")); } }
            Check(Descendants(form).OfType<Label>().Any(l=>l.Text.Contains("MaOnStEr969")),"author credit is present");
            Check(!Descendants(form).OfType<Switch>().Any(),"installer has no trainer switches");
            form.Size=form.MinimumSize; Application.DoEvents(); Layout(form,scale+" minimum");
            form.ClientSize=new Size(Theme.S(1180),Theme.S(900)); Application.DoEvents(); Layout(form,scale+" expanded");
            foreach(InstallCard card in Descendants(form).OfType<InstallCard>())
            {
                Call(card.Install,"OnMouseEnter",EventArgs.Empty);
                using(Bitmap bitmap=new Bitmap(card.Install.Width,card.Install.Height)) { card.Install.DrawToBitmap(bitmap,card.Install.ClientRectangle); Check(bitmap.GetPixel(0,0).ToArgb()==card.BackColor.ToArgb(),"install hover clears corner"); }
                Call(card.Install,"OnMouseLeave",EventArgs.Empty);
                card.Install.PerformClick(); Check(!Field<bool>(form,"busy"),"preview cannot install anything");
            }
            for(int n=0;n<3;n++)
            {
                SendMessage(form.Handle,0x86,IntPtr.Zero,IntPtr.Zero); SendMessage(form.Handle,0x86,new IntPtr(1),IntPtr.Zero);
                int enabled; Check(DwmGetWindowAttribute(form.Handle,1,out enabled,sizeof(int))>=0&&enabled==0,"native border stays disabled after focus change");
            }
            Rectangle normal=form.Bounds;
            Field<ActionButton>(form,"maximizeWindow").PerformClick(); Application.DoEvents(); Check(form.WindowState==FormWindowState.Maximized,"maximize works");
            Field<ActionButton>(form,"maximizeWindow").PerformClick(); Application.DoEvents(); Check(form.Bounds==normal,"restore returns original bounds");
            Field<ActionButton>(form,"minimizeWindow").PerformClick(); Application.DoEvents(); Check(form.WindowState==FormWindowState.Minimized,"minimize works");
            SendMessage(form.Handle,0x112,new IntPtr(0xF120),IntPtr.Zero); Application.DoEvents(); Layout(form,scale+" restored");
            form.GetType().GetField("busy",Hidden).SetValue(form,true); Field<ActionButton>(form,"closeWindow").PerformClick(); Check(!form.IsDisposed,"installation in progress prevents closing");
            form.GetType().GetField("busy",Hidden).SetValue(form,false); Field<ActionButton>(form,"closeWindow").PerformClick(); Check(form.IsDisposed,"close works normally when idle");
        }
    }
    private static string HashFile(string path) { using(SHA256 hash=SHA256.Create()) using(Stream file=File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-",""); }
    private static void InstallTest(string assembly)
    {
        string root=Path.Combine(Path.GetTempPath(),"RatAssist-installer-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,@"Rat Game_Data\Managed")); File.Copy(assembly,Path.Combine(root,@"Rat Game_Data\Managed\Assembly-CSharp.dll"));
        File.WriteAllText(Path.Combine(root,"Rat Game.exe"),"non-executable test fixture"); File.WriteAllText(Path.Combine(root,"preserve.txt"),"user data");
        Installer.GameClosedCheck=delegate(string path) { if(path!=root) throw new Exception("Test attempted to leave its isolated directory"); };
        const string trainer=@"BepInEx\plugins\RatTrainer\RatTrainer.dll",core=@"BepInEx\plugins\RatAssist\RatAssist.Core.dll",marker=@"BepInEx\plugins\RatAssist\RatAssist.Markers.dll";
        Func<string,bool> exists=delegate(string path) { return File.Exists(Path.Combine(root,path)); };
        Installer.InstallComponent(root,"markers"); Check(Installer.IsInstalled(root,"markers")&&!Installer.IsInstalled(root,"map")&&!exists(trainer),"markers install alone without trainer");
        string before=File.ReadAllText(Path.Combine(root,".rat-trainer-install.json")); Installer.InstallComponent(root,"markers");
        Check(File.ReadAllText(Path.Combine(root,".rat-trainer-install.json"))==before,"reinstall is idempotent");
        Installer.InstallComponent(root,"map"); Installer.UninstallComponent(root,"markers");
        Check(Installer.IsInstalled(root,"map")&&!exists(marker)&&exists(core),"uninstall markers preserves map and shared core");
        Installer.UninstallComponent(root,"map"); Check(!exists(core)&&!exists("winhttp.dll")&&!exists(".rat-trainer-install.json"),"last auxiliary uninstall removes owned loader");
        Check(File.ReadAllText(Path.Combine(root,"preserve.txt"))=="user data"&&HashFile(Path.Combine(root,@"Rat Game_Data\Managed\Assembly-CSharp.dll"))==Installer.GameHash,"original game and unrelated files remain unchanged");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root,trainer))); File.WriteAllText(Path.Combine(root,trainer),"existing trainer to preserve");
        JsonFile.Write(Path.Combine(root,".rat-trainer-install.json"),new InstallRecord {Version=2,GamePath=root,GameAssemblyHash=Installer.GameHash,Components=new List<string> {"trainer"},Files=new List<InstalledFile> {new InstalledFile {Relative=trainer,InstalledHash=HashFile(Path.Combine(root,trainer))}}});
        Installer.InstallComponent(root,"markers"); Installer.InstallComponent(root,"map"); Installer.UninstallComponent(root,"map"); Installer.UninstallComponent(root,"markers");
        Check(File.ReadAllText(Path.Combine(root,trainer))=="existing trainer to preserve"&&exists("winhttp.dll")&&!exists(core),"existing trainer is retained when both helpers are uninstalled");
        Installer.InstallComponent(root,"markers"); File.AppendAllText(Path.Combine(root,marker),"modified"); string modified=HashFile(Path.Combine(root,marker));
        Installer.UninstallComponent(root,"markers"); Check(HashFile(Path.Combine(root,marker))==modified&&exists(core)&&exists("winhttp.dll"),"modified plugin and dependencies are preserved");
        results.Add("Fixture retained for review: "+root);
    }
}
