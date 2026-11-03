using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using PortPeek;

internal static class PortReaderTests
{
    private static int passed;
    private static int failed;
    private static int skipped;
    private static readonly DateTime Started = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static int Main()
    {
        Run("group dual stack and retain metadata", GroupDualStack);
        Run("separate identities and numeric ordering", GroupIdentities);
        Run("empty grouping and independent output", GroupEmpty);
        Run("stable normalized identity key", StableKey);
        Run("readable distinct binding labels", BindingLabels);
        Run("browser wildcard and loopback", BrowserLocal);
        Run("browser specific IP and IPv6", BrowserSpecific);
        Run("browser TCP only and TLS ports", BrowserProtocol);
        Run("protect system self and unknown start", TerminationGuards);
        Run("native IPv4 TCP layout and listen filter", NativeTcp4);
        Run("native IPv4 UDP layout", NativeUdp4);
        Run("native IPv6 TCP and UDP layouts", NativeIpv6);
        Run("native IPv6 scope keeps host byte order", NativeIpv6ScopeHostOrder);
        Run("empty native tables", EmptyTables);
        Run("retry native buffer growth", BufferGrowth);
        Run("bound repeated native buffer growth", ContinuousGrowth);
        Run("retain entries on partial family failure", PartialFailure);
        Run("retain complete rows in truncated table", TruncatedTable);
        Run("live owned IPv4 TCP and UDP", LiveIpv4);
        if (Socket.OSSupportsIPv6)
            Run("live owned IPv6 TCP and UDP", LiveIpv6);
        else
        {
            skipped++;
            Console.WriteLine("SKIP live IPv6: IPv6 unavailable");
        }
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed, " + skipped + " skipped");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual), message + "; expected " + expected + ", got " + actual);
    }

    private static PortEntry Entry(string protocol, int port, int pid, params string[] addresses)
    {
        return new PortEntry { Protocol = protocol, Port = port, ProcessId = pid, Addresses = addresses };
    }

    private static void GroupDualStack()
    {
        PortEntry first = Entry("tcp", 8080, 12345, "::1", "127.0.0.1", "::1");
        PortEntry second = Entry("TCP", 8080, 12345, "0:0:0:0:0:0:0:1", "192.0.2.10");
        second.ProcessName = "fixture-worker";
        second.ProcessStartedAtUtc = Started;
        PortEntry[] rows = PortReader.GroupEntries(new PortEntry[] { first, second });
        Equal(1, rows.Length, "IPv4/IPv6 identities must merge");
        Equal("TCP", rows[0].Protocol, "canonical protocol");
        Equal("127.0.0.1|192.0.2.10|::1", string.Join("|", rows[0].Addresses), "distinct sorted canonical binds");
        Equal("fixture-worker", rows[0].ProcessName, "retain readable process name");
        Equal<DateTime?>(Started, rows[0].ProcessStartedAtUtc, "retain exact creation time");
    }

    private static void GroupIdentities()
    {
        PortEntry[] rows = PortReader.GroupEntries(new PortEntry[] {
            Entry("UDP", 80, 30), Entry("TCP", 100, 10), Entry("TCP", 80, 20),
            Entry("TCP", 9, 10), Entry("TCP", 80, 10), Entry("UDP", 80, 10) });
        Equal(6, rows.Length, "port protocol and PID all belong to identity");
        Equal(9, rows[0].Port, "numeric port ordering");
        Equal("TCP", rows[1].Protocol, "protocol tie break");
        Equal(10, rows[1].ProcessId, "PID tie break");
        Equal(20, rows[2].ProcessId, "shared port different PID");
        Equal("UDP", rows[3].Protocol, "same port different protocol");
        Equal(100, rows[5].Port, "numeric last port");
    }

    private static void GroupEmpty()
    {
        Equal(0, PortReader.GroupEntries(null).Length, "null collection is empty");
        PortEntry source = Entry("TCP", 80, 10, "192.0.2.1", null, " ", "192.0.2.1");
        PortEntry[] rows = PortReader.GroupEntries(new PortEntry[] { null, source });
        Equal(1, rows.Length, "ignore null entry");
        Equal(1, rows[0].Addresses.Length, "ignore empty duplicate binds");
        rows[0].Addresses[0] = "192.0.2.2";
        Equal("192.0.2.1", source.Addresses[0], "do not mutate input addresses");
    }

