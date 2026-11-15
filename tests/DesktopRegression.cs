using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RatTrainer;
using RatTrainerDesktop;
using RatAssist;

internal static class DesktopRegression
{
    private static int passed;
    private static readonly List<string> results=new List<string>();
    private static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    private static void Check(bool condition,string label)
    { if(!condition) throw new Exception(label); passed++; results.Add("PASS "+label); }
    private static T Field<T>(object value,string name) { return (T)value.GetType().GetField(name,Hidden).GetValue(value); }
    private static void Call(object value,string name,object arg) { value.GetType().GetMethod(name,Hidden).Invoke(value,new object[] { arg }); }
    private static IEnumerable<Control> Descendants(Control root)
    { foreach(Control c in root.Controls) { yield return c; foreach(Control child in Descendants(c)) yield return child; } }
    private static Rectangle ScreenRect(Control c) { return c.RectangleToScreen(c.ClientRectangle); }
    private static void Contained(Control child,string name)
    { Check(child.Width>0&&child.Height>0&&child.Parent.ClientRectangle.Contains(child.Bounds),name); }
    [STAThread]
    private static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            PolicyTests();
            foreach(float dpi in new[] {1f,1.25f,1.5f}) UiTests(output,dpi);
            if(!Edition.HostOnly&&args[1]!="--ui-only") InstallerTests(args[1]);
            results.Add(passed+" checks passed; no game processes inspected, launched or attached.");
            File.WriteAllLines(Path.Combine(output,Edition.Key+"-regression.txt"),results.ToArray()); return 0;
        }
        catch(Exception e)
        { results.Add("FAIL "+e); File.WriteAllLines(Path.Combine(output,Edition.Key+"-regression.txt"),results.ToArray()); return 1; }
    }
    private static void PolicyTests()
    {
        Check(TeamPolicy.CanSee(Side.Rats,1,Side.Rats,2),"rats see their faction");
        Check(!TeamPolicy.CanSee(Side.Rats,1,Side.Chef,2),"rats never see chef positions or pings");
        Check(TeamPolicy.CanSee(Side.Chef,1,Side.Chef,1),"chef sees self");
        Check(!TeamPolicy.CanSee(Side.Chef,1,Side.Chef,2)&&!TeamPolicy.CanSee(Side.Chef,1,Side.Rats,2),"chef never sees another player");
        Check(!TeamPolicy.CanSee(Side.None,1,Side.Rats,2)&&!TeamPolicy.CanSee(Side.Rats,1,Side.None,2),"unknown role remains hidden");
        long now=DateTime.UtcNow.Ticks;
        Check(TeamPolicy.Fresh(now,now,30)&&!TeamPolicy.Fresh(now-TimeSpan.TicksPerSecond*31,now,30)&&!TeamPolicy.Fresh(now+TimeSpan.TicksPerSecond*6,now,30),"expired and far-future markers rejected");
        Check(!TeamPolicy.Finite(Single.NaN)&&!TeamPolicy.Finite(Single.PositiveInfinity)&&!TeamPolicy.Finite(100001),"invalid map coordinates rejected");
        TrainerOptions o=new TrainerOptions { InfiniteSprint=true,NoHunger=true,Invincible=true,NoPoison=true,NoStun=true,NoFallDamage=true,RatSpeedEnabled=true,JumpEnabled=true,ReachEnabled=true,ChefSpeedEnabled=true,GrabEnabled=true,PreviewAllHats=true,AllowGuestRequests=true,UseInGamePanel=true,ShowHud=true,OpenOnLaunch=true };
        o.World.InfiniteAmmo=true; o.World.FastInteraction=true; o.World.AiTuning=true; o.World.TaskTuning=true; o.World.TaskScale=.4f; o.World.FreezeAi=true;
        o.RestrictToHostSandbox();
        Check(o.Enabled&&o.World.FastInteraction&&o.World.AiTuning&&o.World.TaskTuning&&o.World.FreezeAi&&o.World.TaskScale==.4f,"host keeps requested AI and task settings");
        Check(!o.World.InfiniteAmmo&&!o.InfiniteSprint&&!o.NoHunger&&!o.Invincible&&!o.NoPoison&&!o.NoStun&&!o.NoFallDamage&&!o.RatSpeedEnabled&&!o.JumpEnabled&&!o.ReachEnabled&&!o.ChefSpeedEnabled&&!o.GrabEnabled&&!o.PreviewAllHats&&!o.AllowGuestRequests&&!o.UseInGamePanel&&!o.ShowHud&&!o.OpenOnLaunch,"host rejects hidden personal, ammo and guest settings");
    }
    private static void Layout(MainForm form,string state)
    {
        Control header=Field<TableLayoutPanel>(form,"shellHeader");
        FlowLayoutPanel commands=Field<FlowLayoutPanel>(form,"shellCommands");
        FlowLayoutPanel windowControls=Field<FlowLayoutPanel>(form,"windowControls");
        Control connection=Field<Label>(form,"connection").Parent;
        Check(!ScreenRect(connection).IntersectsWith(ScreenRect(commands)),state+" connection and commands do not overlap");
        Contained(connection,state+" connection stays in header"); Contained(commands,state+" commands stay in header");
        Contained(windowControls,state+" window controls stay in header");
        Check(!ScreenRect(windowControls).IntersectsWith(ScreenRect(commands))&&!ScreenRect(windowControls).IntersectsWith(ScreenRect(connection)),state+" caption controls do not overlap app controls");
        foreach(Control c in windowControls.Controls) Contained(c,state+" caption control bounds");
        foreach(Control c in commands.Controls) Contained(c,state+" command "+c.Text+" stays in row");
        for(int i=0;i<commands.Controls.Count;i++) for(int j=i+1;j<commands.Controls.Count;j++)
            Check(!commands.Controls[i].Bounds.IntersectsWith(commands.Controls[j].Bounds),state+" command bounds "+i+"/"+j);
        Check(header.Height==Theme.S(80),state+" header height stable");
        Check(Field<Label>(form,"footer").Top>=0,state+" footer laid out initially");
    }
    private static void UiTests(string output,float dpi)
    {
        Theme.Dpi=dpi;
        using(MainForm form=new MainForm(true))
        {
            // Preserve the production chrome and initial size; never force a preparatory resize.
            form.Opacity=0; form.ShowInTaskbar=false; form.Show(); Application.DoEvents();
            string prefix=Edition.Key+" "+dpi;
            Layout(form,prefix+" first show");
            var nav=Field<List<ActionButton>>(form,"navigation"); var pages=Field<List<Page>>(form,"pages");
            Check(nav.Count==pages.Count&&pages.Count==(Edition.HostOnly?5:8),prefix+" edition pages match navigation");
            Check(nav.Any(x=>x.Text=="辅助功能安装"),prefix+" has auxiliary install page");
            Check(nav.Last().Text=="关于"&&pages.Last().Controls.Count>0,prefix+" About page is last");
            if(Edition.HostOnly) Check(!nav.Any(x=>x.Text=="物品生成"||x.Text=="房主角色"),prefix+" removed host pages absent");
            for(int i=0;i<nav.Count;i++)
            {
                nav[i].PerformClick(); Application.DoEvents(); Layout(form,prefix+" page "+i);
                foreach(FeatureRow row in Descendants(pages[i]).OfType<FeatureRow>()) foreach(Control child in row.Controls) Contained(child,prefix+" feature control");
                foreach(AuthorCard card in Descendants(pages[i]).OfType<AuthorCard>()) foreach(Control child in card.Controls) if(child.Visible) Contained(child,prefix+" author info bounds");
                if(dpi==1f) Save(form,Path.Combine(output,Edition.Key+"-"+(i+1).ToString("00")+".png"));
            }
            Contained(Field<TextBox>(form,"pathBox"),prefix+" settings path layout");
            Check(!Field<TextBox>(form,"pathBox").Bounds.IntersectsWith(Field<ActionButton>(form,"browse").Bounds),prefix+" path and browse do not overlap");
            var features=Field<List<FeatureRow>>(form,"features");
            if(Edition.HostOnly) Check(!features.Any(x=>Field<string>(x,"flagPath")=="AllowGuestRequests"||Field<string>(x,"flagPath")=="World.InfiniteAmmo"),prefix+" removed host settings absent");
            AuxiliaryCard marker=Field<AuxiliaryCard>(form,"markerInstall"); AuxiliaryCard map=Field<AuxiliaryCard>(form,"mapInstall");
            Check(marker.Install.Parent==marker&&map.Install.Parent==map,prefix+" two separate auxiliary installation controls");
            foreach(AuxiliaryCard card in new[] {marker,map}) foreach(Control child in card.Controls) Contained(child,prefix+" auxiliary card bounds");
            form.Size=form.MinimumSize; Application.DoEvents(); Layout(form,prefix+" minimum");
            form.ClientSize=new Size(Theme.S(1440),Theme.S(960)); Application.DoEvents(); Layout(form,prefix+" expanded");
            form.ClientSize=new Size(Theme.S(1220),Theme.S(840)); Application.DoEvents(); Layout(form,prefix+" restored");
            nav[0].PerformClick(); Application.DoEvents();
            ActionButton sample=nav[0]; Corners(sample,prefix+" normal");
            Call(sample,"OnMouseEnter",EventArgs.Empty); Corners(sample,prefix+" hover");
            Call(sample,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,20,20,0)); Corners(sample,prefix+" pressed");
            Call(sample,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,20,20,0)); Corners(sample,prefix+" released");
            Call(sample,"OnMouseLeave",EventArgs.Empty); Corners(sample,prefix+" mouse left");
            sample.Enabled=false; Corners(sample,prefix+" disabled"); sample.Enabled=true;
            Switch toggle=Field<Switch>(features[0],"toggle");
            if(toggle==null) throw new Exception("First feature must have a toggle");
            int changed=0; toggle.CheckedChanged+=delegate { changed++; };
            toggle.Checked=false; changed=0; Mouse(toggle,toggle.Width-8); Mouse(toggle,toggle.Width-8);
            Check(toggle.Checked&&changed==1,prefix+" on segment sets once, does not toggle back");
            Mouse(toggle,8); Mouse(toggle,8); Check(!toggle.Checked&&changed==2,prefix+" off segment sets once");
            Call(toggle,"OnKeyDown",new KeyEventArgs(Keys.Space)); Call(toggle,"OnKeyUp",new KeyEventArgs(Keys.Space));
            Check(toggle.Checked&&changed==3,prefix+" keyboard toggles once"); Corners(toggle,prefix+" switch after input");
            Call(toggle,"OnMouseEnter",EventArgs.Empty); Corners(toggle,prefix+" switch hover");
            Call(toggle,"OnMouseLeave",EventArgs.Empty); Corners(toggle,prefix+" switch leave");
            toggle.Enabled=false; Corners(toggle,prefix+" switch disabled"); toggle.Enabled=true;
            Check(!Field<Timer>(form,"timer").Enabled,prefix+" preview never polls game");
            ChromeTests(form,prefix,dpi==1f);
        }
    }
    private static IntPtr Position(Point point) { return new IntPtr(unchecked((point.Y<<16)|(point.X&0xffff))); }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowStyle(IntPtr handle,int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr handle,IntPtr dc,uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle,int attribute,out int value,int size);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    private static void ChromeTests(MainForm form,string prefix,bool actualScale)
    {
        Check(form.FormBorderStyle==FormBorderStyle.None&&(GetWindowStyle(form.Handle,-16)&0xC00000)==0,prefix+" no native caption style");
        NativeRect clientRect,windowRect; GetClientRect(form.Handle,out clientRect); GetWindowRect(form.Handle,out windowRect);
        Check(form.ClientSize==form.Size,prefix+" client fills the entire window (managed "+form.ClientSize+" / "+form.Size+"; native "+clientRect.Right+"x"+clientRect.Bottom+" / "+(windowRect.Right-windowRect.Left)+"x"+(windowRect.Bottom-windowRect.Top)+")");
        for(int cycle=0;cycle<3;cycle++)
        {
            SendMessage(form.Handle,0x86,IntPtr.Zero,IntPtr.Zero);
            CheckNativeFrame(form,prefix+" inactive "+cycle);
            SendMessage(form.Handle,0x86,new IntPtr(1),IntPtr.Zero);
            SendMessage(form.Handle,0x85,new IntPtr(1),IntPtr.Zero);
            CheckNativeFrame(form,prefix+" reactivated "+cycle);
        }
        Point[] locations={new Point(1,1),new Point(form.Width-2,1),new Point(1,form.Height-2),new Point(form.Width-2,form.Height-2),new Point(1,100),new Point(form.Width-2,100),new Point(100,1),new Point(100,form.Height-2)};
        int[] expected={13,14,16,17,10,11,12,15};
        for(int i=0;i<locations.Length;i++) Check(SendMessage(form.Handle,0x84,IntPtr.Zero,Position(form.PointToScreen(locations[i]))).ToInt32()==expected[i],prefix+" native edge hit test "+i);
        Check(SendMessage(form.Controls[0].Handle,0x84,IntPtr.Zero,Position(form.PointToScreen(new Point(1,100)))).ToInt32()==-1,prefix+" child routes resize hit to window");
        ActionButton minimize=Field<ActionButton>(form,"minimizeWindow"),maximize=Field<ActionButton>(form,"maximizeWindow"),close=Field<ActionButton>(form,"closeWindow");
        foreach(ActionButton button in new[] {minimize,maximize,close})
        {
            Corners(button,prefix+" window icon normal"); Call(button,"OnMouseEnter",EventArgs.Empty); Corners(button,prefix+" window icon hover");
            Call(button,"OnMouseLeave",EventArgs.Empty); Corners(button,prefix+" window icon leave");
            Check(!String.IsNullOrWhiteSpace(button.AccessibleName),prefix+" caption accessible name");
        }
        Rectangle normal=form.Bounds;
        maximize.PerformClick(); Application.DoEvents();
        Check(form.WindowState==FormWindowState.Maximized&&maximize.Glyph==WindowGlyph.Restore,prefix+" maximize button and restore icon");
        if(actualScale) Check(form.Bounds==Screen.FromControl(form).WorkingArea,prefix+" maximized window respects taskbar work area");
        Check(form.WindowHitTest(new Point(1,1))==1,prefix+" maximized window has no resize edges"); Layout(form,prefix+" maximized");
        maximize.PerformClick(); Application.DoEvents();
        Check(form.WindowState==FormWindowState.Normal&&maximize.Glyph==WindowGlyph.Maximize&&form.Bounds==normal,prefix+" restore returns previous bounds: "+form.Bounds+" / "+normal);
        minimize.PerformClick(); Application.DoEvents(); Check(form.WindowState==FormWindowState.Minimized,prefix+" minimize button works");
        SendMessage(form.Handle,0x112,new IntPtr(0xF120),IntPtr.Zero); Application.DoEvents(); Layout(form,prefix+" restored after minimize");
        CheckNativeFrame(form,prefix+" frame after minimizing and restoring");
        SendMessage(form.Handle,0x31A,IntPtr.Zero,IntPtr.Zero); CheckNativeFrame(form,prefix+" after theme refresh");
        SendMessage(form.Handle,0x31E,IntPtr.Zero,IntPtr.Zero); CheckNativeFrame(form,prefix+" after composition refresh");
        Call(Field<Label>(form,"connection"),"OnMouseDown",new MouseEventArgs(MouseButtons.Left,2,8,8,0)); Application.DoEvents();
        Check(form.WindowState==FormWindowState.Maximized,prefix+" double click drag region maximizes");
        Call(Field<Label>(form,"connection"),"OnMouseDown",new MouseEventArgs(MouseButtons.Left,2,8,8,0)); Application.DoEvents();
        Check(form.WindowState==FormWindowState.Normal,prefix+" double click drag region restores");
        FormClosingEventHandler cancel=delegate(object sender,FormClosingEventArgs e) { e.Cancel=true; };
        form.FormClosing+=cancel; close.PerformClick(); Application.DoEvents();
        Check(!form.IsDisposed&&form.Visible,prefix+" close respects cancellation guard");
        form.FormClosing-=cancel; close.PerformClick(); Application.DoEvents();
        Check(form.IsDisposed,prefix+" custom close follows normal form closing");
    }
    private static void CheckNativeFrame(MainForm form,string label)
    {
        int nativeRendering;
        Check(DwmGetWindowAttribute(form.Handle,1,out nativeRendering,sizeof(int))>=0&&nativeRendering==0,label+" system nonclient rendering remains disabled");
        // This checks native printing as well as DWM state. Opaque activation is also tested with Windows capture via ChromePreview.
        using(Bitmap image=new Bitmap(form.Width,form.Height))
        {
            using(Graphics graphics=Graphics.FromImage(image))
            {
                graphics.Clear(Color.Magenta); IntPtr dc=graphics.GetHdc();
                try { Check(PrintWindow(form.Handle,dc,0),label+" full-window capture"); }
                finally { graphics.ReleaseHdc(dc); }
            }
            Point[] points={new Point(0,form.Height/2),new Point(2,form.Height/2),new Point(Theme.S(32),0),new Point(Theme.S(32),form.Height-1)};
            foreach(Point p in points) Check(image.GetPixel(p.X,p.Y).ToArgb()==Theme.Sidebar.ToArgb(),label+" no native border at "+p+" ("+image.GetPixel(p.X,p.Y)+")");
        }
    }
    private static void Mouse(Control c,int x)
    { var e=new MouseEventArgs(MouseButtons.Left,1,x,c.Height/2,0); Call(c,"OnMouseDown",e); Call(c,"OnMouseUp",e); }
    private static void Corners(Control c,string name)
    {
        using(Bitmap b=new Bitmap(c.Width,c.Height))
        {
            c.DrawToBitmap(b,c.ClientRectangle); int color=c.Parent.BackColor.ToArgb();
            Check(b.GetPixel(0,0).ToArgb()==color&&b.GetPixel(b.Width-1,0).ToArgb()==color&&b.GetPixel(0,b.Height-1).ToArgb()==color&&b.GetPixel(b.Width-1,b.Height-1).ToArgb()==color,name+" four corners cleared to parent color");
        }
    }
    private static void Save(MainForm form,string path)
    { Control root=form.Controls[0]; using(Bitmap b=new Bitmap(root.Width,root.Height)) { root.DrawToBitmap(b,root.ClientRectangle); b.Save(path); } }
    private static void InstallerTests(string assembly)
    {
        string root=Path.Combine(Path.GetTempPath(),"RatTrainer-regression-"+Guid.NewGuid().ToString("N"));
        string fixtureRoot=Path.GetFullPath(root); Directory.CreateDirectory(Path.Combine(root,@"Rat Game_Data\Managed"));
        File.Copy(assembly,Path.Combine(root,@"Rat Game_Data\Managed\Assembly-CSharp.dll")); File.WriteAllText(Path.Combine(root,"Rat Game.exe"),"TEST FIXTURE; NOT EXECUTABLE");
        File.WriteAllText(Path.Combine(root,"preserve.txt"),"user data");
        Installer.GameClosedCheck=delegate(string path) { if(path!=fixtureRoot) throw new Exception("Installer test escaped fixture"); };
        Func<string,bool> exists=delegate(string path) { return File.Exists(Path.Combine(root,path)); };
        const string core=@"BepInEx\plugins\RatAssist\RatAssist.Core.dll", trainer=@"BepInEx\plugins\RatTrainer\RatTrainer.dll";
        Installer.InstallComponent(root,"markers");
        Check(Installer.IsInstalled(root,"markers")&&!Installer.IsInstalled(root,"map")&&!exists(trainer),"markers install independently without trainer or map");
        string record=File.ReadAllText(Path.Combine(root,".rat-trainer-install.json")); Installer.InstallComponent(root,"markers");
        Check(File.ReadAllText(Path.Combine(root,".rat-trainer-install.json"))==record,"marker reinstall idempotent");
        Installer.InstallComponent(root,"map"); Installer.Install(root); Installer.UninstallComponent(root,"markers");
        Check(Installer.IsInstalled(root,"map")&&exists(core)&&exists(trainer)&&!Installer.IsInstalled(root,"markers"),"removing markers keeps map, shared core and trainer");
        Installer.UninstallComponent(root,"map");
        Check(!exists(core)&&!Installer.IsInstalled(root,"map")&&exists(trainer)&&exists("winhttp.dll"),"removing the last auxiliary plugin keeps trainer and its loader");
        Installer.InstallComponent(root,"map"); Installer.Uninstall(root);
        Check(!exists(trainer)&&Installer.IsInstalled(root,"map")&&exists("winhttp.dll"),"removing trainer keeps independent map and loader");
        const string mapFile=@"BepInEx\plugins\RatAssist\RatAssist.TeamMap.dll";
        byte[] originalMap=File.ReadAllBytes(Path.Combine(root,mapFile)); File.AppendAllText(Path.Combine(root,mapFile),"fixture modification");
        byte[] changedMap=File.ReadAllBytes(Path.Combine(root,mapFile)); Installer.UninstallComponent(root,"map");
        Check(exists(core)&&exists("winhttp.dll")&&File.ReadAllBytes(Path.Combine(root,mapFile)).SequenceEqual(changedMap),"modified plugin and dependencies are retained");
        File.WriteAllBytes(Path.Combine(root,mapFile),originalMap);
        Installer.UninstallComponent(root,"map");
        Check(!exists(core)&&!exists("winhttp.dll")&&!exists(".rat-trainer-install.json"),"last component removes owned shared runtime");
        Check(File.ReadAllText(Path.Combine(root,"preserve.txt"))=="user data","uninstall keeps unrelated user file");
        Installer.Install(root);
        InstallRecord legacy=JsonFile.Parse<InstallRecord>(File.ReadAllText(Path.Combine(root,".rat-trainer-install.json"))); legacy.Version=1; legacy.Components.Clear();
        JsonFile.Write(Path.Combine(root,".rat-trainer-install.json"),legacy); Installer.InstallComponent(root,"markers");
        Installer.Uninstall(root); Check(Installer.IsInstalled(root,"markers")&&!exists(trainer),"legacy trainer record upgrades without removing markers");
        Installer.UninstallComponent(root,"markers");
        Installer.InstallComponent(root,"map"); Directory.CreateDirectory(Path.Combine(root,@"BepInEx\plugins\Other")); File.WriteAllText(Path.Combine(root,@"BepInEx\plugins\Other\Other.dll"),"unrelated mod");
        Installer.UninstallComponent(root,"map"); Check(exists("winhttp.dll")&&exists(@"BepInEx\plugins\Other\Other.dll"),"other mods keep shared loader");
        // Retain the fixture for inspection; this suite never deletes recursively.
        results.Add("Installer fixture: "+root);
    }
}
