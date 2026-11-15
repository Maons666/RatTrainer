using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RatTrainerDesktop
{
    internal sealed class InstallCard : Panel
    {
        public readonly ActionButton Install;
        public readonly LinkLabel Remove;
        public readonly Label Status;
        private readonly Label heading,description,note;
        public InstallCard(string title,string descriptionText,string help,string buttonText)
        {
            Height=Theme.S(156); Margin=new Padding(0,0,0,Theme.S(12)); BackColor=Theme.Surface;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            heading=Theme.Label(title,12f,Theme.Text,true); description=Theme.Label(descriptionText,9.5f,Theme.Muted,false); note=Theme.Label(help,9f,Theme.Accent,false);
            Install=Theme.Button(buttonText); Install.Primary=true;
            Status=Theme.Label("尚未安装",9f,Theme.Muted,false); Status.TextAlign=ContentAlignment.MiddleCenter;
            Remove=new LinkLabel { Text="卸载此插件",Font=Theme.Font(9f,false),TextAlign=ContentAlignment.MiddleCenter,LinkColor=Theme.Muted,ActiveLinkColor=Theme.Accent,VisitedLinkColor=Theme.Muted,DisabledLinkColor=Color.FromArgb(154,160,150),BackColor=Theme.Surface,LinkBehavior=LinkBehavior.HoverUnderline,Margin=Padding.Empty };
            Remove.Links.Clear(); Remove.Links.Add(0,Remove.Text.Length);
            Controls.AddRange(new Control[] { heading,description,note,Install,Status,Remove });
        }
        public void SetRemovalEnabled(bool enabled)
        { Remove.Links[0].Enabled=enabled; Remove.TabStop=enabled; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if(heading==null) return;
            int textWidth=Math.Max(Theme.S(180),Width-Theme.S(284));
            heading.SetBounds(Theme.S(20),Theme.S(16),textWidth,Theme.S(28));
            description.SetBounds(Theme.S(20),Theme.S(52),textWidth,Theme.S(54));
            note.SetBounds(Theme.S(20),Theme.S(118),textWidth,Theme.S(24));
            Install.SetBounds(Width-Theme.S(244),Theme.S(22),Theme.S(224),Theme.S(40));
            Status.SetBounds(Width-Theme.S(244),Theme.S(72),Theme.S(224),Theme.S(28));
            Remove.SetBounds(Width-Theme.S(244),Theme.S(108),Theme.S(224),Theme.S(28));
        }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; Theme.Box(e.Graphics,ClientRectangle,Theme.Surface,Theme.Border,Theme.S(12)); }
    }
    internal sealed class AssistPreferences { public string GamePath; }

    internal sealed partial class MainForm : Form
    {
        private readonly bool preview;
        private bool busy;
        private string gamePath;
        private TableLayoutPanel shellHeader;
        private Page page;
        private TextBox pathBox;
        private ActionButton browse;
        private Label footer;
        private InstallCard markers,map;

        public MainForm(bool renderOnly)
        {
            preview=renderOnly;
            gamePath=@"A:\Steam\steamapps\common\rat game";
            if(!preview)
            {
                gamePath=AppPaths.FindGame();
                try { if(File.Exists(AppPaths.Preferences)) { AssistPreferences saved=JsonFile.Parse<AssistPreferences>(JsonFile.Read(AppPaths.Preferences)); if(saved!=null&&!String.IsNullOrWhiteSpace(saved.GamePath)) gamePath=saved.GamePath; } } catch { }
            }
            Text="Rat Assist · "+Edition.Name;
            AutoScaleMode=AutoScaleMode.None; FormBorderStyle=FormBorderStyle.None; StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(Theme.S(1040),Theme.S(840)); MinimumSize=new Size(Theme.S(920),Theme.S(740));
            BackColor=Theme.Background; ForeColor=Theme.Text; Font=Theme.Font(9.5f,false); Icon=SystemIcons.Application;
            BuildLayout(); RouteResizeEdges(this); RefreshStatus();
            Activated+=delegate { if(!preview&&!busy) RefreshStatus(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy) { e.Cancel=true; Note("插件操作尚未完成，请稍候。",true); } };
        }
        private void BuildLayout()
        {
            SuspendLayout();
            TableLayoutPanel root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(64))); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(56))); Controls.Add(root);
            shellHeader=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=Theme.Surface,Margin=Padding.Empty,Padding=new Padding(Theme.S(24),0,Theme.S(16),0) };
            shellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); shellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); shellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); root.Controls.Add(shellHeader,0,0);
            Label brand=Theme.Label("Rat Assist · 辅助插件安装器",15f,Theme.Text,true); brand.Dock=DockStyle.Fill; brand.TextAlign=ContentAlignment.MiddleLeft; shellHeader.Controls.Add(brand,0,0);
            Label version=Theme.Label("v"+Edition.Version,9f,Theme.Muted,false); version.Size=new Size(Theme.S(70),Theme.S(36)); version.Anchor=AnchorStyles.Right; version.TextAlign=ContentAlignment.MiddleRight; shellHeader.Controls.Add(version,1,0);
            BuildWindowControls(); BindWindowDrag(shellHeader); BindWindowDrag(brand); BindWindowDrag(version);
            page=new Page("两项辅助，装好即用。","先关闭游戏，选择目录，再分别安装。下次启动游戏自动加载。",ArtKind.Rat); page.Margin=Padding.Empty; root.Controls.Add(page,0,1);
            TableLayoutPanel directory=new TableLayoutPanel { Height=Theme.S(48),ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,Theme.S(12)) };
            directory.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Theme.S(88))); directory.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); directory.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Theme.S(136)));
            Label directoryTitle=Theme.Label("游戏目录",10f,Theme.Text,true); directoryTitle.Dock=DockStyle.Fill; directoryTitle.TextAlign=ContentAlignment.MiddleLeft; directory.Controls.Add(directoryTitle,0,0);
            pathBox=new TextBox { ReadOnly=true,Text=gamePath,BorderStyle=BorderStyle.FixedSingle,BackColor=Theme.Surface,ForeColor=Theme.Text,Font=Theme.Font(10f,false),Dock=DockStyle.Top,Margin=new Padding(0,Theme.S(8),Theme.S(12),0) }; directory.Controls.Add(pathBox,1,0);
            browse=Theme.Button("选择目录"); browse.Width=Theme.S(124); browse.Anchor=AnchorStyles.Top|AnchorStyles.Right; browse.Margin=Padding.Empty; browse.Click+=delegate { Browse(); }; directory.Controls.Add(browse,2,0); page.Add(directory);
            markers=new InstallCard("地图标点","准心对准实体表面，按键标记，可透过障碍看到。\n小鼠看小鼠阵营的标点；厨师只看自己。","G 标点 · Shift + G 清除 · 默认保留 30 秒","安装地图标点插件");
            map=new InstallCard("阵营实时地图","按住 Tab 显示小鼠头像、玩家名称与平面位置。\n小鼠看小鼠阵营，厨师只看自己，不显示高度。","随 Tab 地图实时更新 · 本机安装即可显示队友位置","安装阵营位置插件");
            page.Add(markers); page.Add(map);
            markers.Install.Click+=async delegate { await Deploy("markers",false); }; map.Install.Click+=async delegate { await Deploy("map",false); };
            markers.Remove.LinkClicked+=async delegate { await Deploy("markers",true); }; map.Remove.LinkClicked+=async delegate { await Deploy("map",true); };
            page.Info("Steam 房间共享标点，需要队友也安装标点插件；非 Steam 房间标点仅本机可见。实时位置只需本机安装。安装后可关闭本程序，无需保持 Trainer 开着。",64);
            TableLayoutPanel status=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=new Padding(Theme.S(24),Theme.S(8),Theme.S(24),Theme.S(8)),BackColor=Theme.Surface };
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Theme.S(190))); root.Controls.Add(status,0,2);
            footer=Theme.Label(preview?"离线界面预览 · 未读取安装状态":"请先关闭游戏，再安装或卸载插件。",9f,Theme.Muted,false); footer.Dock=DockStyle.Fill; footer.AutoEllipsis=true; footer.TextAlign=ContentAlignment.MiddleLeft; status.Controls.Add(footer,0,0);
            Label author=Theme.Label("作者  "+Edition.Author,9f,Theme.Muted,false); author.Dock=DockStyle.Fill; author.TextAlign=ContentAlignment.MiddleRight; status.Controls.Add(author,1,0);
            ResumeLayout(true);
        }
        private void Browse()
        {
            if(preview||busy) return;
            using(OpenFileDialog dialog=new OpenFileDialog { Filter="Rat Game 程序|Rat Game.exe",Title="选择游戏目录中的 Rat Game.exe",CheckFileExists=true })
            {
                if(Directory.Exists(gamePath)) dialog.InitialDirectory=gamePath;
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                gamePath=Path.GetDirectoryName(dialog.FileName); pathBox.Text=gamePath;
                try { JsonFile.Write(AppPaths.Preferences,new AssistPreferences { GamePath=gamePath }); }
                catch(Exception e) { Note("目录已选择；偏好保存失败："+e.GetBaseException().Message,true); }
                RefreshStatus();
            }
        }
        private void RefreshStatus()
        {
            browse.Enabled=!busy;
            UpdateCard(markers,"markers","地图标点"); UpdateCard(map,"map","阵营位置");
        }
        private void UpdateCard(InstallCard card,string component,string title)
        {
            card.Install.Enabled=!busy;
            if(preview) { card.Status.Text="离线界面预览"; card.SetRemovalEnabled(false); return; }
            try
            {
                bool installed=Installer.IsInstalled(gamePath,component);
                card.Install.Text=(installed?"更新":"安装")+title+"插件";
                card.Status.Text=installed?"文件已安装 · 下次启动游戏加载":"尚未安装";
                card.Status.ForeColor=installed?Theme.Green:Theme.Muted; card.SetRemovalEnabled(installed&&!busy);
            }
            catch(Exception) { card.Status.Text="请选择有效游戏目录"; card.SetRemovalEnabled(false); }
        }
        private async Task Deploy(string component,bool remove)
        {
            if(preview||busy) return;
            busy=true; RefreshStatus(); Note(remove?"正在卸载插件…":"正在校验并安装插件…",false);
            try
            {
                string path=gamePath;
                string message=await Task.Run(delegate { return remove?Installer.UninstallComponent(path,component):Installer.InstallComponent(path,component); });
                Note(message,false);
            }
            catch(Exception e) { Note(e.GetBaseException().Message,true); MessageBox.Show(this,e.GetBaseException().Message,"插件操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Information); }
            finally { busy=false; RefreshStatus(); }
        }
        private void Note(string message,bool error) { footer.Text=message; footer.ForeColor=error?Color.FromArgb(166,68,48):Theme.Text; }
        internal void RenderPages(string directory)
        {
            if(!preview) throw new InvalidOperationException("Offline preview only");
            Directory.CreateDirectory(directory); ShowInTaskbar=false; Opacity=0; Show(); Application.DoEvents();
            Control root=Controls[0];
            using(Bitmap image=new Bitmap(root.Width,root.Height)) { root.DrawToBitmap(image,root.ClientRectangle); image.Save(Path.Combine(directory,"assist-installer.png")); }
            Close();
        }
    }
}
