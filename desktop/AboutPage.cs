using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RatTrainerDesktop
{
    internal static class AuthorProfile
    {
        public const string Name="MaOnStEr969";
        public const string Bio="";
        public const string Contact="";
    }
    internal sealed class AuthorCard : Panel
    {
        private readonly Label heading,author,bio,contact;
        private bool layingOut;
        public AuthorCard()
        {
            Height=Theme.S(160); BackColor=Theme.Surface; Margin=new Padding(0,0,0,Theme.S(20));
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            heading=Theme.Label("作者",9f,Theme.Accent,false);
            author=Theme.Label(String.IsNullOrWhiteSpace(AuthorProfile.Name)?"作者信息待填写":AuthorProfile.Name,22f,Theme.Text,true);
            bio=Theme.Label(AuthorProfile.Bio,10f,Theme.Muted,false); contact=Theme.Label(AuthorProfile.Contact,10f,Theme.Text,false);
            Controls.AddRange(new Control[] {heading,author,bio,contact});
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if(layingOut||heading==null) return; layingOut=true;
            try
            {
                int width=Math.Max(Theme.S(160),Width-Theme.S(48)),y=Theme.S(24);
                heading.SetBounds(Theme.S(24),y,width,Theme.S(24)); y+=Theme.S(32);
                foreach(Label label in new[] {author,bio,contact})
                {
                    bool hasText=!String.IsNullOrWhiteSpace(label.Text); label.Visible=hasText; if(!hasText) continue;
                    int height=TextRenderer.MeasureText(label.Text,label.Font,new Size(width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+Theme.S(4);
                    label.SetBounds(Theme.S(24),y,width,height); y+=height+Theme.S(16);
                }
                Height=y+Theme.S(8);
            }
            finally { layingOut=false; }
        }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias; Theme.Box(e.Graphics,ClientRectangle,Theme.Surface,Theme.Border,Theme.S(12)); }
    }
    internal sealed partial class MainForm
    {
        private void BuildAboutPage()
        {
            Page page=NewPage("关于 Rat Trainer","让厨房多一点自己的玩法。",Edition.HostOnly?ArtKind.Robot:ArtKind.Rat);
            page.Add(new AuthorCard());
            page.Section("软件信息");
            page.Info("Rat Trainer · "+Edition.Name+"\nv"+Edition.Version+"  ·  Windows x64",76);
            page.Info("拖动顶部空白区域或左上角名称可以移动窗口；双击可最大化或还原。右上角提供最小化、最大化与关闭。",64);
            page.Info("页面中的老鼠与机械厨师美术来自 Rat Game。",48);
        }
    }
}