    private static void StableKey()
    {
        PortEntry first = Entry("tcp", 1234, 10, "0.0.0.0");
        PortEntry second = Entry("TCP", 1234, 10, "::");
        second.ProcessName = "other-name";
        second.ProcessStartedAtUtc = Started;
        Equal(first.Key, second.Key, "key excludes display metadata and IP family");
        Check(first.Key != Entry("UDP", 1234, 10).Key, "protocol distinguishes keys");
        Check(first.Key != Entry("TCP", 1234, 11).Key, "PID distinguishes keys");
        Check(first.Key != Entry("TCP", 1235, 10).Key, "port distinguishes keys");
    }

    private static void BindingLabels()
    {
        Equal("모든 인터페이스", Entry("TCP", 80, 10, "0.0.0.0", "::").BindingDisplay, "wildcards collapse");
        Equal("localhost", Entry("TCP", 80, 10, "127.0.0.1", "127.0.0.2", "::1").BindingDisplay, "loopbacks collapse");
        Equal("192.0.2.1, 2001:db8::1", Entry("TCP", 80, 10, "2001:db8::1", "192.0.2.1", "192.0.2.1").BindingDisplay, "actual distinct binds");
        Equal("", Entry("TCP", 80, 10, (string[])null).BindingDisplay, "missing binds are readable");
    }

    private static void BrowserLocal()
    {
        foreach (string address in new string[] { "0.0.0.0", "::", "127.0.0.1", "127.0.0.2", "::1" })
            Equal("http://localhost:8080/", Entry("TCP", 8080, 10, address).BrowserUrl, "local target");
        Equal("http://localhost:8080/", Entry("TCP", 8080, 10, "192.0.2.1", "::1").BrowserUrl, "loopback takes priority");
    }

    private static void BrowserSpecific()
    {
        Equal("http://192.0.2.10:8080/", Entry("TCP", 8080, 10, "192.0.2.10").BrowserUrl, "specific IPv4 target");
        Equal("http://[2001:db8::10]:8080/", Entry("TCP", 8080, 10, "2001:db8::10").BrowserUrl, "bracket IPv6 target");
        Equal("http://[fe80::1234%257]:8080/", Entry("TCP", 8080, 10, "fe80::1234%7").BrowserUrl, "escape IPv6 zone separator");
        Equal("http://192.0.2.1:8080/", Entry("TCP", 8080, 10, "192.0.2.2", "192.0.2.1").BrowserUrl, "choose deterministic actual bind");
    }

    private static void BrowserProtocol()
    {
        Equal("https://localhost:443/", Entry("TCP", 443, 10, "::").BrowserUrl, "HTTPS 443");
        Equal("https://192.0.2.1:8443/", Entry("tcp", 8443, 10, "192.0.2.1").BrowserUrl, "HTTPS 8443");
        Equal("http://localhost:444/", Entry("TCP", 444, 10, "0.0.0.0").BrowserUrl, "other ports use HTTP");
        Equal<string>(null, Entry("UDP", 443, 10, "::").BrowserUrl, "UDP has no browser URL");
        Equal<string>(null, Entry(null, 80, 10, "0.0.0.0").BrowserUrl, "unknown protocol has no browser URL");
    }

    private static void TerminationGuards()
    {
        foreach (int pid in new int[] { -1, 0, 1, 2, 3, 4 })
        {
            PortEntry row = Entry("TCP", 80, pid);
            row.ProcessStartedAtUtc = Started;
            Check(!row.CanTerminate, "protect PID <= 4");
        }
        int ownPid;
        using (Process own = Process.GetCurrentProcess()) ownPid = own.Id;
        PortEntry self = Entry("TCP", 80, ownPid);
        self.ProcessStartedAtUtc = Started;
        Check(!self.CanTerminate, "protect scanner itself");
        int otherPid = ownPid == 12345 ? 12346 : 12345;
        PortEntry other = Entry("TCP", 80, otherPid);
        Check(!other.CanTerminate, "protect missing creation time");
        other.ProcessStartedAtUtc = Started;
        Check(other.CanTerminate, "known non-system identity can be revalidated by caller");
    }

    private static byte[] Tcp4Table()
    {
        byte[] table = new byte[52];
        Put32(table, 0, 2);
        Put32(table, 4, 2);
        PutAddress(table, 8, "127.0.0.2");
        PutPort(table, 12, 49153);
        Put32(table, 24, 12345);
        Put32(table, 28, 5);
        PutAddress(table, 32, "192.0.2.1");
        PutPort(table, 36, 80);
        Put32(table, 48, 23456);
        return table;
    }

