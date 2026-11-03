using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PortPeek
{
    public sealed class PortEntry
    {
        private static readonly int OwnProcessId = GetOwnProcessId();
        public int Port, ProcessId;
        public string Protocol, ProcessName;
        public string[] Addresses = new string[0];
        public DateTime? ProcessStartedAtUtc;

        public string Key
        {
            get
            {
                return NormalizeProtocol(Protocol) + ":" + Port.ToString(CultureInfo.InvariantCulture)
                    + ":" + ProcessId.ToString(CultureInfo.InvariantCulture);
            }
        }

        public bool CanTerminate
        {
            get { return ProcessId > 4 && ProcessId != OwnProcessId && ProcessStartedAtUtc.HasValue; }
        }

        public string BindingDisplay
        {
            get
            {
                SortedSet<string> labels = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string address in NormalizeAddresses(Addresses))
                {
                    IPAddress ip;
                    if (IPAddress.TryParse(address, out ip))
                    {
                        if (IsWildcard(ip)) { labels.Add("모든 인터페이스"); continue; }
                        if (IPAddress.IsLoopback(ip)) { labels.Add("localhost"); continue; }
                    }
                    labels.Add(address);
                }
                string[] result = new string[labels.Count];
                labels.CopyTo(result);
                return string.Join(", ", result);
            }
        }

        public string BrowserUrl
        {
            get
            {
                if (NormalizeProtocol(Protocol) != "TCP" || Port <= 0 || Port > 65535) return null;
                string host = null;
                foreach (string address in NormalizeAddresses(Addresses))
                {
                    IPAddress ip;
                    if (!IPAddress.TryParse(address, out ip)) continue;
                    if (IsWildcard(ip) || IPAddress.IsLoopback(ip)) { host = "localhost"; break; }
                    if (host == null)
                        host = ip.AddressFamily == AddressFamily.InterNetworkV6
                            ? "[" + ip.ToString().Replace("%", "%25") + "]" : ip.ToString();
                }
                if (host == null) return null;
                string scheme = Port == 443 || Port == 8443 ? "https://" : "http://";
                return scheme + host + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/";
            }
        }

        private static int GetOwnProcessId()
        {
            using (Process process = Process.GetCurrentProcess()) return process.Id;
        }

        private static bool IsWildcard(IPAddress address)
        {
            return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
        }

        internal static string NormalizeProtocol(string protocol)
        {
            return (protocol ?? string.Empty).Trim().ToUpperInvariant();
        }

        internal static string[] NormalizeAddresses(IEnumerable<string> addresses)
        {
            SortedSet<string> distinct = new SortedSet<string>(StringComparer.Ordinal);
            if (addresses != null)
            {
                foreach (string address in addresses)
                {
                    if (string.IsNullOrWhiteSpace(address)) continue;
                    string value = address.Trim();
                    IPAddress ip;
                    distinct.Add(IPAddress.TryParse(value, out ip) ? ip.ToString() : value);
                }
            }
            string[] result = new string[distinct.Count];
            distinct.CopyTo(result);
            return result;
        }
    }

    public sealed class PortSnapshot
    {
        public PortEntry[] Entries = new PortEntry[0];
        public string Error;
        public DateTime CapturedAt;
    }

    public static class PortReader
    {
        internal delegate uint TableGetter(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved);
        private const uint InsufficientBuffer = 122;
        private const int Ipv4 = 2;
        private const int Ipv6 = 23;

        public static PortSnapshot Read()
        {
            PortSnapshot snapshot = new PortSnapshot { CapturedAt = DateTime.Now };
            List<PortEntry> entries = new List<PortEntry>();
            List<string> errors = new List<string>();
            ReadFamily(true, Ipv4, GetExtendedTcpTable, entries, errors);
            ReadFamily(true, Ipv6, GetExtendedTcpTable, entries, errors);
            ReadFamily(false, Ipv4, GetExtendedUdpTable, entries, errors);
            ReadFamily(false, Ipv6, GetExtendedUdpTable, entries, errors);
            snapshot.Entries = GroupEntries(entries);
            Dictionary<int, ProcessIdentity> processes = new Dictionary<int, ProcessIdentity>();
            foreach (PortEntry entry in snapshot.Entries)
            {
                ProcessIdentity identity;
                if (!processes.TryGetValue(entry.ProcessId, out identity))
                {
                    identity = ResolveProcess(entry.ProcessId);
                    processes.Add(entry.ProcessId, identity);
                }
                entry.ProcessName = identity.Name;
                entry.ProcessStartedAtUtc = identity.StartedAtUtc;
            }
            snapshot.Error = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors.ToArray());
            return snapshot;
        }

        public static PortEntry[] GroupEntries(IEnumerable<PortEntry> entries)
        {
            Dictionary<string, EntryGroup> groups = new Dictionary<string, EntryGroup>(StringComparer.Ordinal);
            if (entries != null)
            {
                foreach (PortEntry entry in entries)
                {
                    if (entry == null) continue;
                    EntryGroup group;
                    if (!groups.TryGetValue(entry.Key, out group))
                    {
                        group = new EntryGroup();
                        group.Entry = new PortEntry {
                            Protocol = PortEntry.NormalizeProtocol(entry.Protocol), Port = entry.Port,
                            ProcessId = entry.ProcessId, ProcessName = entry.ProcessName,
                            ProcessStartedAtUtc = entry.ProcessStartedAtUtc
                        };
                        groups.Add(entry.Key, group);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(group.Entry.ProcessName)
                            || group.Entry.ProcessName == FallbackName(entry.ProcessId))
                            group.Entry.ProcessName = entry.ProcessName;
                        if (!group.Entry.ProcessStartedAtUtc.HasValue)
                            group.Entry.ProcessStartedAtUtc = entry.ProcessStartedAtUtc;
                    }
                    foreach (string address in PortEntry.NormalizeAddresses(entry.Addresses)) group.Addresses.Add(address);
                }
            }
            List<PortEntry> result = new List<PortEntry>();
            foreach (EntryGroup group in groups.Values)
            {
                group.Entry.Addresses = new string[group.Addresses.Count];
                group.Addresses.CopyTo(group.Entry.Addresses);
                result.Add(group.Entry);
            }
            result.Sort(delegate(PortEntry left, PortEntry right) {
                int comparison = left.Port.CompareTo(right.Port);
                if (comparison == 0) comparison = StringComparer.Ordinal.Compare(left.Protocol, right.Protocol);
                if (comparison == 0) comparison = left.ProcessId.CompareTo(right.ProcessId);
                return comparison;
            });
            return result.ToArray();
        }

        internal static void ReadFamily(bool tcp, int family, TableGetter getter, List<PortEntry> entries, List<string> errors)
        {
            string label = (tcp ? "TCP" : "UDP") + (family == Ipv4 ? " IPv4" : " IPv6");
            int tableClass = tcp ? 3 : 1;
            try
            {
                int size = 0;
                uint status = getter(IntPtr.Zero, ref size, false, family, tableClass, 0);
                if (status == 0 && size == 0) return;
                if (status != 0 && status != InsufficientBuffer)
                {
                    errors.Add(label + ": " + new Win32Exception(unchecked((int)status)).Message);
                    return;
                }
                // Endpoint tables can grow between the size probe and the read.
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (size < sizeof(uint)) { errors.Add(label + ": incomplete table header"); return; }
                    int allocated = size;
                    IntPtr buffer = Marshal.AllocHGlobal(allocated);
                    try
                    {
                        status = getter(buffer, ref size, false, family, tableClass, 0);
                        if (status == InsufficientBuffer) continue;
                        if (status != 0)
                        {
                            errors.Add(label + ": " + new Win32Exception(unchecked((int)status)).Message);
                            return;
                        }
                        if (size == 0) return;
                        if (size < sizeof(uint) || size > allocated)
                        {
                            errors.Add(label + ": incomplete table header or invalid size");
                            return;
                        }
                        ParseTable(buffer, size, tcp, family, entries, errors, label);
                        return;
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                errors.Add(label + ": endpoint table kept changing; retry refresh");
            }
            catch (Exception error)
            {
                // A failed family must not discard the other three tables.
                errors.Add(label + ": " + error.Message);
            }
        }

        private static void ParseTable(IntPtr buffer, int size, bool tcp, int family,
            List<PortEntry> entries, List<string> errors, string label)
        {
            Type rowType = tcp ? (family == Ipv4 ? typeof(Tcp4Row) : typeof(Tcp6Row))
                : (family == Ipv4 ? typeof(Udp4Row) : typeof(Udp6Row));
            int rowSize = Marshal.SizeOf(rowType);
            // OWNER_PID rows have DWORD alignment; the DWORD count occupies the first four bytes.
            int offset = sizeof(uint);
            uint count = unchecked((uint)Marshal.ReadInt32(buffer));
            int available = (size - offset) / rowSize;
            if (count > (uint)available) errors.Add(label + ": incomplete endpoint table");
            int length = (int)Math.Min(count, (uint)available);
            for (int index = 0; index < length; index++)
            {
                IntPtr pointer = IntPtr.Add(buffer, offset + index * rowSize);
                uint port;
                uint pid;
                IPAddress address;
                if (tcp && family == Ipv4)
                {
                    Tcp4Row row = (Tcp4Row)Marshal.PtrToStructure(pointer, rowType);
                    if (row.State != 2) continue;
                    port = row.LocalPort; pid = row.OwningPid;
                    address = new IPAddress(BitConverter.GetBytes(row.LocalAddress));
                }
                else if (tcp)
                {
                    Tcp6Row row = (Tcp6Row)Marshal.PtrToStructure(pointer, rowType);
                    if (row.State != 2) continue;
                    port = row.LocalPort; pid = row.OwningPid;
                    // Windows supplies the scope DWORD in host order, as used by IPGlobalProperties.
                    address = new IPAddress(row.LocalAddress, row.LocalScopeId);
                }
                else if (family == Ipv4)
                {
                    Udp4Row row = (Udp4Row)Marshal.PtrToStructure(pointer, rowType);
                    port = row.LocalPort; pid = row.OwningPid;
                    address = new IPAddress(BitConverter.GetBytes(row.LocalAddress));
                }
                else
                {
                    Udp6Row row = (Udp6Row)Marshal.PtrToStructure(pointer, rowType);
                    port = row.LocalPort; pid = row.OwningPid;
                    address = new IPAddress(row.LocalAddress, row.LocalScopeId);
                }
                entries.Add(new PortEntry {
                    Protocol = tcp ? "TCP" : "UDP", Port = (int)(((port & 255) << 8) | ((port >> 8) & 255)),
                    ProcessId = unchecked((int)pid), Addresses = new string[] { address.ToString() }
                });
            }
        }

        private static string FallbackName(int pid)
        {
            return "PID " + pid.ToString(CultureInfo.InvariantCulture);
        }

        private static ProcessIdentity ResolveProcess(int pid)
        {
            ProcessIdentity identity = new ProcessIdentity { Name = FallbackName(pid) };
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    try
                    {
                        string name = process.ProcessName;
                        if (!string.IsNullOrWhiteSpace(name)) identity.Name = name;
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (System.Security.SecurityException) { }
                    try { identity.StartedAtUtc = process.StartTime.ToUniversalTime(); }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (System.Security.SecurityException) { }
                }
            }
            catch (ArgumentException) { }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (System.Security.SecurityException) { }
            return identity;
        }

        private sealed class ProcessIdentity
        {
            public string Name;
            public DateTime? StartedAtUtc;
        }

        private sealed class EntryGroup
        {
            public PortEntry Entry;
            public readonly SortedSet<string> Addresses = new SortedSet<string>(StringComparer.Ordinal);
        }

        // Layouts follow the Microsoft OWNER_PID structures in tcpmib.h and udpmib.h.
        [StructLayout(LayoutKind.Sequential)]
        private struct Tcp4Row
        {
            public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Udp4Row
        {
            public uint LocalAddress, LocalPort, OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Tcp6Row
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
            public uint LocalScopeId, LocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
            public uint RemoteScopeId, RemotePort, State, OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Udp6Row
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
            public uint LocalScopeId, LocalPort, OwningPid;
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetExtendedUdpTable(IntPtr table, ref int size,
            [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
    }
}
