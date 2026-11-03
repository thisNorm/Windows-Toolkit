using System;
using System.IO;
using PortPeek;

internal static class ThemeSettingsTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortPeekTheme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "nested", "theme.txt");
            ThemeSettings settings = ThemeSettings.Load(file);
            Assert(settings.Mode == ThemeMode.System && !File.Exists(file), "Missing file defaults to System without writing");
            Assert(!ThemeSettings.ResolveDark(ThemeMode.Light, false) &&
                !ThemeSettings.ResolveDark(ThemeMode.Light, true), "Explicit Light ignores Windows");
            Assert(ThemeSettings.ResolveDark(ThemeMode.Dark, false) &&
                ThemeSettings.ResolveDark(ThemeMode.Dark, true), "Explicit Dark ignores Windows");
            Assert(!ThemeSettings.ResolveDark(ThemeMode.System, true), "System follows light Windows preference");
            Assert(ThemeSettings.ResolveDark(ThemeMode.System, false), "System follows dark Windows preference");

            int changes = 0;
            settings.Changed += delegate(object sender, EventArgs args)
            {
                changes++;
                Assert(Object.ReferenceEquals(sender, settings), "Changed sender is settings");
                Assert(File.ReadAllText(file) == settings.Mode.ToString(), "Changed observes persisted mode");
            };
            settings.SetMode(ThemeMode.System);
            Assert(!File.Exists(file) && changes == 0, "Same default mode does not write or notify");
            settings.SetMode(ThemeMode.Dark);
            Assert(settings.Mode == ThemeMode.Dark && settings.IsDark && changes == 1, "Dark change persists and notifies once");
            Assert(File.ReadAllText(file) == "Dark", "Only the enum name is saved");
            Assert(ThemeSettings.Load(file).Mode == ThemeMode.Dark, "Saved mode reloads");
            DateTime stamp = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, stamp);
            DateTime savedStamp = File.GetLastWriteTimeUtc(file);
            settings.SetMode(ThemeMode.Dark);
            Assert(changes == 1 && File.GetLastWriteTimeUtc(file) == savedStamp, "Same selection does not rewrite or notify");
            settings.SetMode(ThemeMode.Light);
            Assert(!settings.IsDark && changes == 2, "Explicit Light resolves and notifies");
            settings.SetMode(ThemeMode.System);
            Assert(settings.Mode == ThemeMode.System && changes == 3 && File.ReadAllText(file) == "System", "System persists its enum name");
            try { settings.SetMode((ThemeMode)99); throw new Exception("Invalid mode was accepted"); }
            catch (ArgumentOutOfRangeException) { }
            Assert(settings.Mode == ThemeMode.System && changes == 3 && File.ReadAllText(file) == "System", "Invalid mode has no side effects");

            File.WriteAllText(file, "invalid theme");
            Assert(ThemeSettings.Load(file).Mode == ThemeMode.System, "Invalid text defaults to System");
            File.WriteAllText(file, "1");
            Assert(ThemeSettings.Load(file).Mode == ThemeMode.System, "Numeric text is not a mode name");

            File.WriteAllText(file, "Dark");
            ThemeSettings failing = ThemeSettings.Load(file);
            int failedChanges = 0;
            failing.Changed += delegate { failedChanges++; };
            using (FileStream locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { failing.SetMode(ThemeMode.Light); throw new Exception("Save unexpectedly succeeded"); }
                catch (IOException) { }
            }
            Assert(failing.Mode == ThemeMode.Dark && failedChanges == 0 && File.ReadAllText(file) == "Dark", "Save failure preserves mode, event count and file");

            using (FileStream locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ThemeSettings unreadable = ThemeSettings.Load(file);
                Assert(unreadable.Mode == ThemeMode.System, "Unreadable file defaults to System");
                locked.Dispose();
                try { unreadable.SetMode(ThemeMode.Light); throw new Exception("Unreadable file was overwritten"); }
                catch (IOException) { }
            }
            Assert(File.ReadAllText(file) == "Dark", "Unreadable preference is preserved");
            Console.WriteLine("PASS: ThemeSettings focused tests");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
        finally { Directory.Delete(root, true); }
    }
}
