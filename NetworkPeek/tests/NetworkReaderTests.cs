using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using NetworkPeek;

internal static class NetworkReaderTests
{
    private static int passed;
    private static int failed;
    private static NetworkSnapshot live;

    private static int Main(string[] args)
    {
        Run("No adapters returns null", delegate {
            Assert(NetworkReader.SelectPrimary(new AdapterInfo[0]) == null, "Empty input selected an adapter.");
            Assert(NetworkReader.SelectPrimary(null) == null, "Null input selected an adapter.");
        });
        Run("Physical gateway beats Tailscale WSL and Hyper-V", delegate {
            AdapterInfo lan = Adapter("Ethernet", true, "192.168.50.20", "192.168.50.1", 500);
            Assert(NetworkReader.SelectPrimary(new[] {
                Adapter("Tailscale", false, "100.100.10.20", "100.100.10.1", 1),
                Adapter("WSL", false, "172.28.10.2", "172.28.10.1", 2),
                Adapter("Hyper-V", false, "172.29.10.2", "172.29.10.1", 3), lan }) == lan,
                "A virtual adapter displaced the physical LAN.");
        });
        Run("Lowest effective physical route metric wins", delegate {
            AdapterInfo wifi = Adapter("Wi-Fi", true, "192.168.50.21", "192.168.50.1", 25);
            AdapterInfo ethernet = Adapter("Ethernet", true, "10.42.0.20", "10.42.0.1", 10);
            Assert(NetworkReader.SelectPrimary(new[] { wifi, ethernet }) == ethernet, "Lowest metric lost.");
            Assert(NetworkReader.SelectPrimary(new[] { ethernet, wifi }) == ethernet, "Enumeration order changed the winner.");
            wifi.DefaultRouteMetric = 5;
            Assert(NetworkReader.SelectPrimary(new[] { ethernet, wifi }) == wifi, "Wi-Fi could not win on metric.");
        });
        Run("Only VPN with a real gateway is a fallback", delegate {
            AdapterInfo vpn = Adapter("VPN", false, "10.77.0.2", "10.77.0.1", 20);
            Assert(NetworkReader.SelectPrimary(new[] { vpn }) == vpn, "VPN fallback was discarded.");
            Assert(NetworkReader.SelectPrimary(new[] {
                Adapter("Ethernet", true, "192.168.50.20", null, 1), vpn }) == vpn,
                "A LAN without a gateway displaced the VPN gateway.");
        });
        Run("No gateway means no primary", delegate {
            Assert(NetworkReader.SelectPrimary(new[] {
                Adapter("LAN", true, "192.168.50.20", null, 0),
                Adapter("WSL", false, "172.28.10.2", "", 0) }) == null,
                "A local-only adapter became primary.");
        });
        Run("APIPA loopback IPv6 and invalid IPv4 are excluded", delegate {
            foreach (string ip in new[] { "169.254.4.5", "127.20.30.40", "::1", "fe80::1", "0.0.0.0", "224.0.0.1", "255.255.255.255", "bad", null })
                Assert(NetworkReader.SelectPrimary(new[] { Adapter("Bad", true, ip, "192.168.50.1", 1) }) == null,
                    "An unusable IPv4 became primary.");
        });
        Run("Invalid and self gateways are excluded", delegate {
            foreach (string gateway in new[] { "0.0.0.0", "::1", "127.0.0.2", "169.254.4.1", "224.0.0.1", "bad", "192.168.50.20" })
                Assert(NetworkReader.SelectPrimary(new[] { Adapter("Bad", true, "192.168.50.20", gateway, 1) }) == null,
                    "An unusable gateway became primary.");
        });
        Run("Known metric beats unknown metric", delegate {
            AdapterInfo known = Adapter("Known", true, "10.42.0.20", "10.42.0.1", 90);
            AdapterInfo unknown = Adapter("Unknown", true, "192.168.50.20", "192.168.50.1", long.MaxValue);
            Assert(NetworkReader.SelectPrimary(new[] { unknown, known }) == known, "Unknown metric ranked first.");
            Assert(NetworkReader.SelectPrimary(new[] { unknown }) == unknown, "Unknown metric removed a valid gateway.");
        });
        Run("Null entries are ignored", delegate {
            AdapterInfo lan = Adapter("LAN", true, "10.42.0.20", "10.42.0.1", 10);
            Assert(NetworkReader.SelectPrimary(new[] { null, lan, null }) == lan, "Null entries broke selection.");
        });
        Run("Multiple IPv4 prefer private LAN over other addresses", delegate {
            Assert(NetworkReader.SelectIPv4(new[] { "169.254.4.5", "100.100.10.20", "192.168.50.20", "10.42.0.20" }) == "192.168.50.20",
                "The first usable private LAN address was not chosen.");
            Assert(NetworkReader.SelectIPv4(new[] { "127.0.0.1", "169.254.4.5", "::1" }) == null, "Unusable-only addresses were accepted.");
            Assert(NetworkReader.SelectIPv4(new[] { "100.100.10.20" }) == "100.100.10.20", "VPN IPv4 fallback was lost.");
        });
        Run("Gateway subnet wins in either alias order with its own mask", delegate {
            Dictionary<string, string> masks = new Dictionary<string, string> {
                { "10.42.0.20", "255.255.0.0" }, { "192.168.50.20", "255.255.255.0" } };
            string mask;
            Assert(NetworkReader.SelectIPv4(new[] { "10.42.0.20", "192.168.50.20" }, masks, "192.168.50.1", out mask) == "192.168.50.20",
                "An unrelated private alias displaced the gateway subnet.");
            Assert(mask == "255.255.255.0", "Selected alias has the wrong mask.");
            Assert(NetworkReader.SelectIPv4(new[] { "192.168.50.20", "10.42.0.20" }, masks, "192.168.50.1", out mask) == "192.168.50.20",
                "Reversing aliases changed the gateway subnet choice.");
            Assert(mask == "255.255.255.0", "Reversed aliases lost the matching mask.");
            Assert(NetworkReader.SelectIPv4(new[] { "192.168.50.20", "10.42.0.20" }, masks, "10.42.1.1", out mask) == "10.42.0.20",
                "Subnet matching ignored the assigned /16 mask.");
            Assert(mask == "255.255.0.0", "The /16 alias has the wrong mask.");
        });
        Run("No matching subnet or gateway preserves existing IPv4 fallback", delegate {
            string[] aliases = new[] { "10.42.0.20", "192.168.50.20" };
            Dictionary<string, string> masks = new Dictionary<string, string> {
                { "10.42.0.20", "255.255.0.0" }, { "192.168.50.20", "255.255.255.0" } };
            foreach (string gateway in new[] { "172.28.10.1", null, "bad", "::1" })
            {
                string mask;
                Assert(NetworkReader.SelectIPv4(aliases, masks, gateway, out mask) == "10.42.0.20", "Existing private IPv4 fallback changed.");
                Assert(mask == "255.255.0.0", "Fallback did not retain its own mask.");
            }
        });
        Run("Missing or unusable masks preserve fallback and matching ignores APIPA", delegate {
            string[] aliases = new[] { "10.42.0.20", "192.168.50.20" };
            string mask;
            Assert(NetworkReader.SelectIPv4(aliases, null, "192.168.50.1", out mask) == "10.42.0.20" && mask == null,
                "Missing masks did not use the existing fallback.");
            Dictionary<string, string> masks = new Dictionary<string, string> { { "10.42.0.20", "255.255.0.0" } };
            foreach (string missing in new[] { null, "bad", "::", "0.0.0.0" })
            {
                masks["192.168.50.20"] = missing;
                Assert(NetworkReader.SelectIPv4(aliases, masks, "192.168.50.1", out mask) == "10.42.0.20" && mask == "255.255.0.0",
                    "An unusable mask displaced the fallback.");
            }
            masks = new Dictionary<string, string> { { "169.254.4.5", "128.0.0.0" }, { "192.168.50.20", "255.255.255.0" } };
            Assert(NetworkReader.SelectIPv4(new[] { "169.254.4.5", "192.168.50.20" }, masks, "192.168.50.1", out mask) == "192.168.50.20" && mask == "255.255.255.0",
                "Subnet preference accepted an unusable APIPA alias.");
        });
        Run("Report includes primary and every connected adapter", delegate {
            AdapterInfo lan = Adapter("Fixture LAN", true, "192.168.50.20", "192.168.50.1", 25);
            lan.SubnetMask = "255.255.255.0";
            lan.DnsServers = new[] { "192.168.50.1", "10.42.0.53" };
            lan.IPv4Addresses = new[] { "192.168.50.20", "10.42.0.20", "169.254.4.5" };
            AdapterInfo local = Adapter("Fixture WSL", false, "172.28.10.2", null, long.MaxValue);
            string report = new NetworkSnapshot { Primary = lan, Adapters = new[] { lan, local }, CapturedAt = new DateTime(2026, 1, 2, 3, 4, 5) }.ToReport();
            foreach (string text in new[] { "Primary", "Fixture LAN", "Fixture WSL", "192.168.50.20", "10.42.0.20", "169.254.4.5", "255.255.255.0", "192.168.50.1", "10.42.0.53", "172.28.10.2", "25", "2026" })
                Assert(report.Contains(text), "The report omitted " + text + ".");
            Assert(report.ToLowerInvariant().Contains("local only"), "No-gateway detail was not identified as local only.");
        });
        Run("Empty and partial reports are useful", delegate {
            NetworkSnapshot snapshot = new NetworkSnapshot();
            Assert(snapshot.Adapters.Length == 0, "Adapters should start empty.");
            Assert(new AdapterInfo().DnsServers.Length == 0 && new AdapterInfo().IPv4Addresses.Length == 0, "Address arrays should start empty.");
            Assert(new AdapterInfo().DefaultRouteMetric == long.MaxValue, "Unknown metric default is wrong.");
            snapshot.Error = "Adapter unavailable";
            string report = snapshot.ToReport();
            Assert(report.Contains("Primary") && report.Contains("none") && report.Contains(snapshot.Error), "An empty/partial report lost its status.");
        });
        Run("Actual Read filters disconnected and unusable adapters", delegate {
            live = NetworkReader.Read();
            Assert(live != null && live.Adapters != null, "Read did not return a snapshot.");
            Dictionary<string, NetworkInterface> actual = new Dictionary<string, NetworkInterface>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces()) actual[adapter.Id] = adapter;
            foreach (AdapterInfo adapter in live.Adapters)
            {
                Assert(actual.ContainsKey(adapter.Id), "Read returned an absent adapter.");
                Assert(actual[adapter.Id].OperationalStatus == OperationalStatus.Up, "Read returned a disconnected adapter.");
                Assert(Usable(adapter.IPv4), "Read returned an unusable IPv4.");
                Assert(Array.IndexOf(adapter.IPv4Addresses, adapter.IPv4) >= 0, "Full IPv4 list omitted the selected address.");
                Assert(adapter.DnsServers != null, "Read left DNS null.");
                IPInterfaceProperties properties = actual[adapter.Id].GetIPProperties();
                foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Assert(Array.IndexOf(adapter.IPv4Addresses, ip.Address.ToString()) >= 0, "Full IPv4 list omitted an assigned address.");
                        if (ip.Address.ToString() == adapter.IPv4)
                            Assert(adapter.SubnetMask == ip.IPv4Mask.ToString(), "Selected IPv4 and subnet mask do not correspond.");
                    }
            }
            Assert(live.CapturedAt > DateTime.Now.AddMinutes(-1) && live.CapturedAt <= DateTime.Now, "Capture timestamp is missing.");
            Assert(live.Primary == NetworkReader.SelectPrimary(live.Adapters), "Read and primary selection disagree.");
            if (live.Primary != null) Assert(Usable(live.Primary.Gateway), "Live primary has no IPv4 gateway.");
        });

        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed.");
        if (failed != 0) return 1;
        if (args.Length == 2 && args[0] == "--snapshot") SavePrivateReport(args[1]);
        return 0;
    }

    private static AdapterInfo Adapter(string name, bool physical, string ip, string gateway, long metric)
    {
        return new AdapterInfo { Id = name, Name = name, Description = name + " description", IPv4 = ip,
            IPv4Addresses = new[] { ip }, Gateway = gateway, IsPhysical = physical, DefaultRouteMetric = metric };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + name + ": " + ex.Message); }
    }

    private static bool Usable(string value)
    {
        IPAddress address;
        if (!IPAddress.TryParse(value, out address) || address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] != 0 && b[0] != 127 && b[0] < 224 && !(b[0] == 169 && b[1] == 254);
    }

    private static string PrivateAddress(string value)
    {
        IPAddress address;
        if (!IPAddress.TryParse(value, out address) || address.AddressFamily != AddressFamily.InterNetwork) return "[non-local address omitted]";
        byte[] b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||
            (b[0] == 169 && b[1] == 254) ? value : "[non-local address omitted]";
    }

    private static void SavePrivateReport(string path)
    {
        NetworkSnapshot copy = new NetworkSnapshot { CapturedAt = live.CapturedAt,
            Error = live.Error == null ? null : "Some network information was unavailable." };
        List<AdapterInfo> adapters = new List<AdapterInfo>();
        foreach (AdapterInfo item in live.Adapters)
        {
            AdapterInfo sanitized = new AdapterInfo { Id = "adapter-" + (adapters.Count + 1),
                Name = "Adapter " + (adapters.Count + 1), Description = "(omitted for privacy)",
                IPv4 = PrivateAddress(item.IPv4), SubnetMask = item.SubnetMask,
                Gateway = String.IsNullOrEmpty(item.Gateway) ? null : PrivateAddress(item.Gateway),
                IPv4Addresses = Array.ConvertAll(item.IPv4Addresses, PrivateAddress),
                DnsServers = Array.ConvertAll(item.DnsServers, PrivateAddress),
                InterfaceIndex = item.InterfaceIndex, IsPhysical = item.IsPhysical, DefaultRouteMetric = item.DefaultRouteMetric };
            adapters.Add(sanitized);
            if (item == live.Primary) copy.Primary = sanitized;
        }
        copy.Adapters = adapters.ToArray();
        File.WriteAllText(path, copy.ToReport(), new UTF8Encoding(false));
        Console.WriteLine("LIVE SNAPSHOT: " + live.Adapters.Length + " connected adapter(s); primary=" +
            (live.Primary == null ? "none" : live.Primary.IsPhysical ? "physical" : "virtual") +
            "; effective metric=" + (live.Primary == null || live.Primary.DefaultRouteMetric == long.MaxValue ? "unknown" : live.Primary.DefaultRouteMetric.ToString()) +
            "; error=" + (live.Error == null ? "none" : "present") + "; private report saved.");
    }
}
