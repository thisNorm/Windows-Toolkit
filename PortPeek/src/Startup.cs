using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PortPeek
{
    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\" --tray"; } }

        internal static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key))
                return key != null && String.Equals(key.GetValue("PortPeek") as string, Command, StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enabled) key.SetValue("PortPeek", Command);
                else key.DeleteValue("PortPeek", false);
            }
        }
    }
}
