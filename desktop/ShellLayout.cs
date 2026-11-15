using System;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace RatTrainerDesktop
{
    internal sealed partial class MainForm
    {
        private TableLayoutPanel shellHeader;
        private FlowLayoutPanel shellCommands;
        private void BuildLayout()
        {
            SuspendLayout();
            TableLayoutPanel root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty,Padding=Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Theme.S(216))); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(80))); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(64))); Controls.Add(root);
            TableLayoutPanel sidebar=new TableLayoutPanel { Dock=DockStyle.Fill,BackColor=Theme.Sidebar,ColumnCount=1,RowCount=4,Margin=Padding.Empty,Padding=new Padding(Theme.S(16),0,Theme.S(16),0) };
            sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(164))); sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize)); sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,Theme.S(100)));
            root.Controls.Add(sidebar,0,0); root.SetRowSpan(sidebar,3);
            Panel brandBox=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty,BackColor=Theme.Sidebar };
            Label brand=Theme.Label("Rat Trainer",20f,Color.FromArgb(245,244,234),true); Place(brand,6,30,190,42); brandBox.Controls.Add(brand);
            Label edition=Theme.Label(Edition.Name,10.5f,Color.FromArgb(232,201,135),false); Place(edition,8,80,172,24); brandBox.Controls.Add(edition);
            Label tagline=Theme.Label(Edition.HostOnly?"你的厨房，你来设定。":"给小老鼠，多一点本领。",8.5f,Color.FromArgb(143,155,143),false); Place(tagline,8,110,178,24); brandBox.Controls.Add(tagline); sidebar.Controls.Add(brandBox,0,0);
            FlowLayoutPanel nav=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty,BackColor=Theme.Sidebar };
            sidebar.Controls.Add(nav,0,1);
            for(int i=0;i<pageNames.Length;i++)
            {
                int page=i; ActionButton button=Theme.Button(pageNames[i]); button.Navigation=true; button.Index=(i+1).ToString("00");
                button.Size=new Size(Theme.S(184),Theme.S(44)); button.Margin=new Padding(0,0,0,Theme.S(6)); button.Click+=delegate { SelectPage(page); }; nav.Controls.Add(button); navigation.Add(button);
            }
            Label signature=Theme.Label("F10   全部关闭\n\nv"+Edition.Version+"  ·  本地桌面程序",8.5f,Color.FromArgb(154,165,153),false);
            signature.Dock=DockStyle.Fill; signature.Padding=new Padding(Theme.S(8),Theme.S(4),0,0); sidebar.Controls.Add(signature,0,3);
            shellHeader=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=Theme.Surface,Padding=new Padding(Theme.S(24),0,Theme.S(16),0),Margin=Padding.Empty };
            shellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); shellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); shellHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); shellHeader.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.Controls.Add(shellHeader,1,0);
            TableLayoutPanel connectionBox=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=new Padding(0,Theme.S(16),Theme.S(12),Theme.S(12)),BackColor=Theme.Surface };
            connectionBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); connectionBox.RowStyles.Add(new RowStyle(SizeType.Percent,50)); connectionBox.RowStyles.Add(new RowStyle(SizeType.Percent,50)); shellHeader.Controls.Add(connectionBox,0,0);
            connection=Theme.Label("● 等待游戏连接",10f,Theme.Muted,true); connection.Dock=DockStyle.Fill; connection.AutoEllipsis=true; connection.TextAlign=ContentAlignment.MiddleLeft; connectionBox.Controls.Add(connection,0,0);
            connectionHint=Theme.Label(Edition.HostOnly?"创建房间后，解锁沙盒控制":"本机功能 · 房主协同",8.5f,Theme.Muted,false); connectionHint.Dock=DockStyle.Fill; connectionHint.AutoEllipsis=true; connectionHint.TextAlign=ContentAlignment.MiddleLeft; connectionBox.Controls.Add(connectionHint,0,1);
            shellCommands=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,Anchor=AnchorStyles.Right,Margin=Padding.Empty,BackColor=Theme.Surface };
            shellHeader.Controls.Add(shellCommands,1,0);
            install=Theme.Button("安装 / 更新组件"); install.Width=Theme.S(136); install.Click+=async delegate { await Deploy(false); };
            launch=Theme.Button("启动游戏"); launch.Width=Theme.S(96); launch.Primary=true;
            launch.Click+=delegate { if(!preview) Run(delegate { Process.Start(new ProcessStartInfo { FileName="steam://rungameid/3347910",UseShellExecute=true }); Note("已请求 Steam 启动游戏，等待连接。",false); }); };
            Label masterLabel=Theme.Label("总开关",9f,Theme.Muted,false); masterLabel.Size=new Size(Theme.S(44),Theme.S(40)); masterLabel.TextAlign=ContentAlignment.MiddleLeft;
            master=new Switch { AccessibleName="修改器总开关" }; master.CheckedChanged+=delegate { if(!syncing) { model.Enabled=master.Checked; Changed(); } };
            off=Theme.Button("全部关闭 · F10"); off.Width=Theme.S(136); off.ForeColor=Color.FromArgb(151,66,48); off.Click+=delegate { TurnOff(); };
            foreach(Control control in new Control[] { install,launch,masterLabel,master,off }) { control.Margin=new Padding(Theme.S(8),control==master?Theme.S(2):0,0,0); shellCommands.Controls.Add(control); }
            BuildWindowControls();
            foreach(Control drag in new Control[] { shellHeader,connectionBox,connection,connectionHint,brandBox,brand,edition,tagline }) BindWindowDrag(drag);
            content=new Panel { Dock=DockStyle.Fill,BackColor=Theme.Background,Margin=Padding.Empty }; root.Controls.Add(content,1,1);
            TableLayoutPanel status=new TableLayoutPanel { Dock=DockStyle.Fill,BackColor=Theme.Surface,ColumnCount=1,RowCount=2,Padding=new Padding(Theme.S(24),Theme.S(8),Theme.S(24),Theme.S(4)),Margin=Padding.Empty };
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); status.RowStyles.Add(new RowStyle(SizeType.Percent,50)); status.RowStyles.Add(new RowStyle(SizeType.Percent,50)); root.Controls.Add(status,1,2);
            footer=Theme.Label("等待连接",9f,Theme.Text,false); footer.Dock=DockStyle.Fill; footer.AutoEllipsis=true; footer.TextAlign=ContentAlignment.MiddleLeft; status.Controls.Add(footer,0,0);
            live=Theme.Label("生命 —    饱食 —    体力 —",8.5f,Theme.Muted,false); live.Dock=DockStyle.Fill; live.AutoEllipsis=true; live.TextAlign=ContentAlignment.MiddleLeft; status.Controls.Add(live,0,1);
            ResumeLayout(true);
        }
    }
}
