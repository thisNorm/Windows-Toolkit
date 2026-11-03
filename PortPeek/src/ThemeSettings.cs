using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace PortPeek
{
    internal enum ThemeMode { System, Light, Dark }

    internal sealed class ThemeSettings
    {
        private readonly string fileName;
        private Exception readError;

        internal event EventHandler Changed;
        internal ThemeMode Mode { get; private set; }
        internal bool IsDark
        {
            get { return ResolveDark(Mode, Mode != ThemeMode.System || ReadSystemUsesLightTheme()); }
        }

        private ThemeSettings(string fileName)
        {
            this.fileName = fileName;
            Mode = ThemeMode.System;
        }

        internal static ThemeSettings Load()
        {
            return Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsToolkit", "PortPeek", "theme.txt"));
        }

        internal static ThemeSettings Load(string fileName)
        {
            ThemeSettings settings = new ThemeSettings(Path.GetFullPath(fileName));
            try
            {
                switch (File.ReadAllText(settings.fileName).Trim())
                {
                    case "Light": settings.Mode = ThemeMode.Light; break;
                    case "Dark": settings.Mode = ThemeMode.Dark; break;
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException ex) { settings.readError = ex; }
            catch (UnauthorizedAccessException ex) { settings.readError = ex; }
            catch (SecurityException ex) { settings.readError = ex; }
            return settings;
        }

        internal void SetMode(ThemeMode mode)
        {
            if (mode != ThemeMode.System && mode != ThemeMode.Light && mode != ThemeMode.Dark)
                throw new ArgumentOutOfRangeException("mode");
            if (mode == Mode) return;
            // Preserve an unreadable preference until the caller successfully reloads it.
            if (readError != null)
                throw new IOException("Theme settings could not be read; reload before saving.", readError);

            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            File.WriteAllText(fileName, mode.ToString());
            Mode = mode;
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        internal static bool ResolveDark(ThemeMode mode, bool systemUsesLightTheme)
        {
            switch (mode)
            {
                case ThemeMode.System: return !systemUsesLightTheme;
                case ThemeMode.Light: return false;
                case ThemeMode.Dark: return true;
                default: throw new ArgumentOutOfRangeException("mode");
            }
        }

        private static bool ReadSystemUsesLightTheme()
        {
            try
            {
                return !Object.Equals(Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1), 0);
            }
            catch { return true; }
        }
    }
}
