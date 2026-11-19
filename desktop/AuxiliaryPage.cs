using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RatTrainerDesktop
{
    internal sealed class AuxiliaryCard : Panel
    {
        public readonly ActionButton Install;
        public readonly LinkLabel Remove;
        public readonly Label Status;
        private readonly Label heading,description,note;
        public AuxiliaryCard(string title,string text,string help,string action)
        {
            Height=Theme.S(176); BackColor=Theme.Surface; Margin=new Padding(0,0,0,Theme.S(16));
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            heading=Theme.Label(title,12f,Theme.Text,true); description=Theme.Label(text,9.5f,Theme.Muted,false); note=Theme.Label(help,9f,Theme.Accent,false);
            Install=Theme.Button(action); Install.Primary=true; Status=Theme.Label("尚未读取安装状态",8.5f,Theme.Muted,false); Status.TextAlign=ContentAlignment.MiddleCenter;
            Remove=new LinkLabel { Text="卸载此插件",Font=Theme.Font(9f,false),TextAlign=ContentAlignment.MiddleCenter,LinkColor=Theme.Muted,ActiveLinkColor=Theme.Accent,VisitedLinkColor=Theme.Muted,DisabledLinkColor=Color.FromArgb(154,160,150),BackColor=Theme.Surface,LinkBehavior=LinkBehavior.HoverUnderline,Margin=Padding.Empty };
            Remove.Links.Clear(); Remove.Links.Add(0,Remove.Text.Length); SetRemovalEnabled(false);
            Controls.AddRange(new Control[] { heading,description,note,Install,Status,Remove });
        }
        public void SetRemovalEnabled(bool enabled)
        { Remove.Links[0].Enabled=enabled; Remove.TabStop=enabled; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if(heading==null) return;
            int textWidth=Math.Max(Theme.S(180),Width-Theme.S(304));
            heading.SetBounds(Theme.S(20),Theme.S(18),textWidth,Theme.S(30)); description.SetBounds(Theme.S(20),Theme.S(58),textWidth,Theme.S(64));
            note.SetBounds(Theme.S(20),Theme.S(138),Width-Theme.S(40),Theme.S(26));
            Install.SetBounds(Width-Theme.S(260),Theme.S(24),Theme.S(240),Theme.S(40)); Status.SetBounds(Width-Theme.S(260),Theme.S(72),Theme.S(240),Theme.S(28));
            Remove.SetBounds(Width-Theme.S(260),Theme.S(104),Theme.S(240),Theme.S(28));
        }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; Theme.Box(e.Graphics,ClientRectangle,Theme.Surface,Theme.Border,Theme.S(12)); }
    }
    internal sealed partial class MainForm
    {
        private AuxiliaryCard markerInstall,mapInstall;
        private DateTime assistStatusAt;
        private void BuildAuxiliaryPage()
        {
            Page page=NewPage("装好，就能用。","两个独立的本机辅助插件。安装前关闭游戏，下次启动自动启用，无需保持 Trainer 开着。",ArtKind.Rat);
            markerInstall=new AuxiliaryCard("地图标点","在准心命中的实体表面放置标点，可透过障碍看到。\n小鼠只显示小鼠阵营标点；厨师只显示自己。","默认 G 标点 · Shift + G 清除自己的标点 · 每个点保留 30 秒","安装地图标点插件");
            mapInstall=new AuxiliaryCard("阵营实时地图","在 Tab 地图上显示头像、玩家名称和水平位置。\n小鼠看小鼠阵营，厨师只看自己；不显示高度。","本机安装即可显示已同步的队友位置 · 厨师也可按 Tab 查看自己的地图","安装阵营位置插件");
            page.Add(markerInstall); page.Add(mapInstall);
            markerInstall.Install.Click+=async delegate { await DeployAuxiliary("markers",false); };
            mapInstall.Install.Click+=async delegate { await DeployAuxiliary("map",false); };
            markerInstall.Remove.LinkClicked+=async delegate { await DeployAuxiliary("markers",true); };
            mapInstall.Remove.LinkClicked+=async delegate { await DeployAuxiliary("map",true); };
            page.Info("共享标点请让房主和参与队友都更新标点插件；房主无需开启 Trainer 或客机协同。游戏内 Ctrl+G 查看同步状态。",64);
            page.Info("需要移除时，先关闭游戏，再点击对应卡片的“卸载此插件”。只移除选中的插件；另一项辅助功能和 Trainer 会保留。Trainer 总开关和 F10 不影响辅助插件。",64);
            RefreshAuxiliaryStatus();
        }
        private void RefreshAuxiliaryStatus()
        {
            if(markerInstall==null||mapInstall==null) return;
            markerInstall.Install.Enabled=mapInstall.Install.Enabled=!busy;
            if(busy||preview)
            {
                markerInstall.SetRemovalEnabled(false); mapInstall.SetRemovalEnabled(false); assistStatusAt=DateTime.MinValue;
                if(preview) markerInstall.Status.Text=mapInstall.Status.Text="离线界面预览 · 未读取安装状态";
                return;
            }
            if(DateTime.UtcNow<assistStatusAt) return;
            assistStatusAt=DateTime.UtcNow.AddSeconds(2);
            UpdateAuxiliaryStatus(markerInstall,"markers","地图标点"); UpdateAuxiliaryStatus(mapInstall,"map","阵营位置");
        }
        private void UpdateAuxiliaryStatus(AuxiliaryCard card,string component,string title)
        {
            try
            {
                bool exists=Installer.IsInstalled(link.GamePath,component);
                card.Install.Text=exists?"更新"+title+"插件":"安装"+title+"插件";
                card.Status.Text=exists?"文件已安装 · 下次启动游戏加载":"尚未安装";
                card.Status.ForeColor=exists?Theme.Green:Theme.Muted;
                card.SetRemovalEnabled(exists&&!busy&&!preview);
            }
            catch(Exception e) { card.Status.Text=e.GetBaseException().Message; card.SetRemovalEnabled(false); }
        }
        private async Task DeployAuxiliary(string component,bool remove)
        {
            if(busy||preview) return;
            busy=true; install.Enabled=launch.Enabled=false; RefreshBindings(); RefreshAuxiliaryStatus();
            try
            {
                Note(remove?"正在卸载辅助插件…":"正在校验并安装辅助插件…",false);
                string path=link.GamePath;
                string result=await Task.Run(delegate { return remove?Installer.UninstallComponent(path,component):Installer.InstallComponent(path,component); });
                Note(result,false);
            }
            catch(Exception e) { Note(e.GetBaseException().Message,true); MessageBox.Show(this,e.GetBaseException().Message,"辅助插件操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Information); }
            finally { busy=false; install.Enabled=launch.Enabled=true; assistStatusAt=DateTime.MinValue; RefreshBindings(); RefreshAuxiliaryStatus(); }
        }
    }
}
