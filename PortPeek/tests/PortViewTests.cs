using System;
using System.IO;
using PortPeek;

internal static class PortViewTests
{
    private static int Main()
    {
        string testFolder = Path.Combine(Path.GetTempPath(), "PortPeekViewTests-" + Guid.NewGuid().ToString("N"));
        string favoriteFile = Path.Combine(testFolder, "favorites.txt");
        var entries = new[] {
            new PortEntry { Port = 8080, ProcessId = 301, Protocol = "TCP", ProcessName = "java", Addresses = new[] { "0.0.0.0" } },
            new PortEntry { Port = 80, ProcessId = 77, Protocol = "TCP", ProcessName = "nginx", Addresses = new[] { "127.0.0.1" } },
            new PortEntry { Port = 5353, ProcessId = 92, Protocol = "UDP", ProcessName = "Node", Addresses = new[] { "192.0.2.10" } }
        };
        try
        {
            var sorted = PortView.FilterSort(entries, "", "", 0, true);
            Check(sorted.Length == 3 && sorted[0].Port == 80 && sorted[2].Port == 8080, "Ports must sort numerically.");
            Check(PortView.FilterSort(entries, "8080", "", 0, true).Length == 1, "Port search failed.");
            Check(PortView.FilterSort(entries, " node ", "", 0, true)[0].ProcessId == 92, "Process search must trim and ignore case.");
            Check(PortView.FilterSort(entries, "301", "TCP", 0, true)[0].ProcessName == "java", "PID search failed.");
            Check(PortView.FilterSort(entries, "192.0.2", "UDP", 0, true).Length == 1, "Address search failed.");
            Check(PortView.FilterSort(entries, "node", "TCP", 0, true).Length == 0, "Filters must combine.");
            Check(PortView.FilterSort(entries, "", "", 2, true)[0].ProcessId == 77, "PIDs must sort numerically.");
            Check(PortView.FilterSort(entries, "", "", 0, false)[0].Port == 8080, "Descending sort failed.");
            Check(PortView.FilterSort(null, "", "", 0, true).Length == 0, "Null snapshot must be safe.");
            Console.WriteLine("PASS: port and process search, combined protocol filter, numeric sorting and empty input.");

            var favorites = FavoriteStore.Load(favoriteFile);
            favorites.TogglePort(8080);
            Check(PortView.FilterSort(entries, "", "", 0, true, favorites)[0].Port == 8080, "A favorite port must precede lower port numbers.");
            favorites.TogglePort(8080);
            favorites.TogglePort(80);
            Check(PortView.FilterSort(entries, "", "", 0, false, favorites)[0].Port == 80, "Favorites must stay first under descending sort.");
            favorites.ToggleProgram("NODE");
            var withProgram = PortView.FilterSort(entries, "", "", 1, true, favorites);
            Check(withProgram[2].Port == 8080, "Program and port favorites must both precede ordinary rows.");
            var restarted = new PortEntry { Port = 9000, ProcessId = 901, Protocol = "TCP", ProcessName = "node", Addresses = new[] { "127.0.0.1" } };
            var reloaded = FavoriteStore.Load(favoriteFile);
            Check(PortView.FilterSort(new[] { entries[0], restarted }, "", "", 0, true, reloaded)[0] == restarted, "Program favorites must apply to a new PID and a different port after reload.");
            Check(PortView.FilterSort(entries, "java", "TCP", 0, true, reloaded).Length == 1, "Favorites must not bypass search and protocol filters.");
            reloaded.ToggleProgram("node");
            reloaded.TogglePort(80);
            Check(PortView.FilterSort(entries, "", "", 0, true, reloaded)[0].Port == 80, "Removing favorites must restore normal ordering.");
            Console.WriteLine("PASS: port/program favorites, descending sort, reload, changed PID and filtered results.");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
        finally
        {
            if (File.Exists(favoriteFile)) File.Delete(favoriteFile);
            if (Directory.Exists(testFolder)) Directory.Delete(testFolder);
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
