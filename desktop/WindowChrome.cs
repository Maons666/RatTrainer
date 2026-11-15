using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RatTrainerDesktop
{
    internal enum WindowGlyph { None, Minimize, Maximize, Restore, Close }

    internal sealed partial class MainForm
    {
        private ActionButton minimizeWindow,maximizeWindow,closeWindow;
        private FlowLayoutPanel windowControls;
        private readonly ToolTip windowTips=new ToolTip { InitialDelay=500,ReshowDelay=100,AutoPopDelay=4000 };
        private Rectangle restoreWindowBounds;
        private const int WmNcCalcSize=0x83,WmNcHitTest=0x84,WmNcPaint=0x85,WmNcActivate=0x86,WmGetMinMaxInfo=0x24;
        private const int HtCaption=2,HtLeft=10,HtRight=11,HtTop=12,HtTopLeft=13,HtTopRight=14,HtBottom=15,HtBottomLeft=16,HtBottomRight=17;

        protected override void SetClientSizeCore(int width,int height)
        {
            SetBoundsCore(Left,Top,width,height,BoundsSpecified.Size);
            UpdateBounds(Left,Top,Width,Height,Width,Height);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters=base.CreateParams;
                // Keep native sizing, snap, and taskbar minimize while drawing the whole client ourselves.
                parameters.Style|=0x40000|0x20000|0x10000;
                return parameters;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DisableNativeFrameRendering();
        }
        private void DisableNativeFrameRendering()
        {
            // Keep WS_THICKFRAME for native sizing, but never let DWM paint a frame over our client area.
            // https://learn.microsoft.com/windows/win32/dwm/composition-ovw
            try { int disabled=1; DwmSetWindowAttribute(Handle,2,ref disabled,sizeof(int)); }
            catch(DllNotFoundException) { }
            catch(EntryPointNotFoundException) { }
        }
        private void BuildWindowControls()
        {
            windowControls=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,Anchor=AnchorStyles.Right,Margin=new Padding(Theme.S(12),0,0,0),BackColor=Theme.Surface };
            minimizeWindow=WindowButton(WindowGlyph.Minimize,"最小化"); maximizeWindow=WindowButton(WindowGlyph.Maximize,"最大化"); closeWindow=WindowButton(WindowGlyph.Close,"关闭窗口");
            minimizeWindow.Click+=delegate { SendMessage(Handle,0x112,new IntPtr(0xF020),IntPtr.Zero); };
            maximizeWindow.Click+=delegate { ToggleMaximized(); };
            closeWindow.Click+=delegate { Close(); };
            windowControls.Controls.AddRange(new Control[] { minimizeWindow,maximizeWindow,closeWindow });
            shellHeader.Controls.Add(windowControls,2,0);
            Disposed+=delegate { windowTips.Dispose(); };
        }
        private ActionButton WindowButton(WindowGlyph glyph,string name)
        {
            ActionButton button=Theme.Button(""); button.Glyph=glyph; button.AccessibleName=name; button.AccessibleDescription=name;
            button.Size=new Size(Theme.S(32),Theme.S(36)); button.Margin=new Padding(Theme.S(2),0,0,0); windowTips.SetToolTip(button,name); return button;
        }
        private void ToggleMaximized()
        { SendMessage(Handle,0x112,new IntPtr(WindowState==FormWindowState.Maximized?0xF120:0xF030),IntPtr.Zero); }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if(maximizeWindow==null) return;
            bool expanded=WindowState==FormWindowState.Maximized;
            maximizeWindow.Glyph=expanded?WindowGlyph.Restore:WindowGlyph.Maximize;
            maximizeWindow.AccessibleName=expanded?"还原窗口":"最大化"; maximizeWindow.AccessibleDescription=maximizeWindow.AccessibleName;
            windowTips.SetToolTip(maximizeWindow,maximizeWindow.AccessibleName); maximizeWindow.Invalidate();
        }
        private void BindWindowDrag(Control control)
        {
            windowTips.SetToolTip(control,"拖动以移动窗口；双击最大化或还原");
            control.MouseDown+=delegate(object sender,MouseEventArgs e)
            {
                if(e.Button!=MouseButtons.Left) return;
                if(e.Clicks>=2) { ToggleMaximized(); return; }
                ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(HtCaption),IntPtr.Zero);
            };
        }
        private void RouteResizeEdges(Control parent)
        {
            foreach(Control child in parent.Controls)
            {
                new ResizeRelay(this,child);
                RouteResizeEdges(child);
            }
            parent.ControlAdded+=delegate(object sender,ControlEventArgs e) { new ResizeRelay(this,e.Control); RouteResizeEdges(e.Control); };
        }
        // Children fill the frameless window. Let edge hit tests reach the owning form.
        private sealed class ResizeRelay : NativeWindow
        {
            private readonly MainForm form;
            public ResizeRelay(MainForm owner,Control control)
            {
                form=owner;
                control.HandleCreated+=delegate { AssignHandle(control.Handle); };
                control.HandleDestroyed+=delegate { if(Handle!=IntPtr.Zero) ReleaseHandle(); };
                if(control.IsHandleCreated) AssignHandle(control.Handle);
            }
            protected override void WndProc(ref Message message)
            {
                if(message.Msg==WmNcHitTest)
                {
                    long position=message.LParam.ToInt64();
                    Point point=form.PointToClient(new Point(unchecked((short)(position&0xffff)),unchecked((short)((position>>16)&0xffff))));
                    if(form.WindowHitTest(point)!=1) { message.Result=new IntPtr(-1); return; }
                }
                base.WndProc(ref message);
            }
        }
        internal int WindowHitTest(Point clientPoint)
        {
            if(WindowState==FormWindowState.Normal)
            {
                int grip=Theme.S(6);
                bool left=clientPoint.X<grip,right=clientPoint.X>=ClientSize.Width-grip,top=clientPoint.Y<grip,bottom=clientPoint.Y>=ClientSize.Height-grip;
                if(top&&left) return HtTopLeft; if(top&&right) return HtTopRight;
                if(bottom&&left) return HtBottomLeft; if(bottom&&right) return HtBottomRight;
                if(left) return HtLeft; if(right) return HtRight; if(top) return HtTop; if(bottom) return HtBottom;
            }
            return 1;
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x112)
            {
                int action=message.WParam.ToInt32()&0xFFF0;
                if((action==0xF030||action==0xF020)&&WindowState==FormWindowState.Normal) restoreWindowBounds=Bounds;
                Rectangle restore=restoreWindowBounds;
                base.WndProc(ref message);
                if(action==0xF120&&WindowState==FormWindowState.Normal&&restore.Width>0) Bounds=restore;
                return;
            }
            if(message.Msg==WmNcCalcSize) { message.Result=IntPtr.Zero; return; }
            if(message.Msg==WmNcPaint&&WindowState!=FormWindowState.Minimized)
            { message.Result=IntPtr.Zero; return; }
            if(message.Msg==WmNcActivate&&WindowState!=FormWindowState.Minimized)
            {
                // Update activation normally, without DefWindowProc repainting the system border.
                // https://learn.microsoft.com/windows/win32/winmsg/wm-ncactivate
                message.LParam=new IntPtr(-1); base.WndProc(ref message); return;
            }
            if(message.Msg==0x31A||message.Msg==0x31E)
            {
                base.WndProc(ref message); DisableNativeFrameRendering(); return;
            }
            if(message.Msg==WmNcHitTest)
            {
                long position=message.LParam.ToInt64();
                Point point=PointToClient(new Point(unchecked((short)(position&0xffff)),unchecked((short)((position>>16)&0xffff))));
                message.Result=new IntPtr(WindowHitTest(point)); return;
            }
            if(message.Msg==WmGetMinMaxInfo)
            {
                base.WndProc(ref message);
                MonitorInfo monitor=new MonitorInfo(); monitor.Size=Marshal.SizeOf(typeof(MonitorInfo));
                if(GetMonitorInfo(MonitorFromWindow(Handle,2),ref monitor))
                {
                    MinMaxInfo limits=(MinMaxInfo)Marshal.PtrToStructure(message.LParam,typeof(MinMaxInfo));
                    limits.MaxPosition=new Point(monitor.Work.Left-monitor.Monitor.Left,monitor.Work.Top-monitor.Monitor.Top);
                    limits.MaxSize=new Point(monitor.Work.Right-monitor.Work.Left,monitor.Work.Bottom-monitor.Work.Top);
                    limits.MinTrackSize=new Point(MinimumSize.Width,MinimumSize.Height);
                    Marshal.StructureToPtr(limits,message.LParam,false);
                }
                message.Result=IntPtr.Zero; return;
            }
            base.WndProc(ref message);
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor,Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize; }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    }
}
