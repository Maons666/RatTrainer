using System;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Drawing;
using System.IO;

[assembly: AssemblyTitle("Rat Trainer")]
[assembly: AssemblyDescription("Rat Game standalone desktop trainer")]
[assembly: AssemblyCompany("Local Tools")]
[assembly: AssemblyProduct("Rat Trainer · " + RatTrainerDesktop.Edition.Name)]
[assembly: AssemblyVersion("1.3.4.0")]
[assembly: AssemblyFileVersion("1.3.4.0")]

namespace RatTrainerDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) Theme.Dpi = graphics.DpiX / 96f;
            if (args.Length == 2 && args[0] == "--render")
            {
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                try { using (MainForm form = new MainForm(true)) form.RenderPages(output); }
                catch (Exception error) { File.WriteAllText(Path.Combine(output, Edition.Key + "-render-error.txt"), error.ToString()); Environment.ExitCode = 1; }
                return;
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\RatTrainer.Desktop." + Environment.UserName, out created))
            {
                if (!created) { MessageBox.Show("已有 Rat Trainer 在运行。切换版本前请先关闭另一个修改器窗口。", "Rat Trainer"); return; }
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs threadError)
                { MessageBox.Show(threadError.Exception.GetBaseException().Message, "Rat Trainer", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                try { Application.Run(new MainForm(false)); }
                catch (Exception e) { MessageBox.Show(e.GetBaseException().Message, "Rat Trainer 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
