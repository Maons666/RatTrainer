using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using RatTrainer;

namespace RatTrainerDesktop
{
    internal static class Theme
    {
        public static float Dpi = 1f;
        public static readonly Color Background = Color.FromArgb(242,241,236), Surface = Color.FromArgb(255,255,252), Sidebar = Color.FromArgb(32,37,36);
        public static readonly Color Text = Color.FromArgb(40,46,43), Muted = Color.FromArgb(113,120,113), Border = Color.FromArgb(223,225,216), Green = Color.FromArgb(50,119,87);
        public static readonly Color Accent = Edition.HostOnly ? Color.FromArgb(177,83,51) : Color.FromArgb(145,103,27);
        public static readonly Color SoftAccent = Edition.HostOnly ? Color.FromArgb(246,231,222) : Color.FromArgb(247,237,207);
        private static readonly Dictionary<string,Font> fonts = new Dictionary<string,Font>();
        public static int S(float v) { return (int)Math.Round(v*Dpi); }
        public static Font Font(float size,bool bold)
        {
            string key=size.ToString(CultureInfo.InvariantCulture)+bold; Font font;
            if(!fonts.TryGetValue(key,out font)) fonts[key]=font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);
            return font;
        }
        public static ActionButton Button(string text) { return new ActionButton { Text=text }; }
        public static Label Label(string text,float size,Color color,bool bold)
        { return new Label { Text=text,Font=Font(size,bold),ForeColor=color,BackColor=Color.Transparent,AutoSize=false,Margin=Padding.Empty }; }
        public static GraphicsPath Round(RectangleF r,float radius)
        {
            GraphicsPath p=new GraphicsPath(); float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            if(d<1) { p.AddRectangle(r); return p; }
            p.AddArc(r.Left,r.Top,d,d,180,90); p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.Left,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        public static void Box(Graphics g,Rectangle r,Color fill,Color border,int radius)
        {
            if(r.Width<2||r.Height<2) return;
            using(GraphicsPath path=Round(new RectangleF(r.X+.5f,r.Y+.5f,r.Width-1,r.Height-1),radius))
            using(Brush b=new SolidBrush(fill)) using(Pen p=new Pen(border)) { g.FillPath(b,path); g.DrawPath(p,path); }
        }
        public static void TextIn(Graphics g,string text,Font font,Color color,Rectangle rect,TextFormatFlags flags)
        { TextRenderer.DrawText(g,text??"",font,rect,color,flags|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding); }
    }