    private static void NativeTcp4()
    {
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(true, 2, Fixture(Tcp4Table()), rows, errors);
        Equal(0, errors.Count, "valid TCP table");
        Equal(1, rows.Count, "exclude established connections");
        Equal(49153, rows[0].Port, "TCP port network order");
        Equal(12345, rows[0].ProcessId, "TCP owner offset");
        Equal("127.0.0.2", rows[0].Addresses[0], "TCP IPv4 bytes");
    }

    private static void NativeUdp4()
    {
        byte[] table = new byte[16];
        Put32(table, 0, 1);
        PutAddress(table, 4, "192.0.2.10");
        PutPort(table, 8, 65001);
        Put32(table, 12, 23456);
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(false, 2, Fixture(table), rows, errors);
        Equal(0, errors.Count, "valid UDP table");
        Equal(1, rows.Count, "UDP row count");
        Equal("UDP", rows[0].Protocol, "UDP label");
        Equal(65001, rows[0].Port, "UDP port network order");
        Equal(23456, rows[0].ProcessId, "UDP owner offset");
        Equal("192.0.2.10", rows[0].Addresses[0], "UDP IPv4 bytes");
    }

    private static void NativeIpv6()
    {
        byte[] tcp = new byte[60];
        Put32(tcp, 0, 1);
        PutAddress(tcp, 4, "fe80::1234");
        Put32(tcp, 20, 7);
        PutPort(tcp, 24, 54321);
        Put32(tcp, 52, 2);
        Put32(tcp, 56, 34567);
        byte[] udp = new byte[32];
        Put32(udp, 0, 1);
        PutAddress(udp, 4, "2001:db8::10");
        PutPort(udp, 24, 54322);
        Put32(udp, 28, 45678);
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(true, 23, Fixture(tcp), rows, errors);
        PortReader.ReadFamily(false, 23, Fixture(udp), rows, errors);
        Equal(0, errors.Count, "valid IPv6 tables");
        Equal(2, rows.Count, "TCP and UDP IPv6 rows");
        Equal("fe80::1234%7", rows[0].Addresses[0], "IPv6 address and native host-order scope");
        Equal(54321, rows[0].Port, "IPv6 TCP port");
        Equal(34567, rows[0].ProcessId, "IPv6 TCP owner offset");
        Equal("2001:db8::10", rows[1].Addresses[0], "IPv6 UDP address");
        Equal(54322, rows[1].Port, "IPv6 UDP port");
        Equal(45678, rows[1].ProcessId, "IPv6 UDP owner offset");
    }

    private static void NativeIpv6ScopeHostOrder()
    {
        byte[] table = new byte[32];
        Put32(table, 0, 1);
        PutAddress(table, 4, "fe80::1234");
        Put32(table, 20, 257);
        PutPort(table, 24, 54322);
        Put32(table, 28, 12345);
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(false, 23, Fixture(table), rows, errors);
        Equal(0, errors.Count, "valid scoped UDP6 table");
        Equal(1, rows.Count, "scoped UDP6 row");
        IPEndPoint expected = new IPEndPoint(IPAddress.Parse("fe80::1234%257"), 54322);
        IPAddress actual = IPAddress.Parse(rows[0].Addresses[0]);
        Equal(expected.Address.ScopeId, actual.ScopeId, "DWORD scope must not be byte swapped");
        Equal(expected.Address.ToString(), rows[0].BindingDisplay, "display keeps actual interface index");
    }

    private static void EmptyTables()
    {
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(true, 2, Fixture(new byte[4]), rows, errors);
        PortReader.ReadFamily(false, 23, delegate(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved) { size = 0; return 0; }, rows, errors);
        Equal(0, rows.Count, "empty is valid");
        Equal(0, errors.Count, "empty is not an error");
    }

    private static void BufferGrowth()
    {
        byte[] table = Tcp4Table();
        int calls = 0;
        PortReader.TableGetter getter = delegate(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved) {
            calls++;
            Equal(2, family, "IPv4 family argument");
            Equal(3, tableClass, "TCP OWNER_PID_LISTENER argument");
            Equal((uint)0, reserved, "reserved argument");
            if (calls == 1) { size = 4; return 122; }
            if (size < table.Length) { size = table.Length; return 122; }
            Marshal.Copy(table, 0, buffer, table.Length); size = table.Length; return 0;
        };
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(true, 2, getter, rows, errors);
        Equal(3, calls, "probe resize successful read");
        Equal(1, rows.Count, "retry retains listener");
        Equal(0, errors.Count, "successful resize is not an error");
    }

