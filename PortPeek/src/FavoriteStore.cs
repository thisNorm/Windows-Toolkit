using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace PortPeek
{
    internal sealed class FavoriteStore
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string fileName;
        private readonly SortedSet<int> ports = new SortedSet<int>();
        private readonly SortedSet<string> programs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool readable = true;

        internal event EventHandler Changed;

        private FavoriteStore(string fileName)
        {
            this.fileName = Path.GetFullPath(fileName);
        }

        internal static FavoriteStore Load()
        {
            return Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsToolkit", "PortPeek", "favorites.txt"));
        }

        internal static FavoriteStore Load(string fileName)
        {
            FavoriteStore store = new FavoriteStore(fileName);
            string[] lines;
            try { lines = File.ReadAllLines(store.fileName, Utf8); }
            catch (FileNotFoundException) { return store; }
            catch (DirectoryNotFoundException) { return store; }
            catch (IOException) { store.readable = false; return store; }
            catch (UnauthorizedAccessException) { store.readable = false; return store; }
            catch (SecurityException) { store.readable = false; return store; }
            catch (DecoderFallbackException) { store.readable = false; return store; }

            foreach (string line in lines)
            {
                if (line.StartsWith("port:", StringComparison.Ordinal))
                {
                    int port;
                    if (Int32.TryParse(line.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out port)
                        && ValidPort(port)) store.ports.Add(port);
                }
                else if (line.StartsWith("program:", StringComparison.Ordinal))
                {
                    string name = line.Substring(8);
                    if (ValidProgram(name)) store.programs.Add(name);
                }
            }
            return store;
        }

        internal bool IsPortFavorite(int port)
        {
            return ports.Contains(port);
        }

        internal bool IsProgramFavorite(string name)
        {
            return ValidProgram(name) && programs.Contains(name);
        }

        internal bool IsFavorite(PortEntry entry)
        {
            return entry != null && (IsPortFavorite(entry.Port) || IsProgramFavorite(entry.ProcessName));
        }

        internal static bool CanFavoriteProgram(PortEntry entry)
        {
            return entry != null && ValidProgram(entry.ProcessName)
                && entry.ProcessName != "PID " + entry.ProcessId.ToString(CultureInfo.InvariantCulture);
        }

        internal void TogglePort(int port)
        {
            if (ValidPort(port)) Toggle(ports, port);
        }

        internal void ToggleProgram(string name)
        {
            if (ValidProgram(name)) Toggle(programs, name);
        }

        private static bool ValidPort(int port)
        {
            return port > 0 && port <= 65535;
        }

        private static bool ValidProgram(string name)
        {
            return !String.IsNullOrWhiteSpace(name) && name.IndexOf('\r') < 0
                && name.IndexOf('\n') < 0 && name.IndexOf('\0') < 0;
        }

        private void Toggle<T>(SortedSet<T> values, T value)
        {
            // An unreadable load must not turn an existing configuration into an empty one.
            if (!readable) return;
            bool added = values.Add(value);
            if (!added) values.Remove(value);
            if (!TrySave())
            {
                if (added) values.Remove(value);
                else values.Add(value);
                return;
            }
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private bool TrySave()
        {
            string temporary = null;
            try
            {
                string directory = Path.GetDirectoryName(fileName);
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, "favorites-" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, Utf8))
                {
                    foreach (int port in ports) writer.WriteLine("port:" + port.ToString(CultureInfo.InvariantCulture));
                    foreach (string name in programs) writer.WriteLine("program:" + name);
                    writer.Flush();
                    stream.Flush(true);
                }
                // Replace atomically; a failed replacement leaves the original intact.
                if (File.Exists(fileName)) File.Replace(temporary, fileName, null);
                else File.Move(temporary, fileName);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
            catch (EncoderFallbackException) { return false; }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (SecurityException) { }
                }
            }
        }
    }
}