    internal sealed class NumberBox : UserControl
    {
        private readonly TextBox editor;
        private decimal current,min,max=100,step=1;
        public event EventHandler ValueChanged;
        public decimal Minimum { get { return min; } set { min=value; Value=current; } }
        public decimal Maximum { get { return max; } set { max=value; Value=current; } }
        public decimal Increment { get { return step; } set { step=value; UpdateText(); } }
        public decimal Value
        {
            get { return current; }
            set { decimal next=Math.Max(min,Math.Min(max,value)); bool changed=next!=current; current=next; if(!editor.Focused) UpdateText(); if(changed&&ValueChanged!=null) ValueChanged(this,EventArgs.Empty); }
        }
        public NumberBox()
        {
            Size=new Size(Theme.S(136),Theme.S(36)); BackColor=Theme.Surface;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            editor=new TextBox { BorderStyle=BorderStyle.None,TextAlign=HorizontalAlignment.Center,Font=Theme.Font(10f,false),BackColor=Theme.Surface,ForeColor=Theme.Text };
            Controls.Add(editor); editor.Text="0"; editor.Leave+=delegate { Commit(); };
            editor.KeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.KeyCode==Keys.Enter) { Commit(); e.SuppressKeyPress=true; }
                if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down) { Value+=e.KeyCode==Keys.Up?step:-step; UpdateText(); e.SuppressKeyPress=true; }
            };
            AccessibleRole=AccessibleRole.SpinButton;
        }
        private void UpdateText() { if(editor!=null) editor.Text=current.ToString(step<.1m?"0.00":step<1?"0.0":"0",CultureInfo.CurrentCulture); }
        private void Commit() { decimal value; if(Decimal.TryParse(editor.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out value)) Value=value; UpdateText(); }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if(editor!=null) editor.SetBounds(Theme.S(32),(Height-editor.PreferredHeight)/2,Math.Max(Theme.S(24),Width-Theme.S(64)),editor.PreferredHeight); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if(editor!=null) editor.Enabled=Enabled; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if(!Enabled) return;
            if(e.X<Theme.S(32)) { Commit(); Value-=step; UpdateText(); }
            else if(e.X>Width-Theme.S(32)) { Commit(); Value+=step; UpdateText(); } else editor.Focus();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Theme.Box(e.Graphics,ClientRectangle,Theme.Surface,Theme.Border,Theme.S(8)); Color ink=Enabled?Theme.Text:Theme.Muted;
            Theme.TextIn(e.Graphics,"−",Theme.Font(12f,false),ink,new Rectangle(0,0,Theme.S(32),Height),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            Theme.TextIn(e.Graphics,"+",Theme.Font(12f,false),ink,new Rectangle(Width-Theme.S(32),0,Theme.S(32),Height),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class FeatureRow : Panel
    {
        private readonly Func<TrainerOptions> options;
        private readonly Action changed;
        private readonly string flagPath,numberPath,title,hint,unit;
        private readonly Switch toggle;
        private readonly NumberBox number;
        private bool binding,allowed=true,online,isHost;
        public readonly bool NeedsRoomSupport,HostOnly;
        public FeatureRow(Func<TrainerOptions> getOptions,Action onChanged,string label,string description,string flag,string numberField,decimal min,decimal max,decimal step,string suffix,bool roomSupport,bool hostOnly)
        {
            options=getOptions; changed=onChanged; flagPath=flag; numberPath=numberField; title=label; hint=description; unit=suffix; NeedsRoomSupport=roomSupport; HostOnly=hostOnly;
            Dock=DockStyle.Top; Height=Theme.S(84); Margin=new Padding(0,0,0,Theme.S(8)); BackColor=Theme.Surface;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            if(flagPath!=null)
            {
                toggle=new Switch { AccessibleName=label,AccessibleDescription=description };
                toggle.CheckedChanged+=delegate { if(!binding) { Set(flagPath,toggle.Checked); changed(); Invalidate(); } }; Controls.Add(toggle);
            }
            if(numberField!=null)
            {
                number=new NumberBox { Minimum=min,Maximum=max,Increment=step,AccessibleName=label+"数值" };
                number.ValueChanged+=delegate { if(!binding) { Set(numberPath,(float)number.Value); changed(); } }; Controls.Add(number);
            }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if(toggle!=null) toggle.SetBounds(Width-Theme.S(112),Theme.S(24),Theme.S(92),Theme.S(36));
            if(number!=null) number.SetBounds(Width-Theme.S(toggle!=null?280:192),Theme.S(24),Theme.S(136),Theme.S(36));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Theme.Box(e.Graphics,ClientRectangle,Theme.Surface,Theme.Border,Theme.S(12));
            int right=number!=null?(toggle!=null?300:212):236, textWidth=Math.Max(Theme.S(120),Width-Theme.S(right+20));
            Theme.TextIn(e.Graphics,title,Theme.Font(11f,true),Theme.Text,new Rectangle(Theme.S(20),Theme.S(15),textWidth,Theme.S(27)),TextFormatFlags.EndEllipsis);
            Theme.TextIn(e.Graphics,hint,Theme.Font(9f,false),Theme.Muted,new Rectangle(Theme.S(20),Theme.S(46),textWidth,Theme.S(30)),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            if(number!=null) Theme.TextIn(e.Graphics,unit,Theme.Font(9f,false),Theme.Muted,new Rectangle(number.Right+Theme.S(8),Theme.S(24),Theme.S(32),Theme.S(36)),TextFormatFlags.VerticalCenter);
            else
            {
                string scope=!allowed?(Edition.HostOnly||HostOnly?"仅限房主":"等待房主协同"):NeedsRoomSupport?(isHost||Edition.HostOnly?"房主控制":"房主协同"):HostOnly?"房主设置":"本机功能";
                Theme.TextIn(e.Graphics,scope,Theme.Font(8.5f,false),online&&allowed?Theme.Green:Theme.Muted,new Rectangle(Width-Theme.S(228),Theme.S(24),Theme.S(104),Theme.S(36)),TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            }
        }
        private object Owner(string path,out string field) { if(path.StartsWith("World.",StringComparison.Ordinal)) { field=path.Substring(6); return options().World; } field=path; return options(); }
        private object Get(string path) { string field; object owner=Owner(path,out field); return owner.GetType().GetField(field).GetValue(owner); }
        private void Set(string path,object value) { string field; object owner=Owner(path,out field); owner.GetType().GetField(field).SetValue(owner,value); }
        public void RefreshValue(bool connected,DesktopState state,bool busy)
        {
            binding=true;
            try
            {
                online=connected; isHost=connected&&state!=null&&state.Host;
                if(toggle!=null) toggle.Checked=(bool)Get(flagPath); if(number!=null) number.Value=Convert.ToDecimal(Get(numberPath));
                allowed=!busy&&(!HostOnly||isHost);
                if(Edition.HostOnly&&connected&&!isHost) allowed=false;
                if(NeedsRoomSupport&&connected&&(state==null||!state.BridgeAvailable)) allowed=false;
                if(toggle!=null) toggle.Enabled=allowed; if(number!=null) number.Enabled=allowed; Invalidate();
            }
            finally { binding=false; }
        }
    }

    internal enum ArtKind { Rat,Rats,Chef,Robot }
    internal static class Artwork
    {
        private static readonly Dictionary<ArtKind,Image> cache=new Dictionary<ArtKind,Image>();
        public static Image Get(ArtKind kind)
        {
            Image image;
            if(!cache.TryGetValue(kind,out image))
            {
                string name=kind==ArtKind.Robot?"robochef":kind.ToString().ToLowerInvariant();
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("RatTrainer.Art."+name+".png"))
                using(Image source=Image.FromStream(stream)) cache[kind]=image=new Bitmap(source);
            }
            return image;
        }
        public static void Draw(Graphics g,ArtKind kind,Rectangle destination)
        {
            Image image=Get(kind); RectangleF source=kind==ArtKind.Robot?new RectangleF(180,340,440,515):new RectangleF(0,0,image.Width,image.Height);
            if(kind==ArtKind.Robot)
            {
                using(Brush background=new SolidBrush(Color.FromArgb(246,166,56))) g.FillRectangle(background,destination);
                int width=(int)(destination.Height*source.Width/source.Height);
                Rectangle portrait=new Rectangle(destination.X+(destination.Width-width)/2,destination.Y,width,destination.Height);
                g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.DrawImage(image,portrait,source,GraphicsUnit.Pixel); return;
            }
            float ratio=(float)destination.Width/destination.Height;
            if(source.Width/source.Height>ratio) { float w=source.Height*ratio; source.X+=(source.Width-w)/2; source.Width=w; }
            else { float h=source.Width/ratio; source.Y+=(source.Height-h)/2; source.Height=h; }
            g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.DrawImage(image,destination,source,GraphicsUnit.Pixel);
        }
    }
    internal sealed class HeroPanel : Control
    {
        private readonly string title,description,caption;
        private readonly ArtKind art;
        public HeroPanel(string heading,string detail,ArtKind picture)
        {
            title=heading; description=detail; art=picture;
            caption=picture==ArtKind.Robot?"RoboChef / 厨房沙盒":picture==ArtKind.Chef?"厨师 / 追逐时间":"Rat Game / 小鼠行动";
            Height=Theme.S(168); Margin=new Padding(0,0,0,Theme.S(20)); BackColor=Theme.Background;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath clip=Theme.Round(new RectangleF(0,0,Width,Height),Theme.S(16)))
            {
                e.Graphics.SetClip(clip); using(Brush bg=new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(bg,ClientRectangle);
                int artWidth=Theme.S(art==ArtKind.Robot||art==ArtKind.Rat?224:300); Rectangle imageRect=new Rectangle(Width-artWidth,0,artWidth,Height);
                Artwork.Draw(e.Graphics,art,imageRect);
                using(LinearGradientBrush fade=new LinearGradientBrush(new Rectangle(imageRect.X-1,0,Theme.S(48),Height),Theme.Surface,Color.FromArgb(0,Theme.Surface),LinearGradientMode.Horizontal)) e.Graphics.FillRectangle(fade,imageRect.X-1,0,Theme.S(48),Height);
                int textWidth=Math.Max(Theme.S(220),Width-artWidth-Theme.S(28));
                Theme.TextIn(e.Graphics,caption,Theme.Font(8.5f,false),Theme.Accent,new Rectangle(Theme.S(24),Theme.S(22),textWidth,Theme.S(20)),TextFormatFlags.Left);
                Theme.TextIn(e.Graphics,title,Theme.Font(24f,true),Theme.Text,new Rectangle(Theme.S(24),Theme.S(48),textWidth,Theme.S(50)),TextFormatFlags.EndEllipsis);
                Theme.TextIn(e.Graphics,description,Theme.Font(9.5f,false),Theme.Muted,new Rectangle(Theme.S(24),Theme.S(108),textWidth,Theme.S(48)),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
                e.Graphics.ResetClip();
            }
        }
    }

    internal sealed class Page : Panel
    {
        private readonly TableLayoutPanel rows;
        public Page(string title,string description,ArtKind art)
        {
            Dock=DockStyle.Fill; AutoScroll=true; BackColor=Theme.Background; Padding=new Padding(Theme.S(24),Theme.S(24),Theme.S(24),Theme.S(20));
            rows=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=0,BackColor=Theme.Background,Margin=Padding.Empty,Padding=Padding.Empty };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(rows); Add(new HeroPanel(title,description,art));
        }
        public void Add(Control control) { int row=rows.RowCount++; rows.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Dock=DockStyle.Top; rows.Controls.Add(control,0,row); }
        public void Section(string text) { Label label=Theme.Label(text,10f,Theme.Text,true); label.Height=Theme.S(40); label.Padding=new Padding(0,Theme.S(12),0,0); label.Margin=new Padding(0,Theme.S(8),0,Theme.S(4)); Add(label); }
        public Label Info(string text,int height) { Label label=Theme.Label(text,9f,Theme.Muted,false); label.Height=Theme.S(height); label.Padding=new Padding(Theme.S(12),Theme.S(10),Theme.S(12),Theme.S(8)); label.BackColor=Theme.SoftAccent; label.Margin=new Padding(0,0,0,Theme.S(12)); Add(label); return label; }
        public FlowLayoutPanel Actions(params Control[] controls)
        {
            FlowLayoutPanel panel=new FlowLayoutPanel { AutoSize=true,MinimumSize=new Size(0,Theme.S(48)),FlowDirection=FlowDirection.LeftToRight,WrapContents=true,Margin=new Padding(0,Theme.S(4),0,Theme.S(12)),Padding=Padding.Empty };
            foreach(Control control in controls) { control.Margin=new Padding(0,0,Theme.S(12),Theme.S(8)); panel.Controls.Add(control); } Add(panel); return panel;
        }
    }
}