    private static void ContinuousGrowth()
    {
        int calls = 0;
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(false, 2, delegate(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved) {
            calls++; size += 16; return 122;
        }, rows, errors);
        Check(calls > 1 && calls <= 10, "bounded retry count");
        Check(errors.Count == 1, "exhausted growth is reported");
        Equal(0, rows.Count, "failed buffer is never parsed");
    }

    private static void PartialFailure()
    {
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(true, 2, Fixture(Tcp4Table()), rows, errors);
        PortReader.ReadFamily(false, 23, delegate(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved) { return 50; }, rows, errors);
        Equal(1, rows.Count, "another family failure cannot discard successful rows");
        Equal(1, errors.Count, "report only failed family");
        Check(errors[0].Contains("UDP") && errors[0].Contains("IPv6"), "error identifies failed family");
    }

    private static void TruncatedTable()
    {
        byte[] table = new byte[28];
        Array.Copy(Tcp4Table(), table, table.Length);
        List<PortEntry> rows = new List<PortEntry>();
        List<string> errors = new List<string>();
        PortReader.ReadFamily(true, 2, Fixture(table), rows, errors);
        Equal(1, rows.Count, "preserve complete row without reading outside buffer");
        Equal(1, errors.Count, "report truncated table");
        PortReader.ReadFamily(false, 2, Fixture(new byte[2]), rows, errors);
        Equal(2, errors.Count, "report truncated header without crash");
    }

    private static PortReader.TableGetter Fixture(byte[] data)
    {
        return delegate(IntPtr buffer, ref int size, bool order, int family, int tableClass, uint reserved) {
            Equal(family == 2 || family == 23, true, "valid family argument");
            Check(tableClass == 1 || tableClass == 3, "owner PID table class");
            if (buffer == IntPtr.Zero || size < data.Length) { size = data.Length; return 122; }
            Marshal.Copy(data, 0, buffer, data.Length); size = data.Length; return 0;
        };
    }

    private static void Put32(byte[] bytes, int offset, int value)
    {
        byte[] encoded = BitConverter.GetBytes(value);
        Array.Copy(encoded, 0, bytes, offset, 4);
    }

    private static void PutPort(byte[] bytes, int offset, int port)
    {
        bytes[offset] = (byte)(port >> 8);
        bytes[offset + 1] = (byte)port;
    }

    private static void PutAddress(byte[] bytes, int offset, string address)
    {
        byte[] encoded = IPAddress.Parse(address).GetAddressBytes();
        Array.Copy(encoded, 0, bytes, offset, encoded.Length);
    }

    private static void LiveIpv4() { Live(IPAddress.Loopback); }
    private static void LiveIpv6() { Live(IPAddress.IPv6Loopback); }

    private static void Live(IPAddress address)
    {
        TcpListener tcp = new TcpListener(address, 0);
        UdpClient udp = new UdpClient(address.AddressFamily);
        try
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                tcp.Server.DualMode = false;
                udp.Client.DualMode = false;
            }
            tcp.Start();
            udp.Client.Bind(new IPEndPoint(address, 0));
            int tcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
            int udpPort = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            DateTime before = DateTime.Now;
            PortSnapshot snapshot = PortReader.Read();
            Check(snapshot != null && snapshot.Entries != null, "snapshot contract");
            Check(snapshot.CapturedAt >= before && snapshot.CapturedAt <= DateTime.Now, "snapshot capture timestamp");
            Equal<string>(null, snapshot.Error, "all live family reads succeed");
            using (Process own = Process.GetCurrentProcess())
            {
                PortEntry tcpRow = Find(snapshot, "TCP", tcpPort, own.Id);
                PortEntry udpRow = Find(snapshot, "UDP", udpPort, own.Id);
                foreach (PortEntry row in new PortEntry[] { tcpRow, udpRow })
                {
                    Check(Array.IndexOf(row.Addresses, address.ToString()) >= 0, "owned bind address");
                    Equal(own.ProcessName, row.ProcessName, "process metadata");
                    Equal<DateTime?>(own.StartTime.ToUniversalTime(), row.ProcessStartedAtUtc, "exact UTC process creation timestamp");
                    Equal(DateTimeKind.Utc, row.ProcessStartedAtUtc.Value.Kind, "UTC kind");
                    Check(!row.CanTerminate, "live scanner process protected");
                }
            }
        }
        finally { tcp.Stop(); udp.Close(); }
    }

    private static PortEntry Find(PortSnapshot snapshot, string protocol, int port, int pid)
    {
        foreach (PortEntry row in snapshot.Entries)
            if (row.Protocol == protocol && row.Port == port && row.ProcessId == pid) return row;
        throw new Exception("owned " + protocol + " ephemeral bind missing from snapshot");
    }
}
