using System;
using System.Threading;
using System.Windows.Forms;

namespace PortPeek
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool startInTray = Array.Exists(args, arg => String.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
            bool first;
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\WindowsToolkit.PortPeek.Show"))
            using (var mutex = new Mutex(true, "Local\\WindowsToolkit.PortPeek", out first))
            {
                if (!first) { if (!startInTray) show.Set(); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainWindow(show, startInTray));
            }
        }
    }
}
