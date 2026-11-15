using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Loads the released desktop assembly in its existing offline preview mode.
// This exercises the opaque, native top-level window without reading game state.
internal static class ChromePreview
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string path=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-assembly.txt")).Trim();
        Assembly assembly=Assembly.LoadFile(path);
        using(Graphics g=Graphics.FromHwnd(IntPtr.Zero)) assembly.GetType("RatTrainerDesktop.Theme").GetField("Dpi").SetValue(null,g.DpiX/96f);
        using(Form form=(Form)Activator.CreateInstance(assembly.GetType("RatTrainerDesktop.MainForm"),new object[] { true }))
        {
            form.Text="Rat Trainer - Offline Chrome Check";
            Application.Run(form);
        }
    }
}
