using System;
using System.IO;
using System.Text;
using PortPeek;

internal static class FavoriteStoreTests
{
    private static int passed, failed;

    private static int Main()
    {
        Run("independent toggles and committed change events", IndependentToggles);
        Run("port identity ignores protocol and PID; favorites are a union", FavoriteUnion);
        Run("program identity ignores case, port and new PID after reload", ProgramIdentity);
        Run("only the exact reader fallback is ineligible", ProgramEligibility);
        Run("invalid lines are ignored without losing valid favorites", InvalidLines);
        Run("invalid toggles do not write or notify", InvalidToggles);
        Run("failed additions and removals roll back without events", FailedSave);
        Run("unreadable existing configuration is never overwritten", UnreadableLoad);
        Run("invalid UTF8 configuration is preserved", InvalidUtf8);
        Console.WriteLine("FavoriteStoreTests: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action<string> test)
    {
        string folder = Path.Combine(Path.GetTempPath(), "PortPeek-FavoriteStoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            test(Path.Combine(folder, "favorites.txt"));
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception error) { failed++; Console.WriteLine("FAIL: " + name + ": " + error.Message); }
        finally { Directory.Delete(folder, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static PortEntry Entry(int port, int pid, string protocol, string name)
    {
        return new PortEntry { Port = port, ProcessId = pid, Protocol = protocol, ProcessName = name };
    }

    private static void IndependentToggles(string file)
    {
        file = Path.Combine(Path.GetDirectoryName(file), "nested", "favorites.txt");
        FavoriteStore store = FavoriteStore.Load(file);
        int changes = 0;
        store.Changed += delegate(object sender, EventArgs args) {
            Check(Object.ReferenceEquals(sender, store), "Wrong event sender.");
            FavoriteStore saved = FavoriteStore.Load(file);
            Check(saved.IsPortFavorite(8080) == store.IsPortFavorite(8080)
                && saved.IsProgramFavorite("node") == store.IsProgramFavorite("node"), "Event preceded persistence.");
            changes++;
        };
        store.TogglePort(8080);
        Check(store.IsPortFavorite(8080) && !store.IsProgramFavorite("node"), "Port toggle did not stay independent.");
        store.ToggleProgram("node");
        Check(store.IsPortFavorite(8080) && store.IsProgramFavorite("node"), "Program toggle lost port favorite.");
        store.TogglePort(8080);
        Check(!store.IsPortFavorite(8080) && store.IsProgramFavorite("node"), "Port removal lost program favorite.");
        store.ToggleProgram("NODE");
        FavoriteStore reloaded = FavoriteStore.Load(file);
        Check(!reloaded.IsPortFavorite(8080) && !reloaded.IsProgramFavorite("node"), "Removals did not persist.");
        Check(changes == 4, "Expected one event per successful toggle.");
    }

    private static void FavoriteUnion(string file)
    {
        FavoriteStore store = FavoriteStore.Load(file);
        store.TogglePort(8080);
        Check(store.IsFavorite(Entry(8080, 10, "TCP", "node")), "TCP port missing.");
        Check(store.IsFavorite(Entry(8080, 20, "UDP", "other")), "Port incorrectly tied to protocol or PID.");
        Check(!store.IsFavorite(Entry(8081, 10, "TCP", "node")), "PID alone matched.");
        store.ToggleProgram("node");
        Check(store.IsFavorite(Entry(9000, 30, "UDP", "NODE")), "Program side of union missing.");
        store.TogglePort(8080);
        Check(store.IsFavorite(Entry(8080, 40, "TCP", "node")), "Removing port disabled program favorite.");
        Check(!store.IsFavorite(null), "Null entry matched.");
    }

    private static void ProgramIdentity(string file)
    {
        FavoriteStore store = FavoriteStore.Load(file);
        store.ToggleProgram("Node");
        store.ToggleProgram("\uD55C\uAE00 worker");
        store = FavoriteStore.Load(file);
        Check(store.IsProgramFavorite("nOdE"), "Case-insensitive reload failed.");
        Check(store.IsFavorite(Entry(3000, 111, "TCP", "node"))
            && store.IsFavorite(Entry(4000, 222, "UDP", "NODE")), "Program favorite did not survive new port/PID.");
        Check(!store.IsFavorite(Entry(3000, 111, "TCP", "other")), "Program favorite leaked to another program.");
        Check(store.IsProgramFavorite("\uD55C\uAE00 worker"), "UTF8 name was lost.");
        store.ToggleProgram("NODE");
        Check(!FavoriteStore.Load(file).IsProgramFavorite("Node"), "Case-insensitive removal did not persist.");
    }

    private static void ProgramEligibility(string file)
    {
        Check(!FavoriteStore.CanFavoriteProgram(null), "Null entry was eligible.");
        foreach (string name in new string[] { null, "", "  ", "PID 42" })
            Check(!FavoriteStore.CanFavoriteProgram(Entry(80, 42, "TCP", name)), "Missing/fallback name was eligible.");
        foreach (string name in new string[] { "node", "PID Runner", "PID 43", "pid 42" })
            Check(FavoriteStore.CanFavoriteProgram(Entry(80, 42, "TCP", name)), "Actual name was rejected: " + name);
    }

    private static void InvalidLines(string file)
    {
        File.WriteAllLines(file, new string[] {
            "", "garbage", "port:", "port:no", "port:8080x", "port:-1", "port:0", "port:65536",
            "port:999999999999999999", "program:", "program:   ", "program:bad\0name", "pid:42", "tcp:9000",
            "port:8080", "port:1", "port:65535", "program:node", "program:NODE", "program:worker:child"
        }, new UTF8Encoding(false));
        FavoriteStore store = FavoriteStore.Load(file);
        Check(store.IsPortFavorite(8080) && store.IsPortFavorite(1) && store.IsPortFavorite(65535), "Valid ports lost.");
        Check(!store.IsPortFavorite(0) && !store.IsPortFavorite(-1) && !store.IsPortFavorite(65536)
            && !store.IsPortFavorite(9000), "Invalid port line accepted.");
        Check(store.IsProgramFavorite("NODE") && store.IsProgramFavorite("worker:child"), "Valid program names lost.");
        Check(!store.IsProgramFavorite("") && !store.IsProgramFavorite("   "), "Empty program accepted.");
        store.TogglePort(9090);
        Check(FavoriteStore.Load(file).IsProgramFavorite("node"), "Saving after invalid lines lost valid data.");
    }

    private static void InvalidToggles(string file)
    {
        FavoriteStore store = FavoriteStore.Load(file);
        int changes = 0;
        store.Changed += delegate { changes++; };
        foreach (int port in new int[] { -1, 0, 65536 }) store.TogglePort(port);
        foreach (string name in new string[] { null, "", " ", "bad\rname", "bad\nport:8080", "bad\0name" })
            store.ToggleProgram(name);
        Check(changes == 0 && !File.Exists(file), "Invalid toggle wrote configuration or raised Changed.");
    }

    private static void FailedSave(string file)
    {
        FavoriteStore store = FavoriteStore.Load(file);
        int changes = 0;
        store.Changed += delegate { changes++; };
        store.TogglePort(8080);
        store.ToggleProgram("node");
        Check(changes == 2, "Successful setup did not persist/notify.");
        string original = File.ReadAllText(file);
        using (FileStream locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            store.TogglePort(8080);
            store.ToggleProgram("NODE");
            store.TogglePort(9090);
            store.ToggleProgram("python");
            Check(store.IsPortFavorite(8080) && store.IsProgramFavorite("node"), "Failed removal was not rolled back.");
            Check(!store.IsPortFavorite(9090) && !store.IsProgramFavorite("python"), "Failed addition was not rolled back.");
            Check(changes == 2, "Failed saves raised Changed.");
        }
        Check(File.ReadAllText(file) == original, "Failed save damaged original configuration.");
        Check(Directory.GetFiles(Path.GetDirectoryName(file)).Length == 1, "Failed save left temporary files.");
        store.TogglePort(8080);
        Check(changes == 3 && !FavoriteStore.Load(file).IsPortFavorite(8080), "Save did not recover after unlocking.");

        string blocker = Path.Combine(Path.GetDirectoryName(file), "blocker");
        File.WriteAllText(blocker, "keep");
        FavoriteStore blocked = FavoriteStore.Load(Path.Combine(blocker, "favorites.txt"));
        blocked.Changed += delegate { throw new Exception("Failed first save raised Changed."); };
        blocked.TogglePort(80);
        blocked.ToggleProgram("node");
        Check(!blocked.IsPortFavorite(80) && !blocked.IsProgramFavorite("node")
            && File.ReadAllText(blocker) == "keep", "Failed first save was not rolled back safely.");
    }

    private static void UnreadableLoad(string file)
    {
        const string original = "port:8080\nprogram:node\n";
        File.WriteAllText(file, original, new UTF8Encoding(false));
        FavoriteStore store;
        using (FileStream locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store = FavoriteStore.Load(file);
        int changes = 0;
        store.Changed += delegate { changes++; };
        store.TogglePort(9090);
        store.ToggleProgram("python");
        Check(!store.IsPortFavorite(9090) && !store.IsProgramFavorite("python") && changes == 0,
            "Unreadable load allowed a toggle after unlocking.");
        Check(File.ReadAllText(file) == original, "Unreadable existing configuration was overwritten.");
        Check(FavoriteStore.Load(file).IsPortFavorite(8080), "Original favorites were lost.");
    }

    private static void InvalidUtf8(string file)
    {
        byte[] original = new byte[] { 0x70, 0x72, 0x6f, 0x67, 0x72, 0x61, 0x6d, 0x3a, 0xc3, 0x28 };
        File.WriteAllBytes(file, original);
        FavoriteStore store = FavoriteStore.Load(file);
        int changes = 0;
        store.Changed += delegate { changes++; };
        store.ToggleProgram("node");
        Check(!store.IsProgramFavorite("node") && changes == 0, "Unreadable UTF8 allowed a toggle.");
        Check(Convert.ToBase64String(File.ReadAllBytes(file)) == Convert.ToBase64String(original), "Invalid UTF8 file was overwritten.");
    }
}
