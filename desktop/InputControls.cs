using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RatTrainerDesktop
{
    // Pure managed controls: no native Button/CheckBox renderer can repaint the corners.
    internal sealed class ActionButton : Control, IButtonControl
    {
        public bool Primary, Selected, Navigation;
        public string Index;
        public WindowGlyph Glyph;
        private bool hover, down;
        public DialogResult DialogResult { get; set; }
        public ActionButton()
        {
            Font=Theme.Font(9.5f,false); Size=new Size(Theme.S(144),Theme.S(40)); Cursor=Cursors.Hand;
            BackColor=Theme.Surface; ForeColor=Theme.Text; TabStop=true; AccessibleRole=AccessibleRole.PushButton;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            SetStyle(ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,false);
        }
        public void NotifyDefault(bool value) { Invalidate(); }
        public void PerformClick() { if(Enabled) OnClick(EventArgs.Empty); }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left&&Enabled) { Focus(); down=true; Capture=true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        { bool click=down&&e.Button==MouseButtons.Left&&ClientRectangle.Contains(e.Location); down=false; Capture=false; Invalidate(); if(click) PerformClick(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if(!Capture) down=false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter) { down=true; e.SuppressKeyPress=true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        { if((e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)&&down) { down=false; PerformClick(); e.Handled=true; Invalidate(); } base.OnKeyUp(e); }
        protected override bool IsInputKey(Keys key) { return key==Keys.Space||key==Keys.Enter||base.IsInputKey(key); }
        protected override void OnEnabledChanged(EventArgs e) { down=false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { down=false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?Theme.Background:Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            if(Glyph!=WindowGlyph.None) { PaintWindowGlyph(e.Graphics); return; }
            Color fill=Navigation?(Selected?Color.FromArgb(59,66,61):hover?Color.FromArgb(43,49,46):Theme.Sidebar)
                :!Enabled?Color.FromArgb(233,235,228):Primary?(down?ControlPaint.Dark(Theme.Accent,.1f):hover?ControlPaint.Light(Theme.Accent,.1f):Theme.Accent):Selected?Theme.SoftAccent:down?Color.FromArgb(226,228,218):hover?Color.FromArgb(246,247,240):BackColor;
            Color ink=Navigation?(Selected?Color.FromArgb(250,226,163):Color.FromArgb(183,192,182)):!Enabled?Color.FromArgb(145,151,141):Primary?Color.White:ForeColor;
            Theme.Box(e.Graphics,ClientRectangle,fill,Focused&&ShowFocusCues?Theme.Accent:Navigation||Primary?fill:Theme.Border,Theme.S(8));
            Rectangle text=new Rectangle(Theme.S(12),0,Width-Theme.S(24),Height);
            if(Navigation)
            { Theme.TextIn(e.Graphics,Index,Theme.Font(8.5f,false),Color.FromArgb(125,139,126),new Rectangle(Theme.S(14),0,Theme.S(25),Height),TextFormatFlags.VerticalCenter); text.X=Theme.S(46); text.Width=Width-Theme.S(56); }
            Theme.TextIn(e.Graphics,Text,Font,ink,text,TextFormatFlags.VerticalCenter|(Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.EndEllipsis);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ButtonAccessible(this); }
        private void PaintWindowGlyph(Graphics graphics)
        {
            Color fill=Parent==null?Theme.Surface:Parent.BackColor;
            Color ink=Enabled?Theme.Muted:Theme.Border;
            if(hover||down) { fill=Glyph==WindowGlyph.Close?Color.FromArgb(174,66,48):down?Theme.Border:Theme.Background; ink=Glyph==WindowGlyph.Close?Color.White:Theme.Text; }
            Theme.Box(graphics,ClientRectangle,fill,Focused&&ShowFocusCues?Theme.Accent:fill,Theme.S(6));
            float size=Theme.S(10),x=(Width-size)/2f,y=(Height-size)/2f;
            using(Pen pen=new Pen(ink,Math.Max(1.2f,Theme.Dpi)))
            {
                if(Glyph==WindowGlyph.Minimize) graphics.DrawLine(pen,x,y+size/2,x+size,y+size/2);
                else if(Glyph==WindowGlyph.Close) { graphics.DrawLine(pen,x,y,x+size,y+size); graphics.DrawLine(pen,x+size,y,x,y+size); }
                else if(Glyph==WindowGlyph.Maximize) graphics.DrawRectangle(pen,x,y,size,size);
                else { graphics.DrawRectangle(pen,x+2*Theme.Dpi,y,8*Theme.Dpi,8*Theme.Dpi); using(Brush brush=new SolidBrush(fill)) graphics.FillRectangle(brush,x,y+2*Theme.Dpi,8*Theme.Dpi,8*Theme.Dpi); graphics.DrawRectangle(pen,x,y+2*Theme.Dpi,8*Theme.Dpi,8*Theme.Dpi); }
            }
        }
        private sealed class ButtonAccessible : ControlAccessibleObject
        {
            private readonly ActionButton owner;
            public ButtonAccessible(ActionButton value) : base(value) { owner=value; }
            public override string DefaultAction { get { return "按下"; } }
            public override void DoDefaultAction() { owner.PerformClick(); }
        }
    }

    internal sealed class Switch : Control
    {
        private bool value, pressed, keyboardDown;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return value; }
            set { if(this.value==value) return; this.value=value; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange,-1); if(CheckedChanged!=null) CheckedChanged(this,EventArgs.Empty); }
        }
        public Switch()
        {
            Size=new Size(Theme.S(92),Theme.S(36)); Cursor=Cursors.Hand; BackColor=Theme.Surface; TabStop=true; AccessibleRole=AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
            SetStyle(ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,false);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if(e.Button==MouseButtons.Left&&Enabled) { Focus(); pressed=true; Capture=true; } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        { bool click=Enabled&&pressed&&e.Button==MouseButtons.Left&&ClientRectangle.Contains(e.Location); pressed=false; Capture=false; if(click) Checked=e.X>=Width/2; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if(!Capture) pressed=false; base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter) { keyboardDown=true; e.SuppressKeyPress=true; } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if((e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)&&keyboardDown) { keyboardDown=false; Checked=!Checked; e.Handled=true; } base.OnKeyUp(e); }
        protected override bool IsInputKey(Keys key) { return key==Keys.Space||key==Keys.Enter||base.IsInputKey(key); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyboardDown=false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { pressed=keyboardDown=false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?Theme.Surface:Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Theme.Box(e.Graphics,ClientRectangle,Color.FromArgb(238,240,233),Focused&&ShowFocusCues?Theme.Accent:Theme.Border,Theme.S(8));
            int gap=Theme.S(3),half=Width/2; Color fill=!Enabled?Color.FromArgb(217,220,210):Checked?Theme.Accent:Theme.Surface;
            Theme.Box(e.Graphics,new Rectangle(Checked?half:gap,gap,half-gap,Height-gap*2),fill,!Enabled||Checked?fill:Theme.Border,Theme.S(6));
            Theme.TextIn(e.Graphics,"关",Theme.Font(9f,!Checked),!Checked&&Enabled?Theme.Text:Theme.Muted,new Rectangle(0,0,half,Height),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            Theme.TextIn(e.Graphics,"开",Theme.Font(9f,Checked),Checked&&Enabled?Color.White:Theme.Muted,new Rectangle(half,0,Width-half,Height),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SwitchAccessible(this); }
        private sealed class SwitchAccessible : ControlAccessibleObject
        {
            private readonly Switch owner;
            public SwitchAccessible(Switch value) : base(value) { owner=value; }
            public override string DefaultAction { get { return "切换"; } }
            public override AccessibleStates State { get { return base.State|(owner.Checked?AccessibleStates.Checked:AccessibleStates.None); } }
            public override void DoDefaultAction() { if(owner.Enabled) owner.Checked=!owner.Checked; }
        }
    }
}
