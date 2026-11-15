using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly:AssemblyTitle("Rat Assist Installer")]
[assembly:AssemblyDescription("Rat Game 地图标点与阵营实时地图独立安装器")]
[assembly:AssemblyCompany("MaOnStEr969")]
[assembly:AssemblyProduct("Rat Assist · 辅助插件安装器")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]

namespace RatTrainerDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(Graphics graphics=Graphics.FromHwnd(IntPtr.Zero)) Theme.Dpi=graphics.DpiX/96f;
            if(args.Length==2&&args[0]=="--render")
            {
                string directory=Path.GetFullPath(args[1]); Directory.CreateDirectory(directory);
                try { using(MainForm form=new MainForm(true)) form.RenderPages(directory); }
                catch(Exception e) { File.WriteAllText(Path.Combine(directory,"assist-installer-render-error.txt"),e.ToString()); Environment.ExitCode=1; }
                return;
            }
            if(args.Length==1&&args[0]=="--preview") { Application.Run(new MainForm(true)); return; }
            bool created;
            // The existing trainer uses the same mutex. Shared installation records must have only one writer.
            using(Mutex mutex=new Mutex(true,"Local\\RatTrainer.Desktop."+Environment.UserName,out created))
            {
                if(!created) { MessageBox.Show("已有 Rat Trainer 或辅助安装器在运行，请先关闭另一个窗口。",Edition.Name); return; }
                Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs error) { MessageBox.Show(error.Exception.GetBaseException().Message,Edition.Name,MessageBoxButtons.OK,MessageBoxIcon.Error); };
                try { Application.Run(new MainForm(false)); }
                catch(Exception e) { MessageBox.Show(e.GetBaseException().Message,Edition.Name,MessageBoxButtons.OK,MessageBoxIcon.Error); }
            }
        }
    }
}
