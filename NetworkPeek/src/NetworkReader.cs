using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkPeek
{
    public sealed class AdapterInfo
    {
        public string Id, Name, Description, IPv4, SubnetMask, Gateway;
        public string[] DnsServers = new string[0];
        public string[] IPv4Addresses = new string[0];
        public int InterfaceIndex;
        public bool IsPhysical;
        public long DefaultRouteMetric = long.MaxValue;
    }

    public sealed class NetworkSnapshot
    {
        public AdapterInfo Primary;
        public AdapterInfo[] Adapters = new AdapterInfo[0];
        public string Error;
        public DateTime CapturedAt;

        public string ToReport()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("NetworkPeek - " + CapturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            text.AppendLine("Primary adapter: " + (Primary == null ? "none (no usable IPv4 gateway)" : Primary.Name));
            text.AppendLine("Internet reachability: not checked.");
            if (!String.IsNullOrEmpty(Error)) text.AppendLine("Warning: " + Error);
            if (Primary != null) AppendAdapter(text, Primary);
            text.AppendLine();
            text.AppendLine("Connected IPv4 adapters (" + (Adapters == null ? 0 : Adapters.Length) + "):");
            if (Adapters != null)
                foreach (AdapterInfo adapter in Adapters)
                    if (adapter != null) AppendAdapter(text, adapter);
            return text.ToString();
        }

        private static void AppendAdapter(StringBuilder text, AdapterInfo adapter)
        {
            text.AppendLine();
            text.AppendLine(adapter.Name + " [" + (adapter.IsPhysical ? "physical" : "virtual/other") + "]");
            text.AppendLine("  Description: " + adapter.Description);
            text.AppendLine("  ID: " + adapter.Id + "; interface index: " + adapter.InterfaceIndex);
            text.AppendLine("  IPv4: " + adapter.IPv4);
            text.AppendLine("  All IPv4: " + String.Join(", ", adapter.IPv4Addresses ?? new string[0]));
            text.AppendLine("  Subnet mask: " + adapter.SubnetMask);
            text.AppendLine("  Gateway: " + (String.IsNullOrEmpty(adapter.Gateway) ? "none (local only)" : adapter.Gateway));
            text.AppendLine("  DNS: " + (adapter.DnsServers == null || adapter.DnsServers.Length == 0 ? "none" : String.Join(", ", adapter.DnsServers)));
            text.AppendLine("  Effective IPv4 default route metric: " + (adapter.DefaultRouteMetric == long.MaxValue ? "unavailable" : adapter.DefaultRouteMetric.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public static class NetworkReader
    {
        public static NetworkSnapshot Read()
        {
            NetworkSnapshot snapshot = new NetworkSnapshot { CapturedAt = DateTime.Now };
            List<AdapterInfo> connected = new List<AdapterInfo>();
            List<DefaultRoute> routes = new List<DefaultRoute>();
            try { routes = ReadDefaultRoutes(); }
            catch (Exception ex) { AddError(snapshot, "IPv4 route metrics unavailable (" + ex.GetType().Name + ")."); }
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    try
                    {
                        if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                            continue;
                        IPInterfaceProperties properties = adapter.GetIPProperties();
                        List<string> addresses = new List<string>();
                        Dictionary<string, string> masks = new Dictionary<string, string>();
                        foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                            if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                addresses.Add(address.Address.ToString());
                                IPAddress mask = address.IPv4Mask;
                                masks[address.Address.ToString()] = mask == null ? null : mask.ToString();
                            }
                        if (SelectIPv4(addresses) == null) continue;

                        IPv4InterfaceProperties ipv4 = properties.GetIPv4Properties();
                        AdapterInfo info = new AdapterInfo { Id = adapter.Id, Name = adapter.Name,
                            Description = adapter.Description, IPv4Addresses = addresses.ToArray(),
                            InterfaceIndex = ipv4 == null ? 0 : ipv4.Index };
                        foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                            if (UsableIPv4(gateway.Address.ToString()) && !addresses.Contains(gateway.Address.ToString()))
                            { info.Gateway = gateway.Address.ToString(); break; }
                        foreach (DefaultRoute route in routes)
                            if (route.InterfaceIndex == info.InterfaceIndex && route.Metric < info.DefaultRouteMetric && !addresses.Contains(route.Gateway))
                            { info.Gateway = route.Gateway; info.DefaultRouteMetric = route.Metric; }
                        info.IPv4 = SelectIPv4(addresses, masks, info.Gateway, out info.SubnetMask);
                        List<string> dns = new List<string>();
                        foreach (IPAddress server in properties.DnsAddresses) dns.Add(server.ToString());
                        info.DnsServers = dns.ToArray();
                        info.IsPhysical = IsPhysicalAdapter(adapter, info.InterfaceIndex, snapshot);
                        // Recheck after reading properties in case the adapter disconnected mid-read.
                        if (adapter.OperationalStatus == OperationalStatus.Up) connected.Add(info);
                    }
                    catch (Exception ex) { AddError(snapshot, "An adapter could not be read (" + ex.GetType().Name + ")."); }
                }
            }
            catch (Exception ex) { AddError(snapshot, "Adapters could not be enumerated (" + ex.GetType().Name + ")."); }
            snapshot.Adapters = connected.ToArray();
            snapshot.Primary = SelectPrimary(snapshot.Adapters);
            return snapshot;
        }

        public static AdapterInfo SelectPrimary(IEnumerable<AdapterInfo> adapters)
        {
            AdapterInfo best = null;
            if (adapters == null) return null;
            foreach (AdapterInfo adapter in adapters)
            {
                if (adapter == null || !UsableIPv4(adapter.IPv4) || !UsableIPv4(adapter.Gateway) ||
                    adapter.IPv4 == adapter.Gateway || (adapter.IPv4Addresses != null && Array.IndexOf(adapter.IPv4Addresses, adapter.Gateway) >= 0))
                    continue;
                if (best == null || (adapter.IsPhysical && !best.IsPhysical) ||
                    (adapter.IsPhysical == best.IsPhysical && adapter.DefaultRouteMetric < best.DefaultRouteMetric))
                    best = adapter;
            }
            return best;
        }

        internal static string SelectIPv4(IEnumerable<string> addresses)
        {
            string first = null;
            foreach (string address in addresses)
            {
                if (!UsableIPv4(address)) continue;
                if (first == null) first = address;
                byte[] bytes = IPAddress.Parse(address).GetAddressBytes();
                if (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168)) return address;
            }
            return first;
        }

        internal static string SelectIPv4(IEnumerable<string> addresses, IDictionary<string, string> masks, string gateway, out string subnetMask)
        {
            string selected = null;
            if (masks != null && UsableIPv4(gateway))
            {
                byte[] nextHop = IPAddress.Parse(gateway).GetAddressBytes();
                foreach (string address in addresses)
                {
                    string value;
                    IPAddress mask;
                    if (!UsableIPv4(address) || !masks.TryGetValue(address, out value) ||
                        !IPAddress.TryParse(value, out mask) || mask.AddressFamily != AddressFamily.InterNetwork || mask.Equals(IPAddress.Any))
                        continue;
                    byte[] ip = IPAddress.Parse(address).GetAddressBytes();
                    byte[] bits = mask.GetAddressBytes();
                    bool matches = true;
                    for (int i = 0; i < 4; i++)
                        if ((ip[i] & bits[i]) != (nextHop[i] & bits[i])) { matches = false; break; }
                    if (matches) { selected = address; break; }
                }
            }
            if (selected == null) selected = SelectIPv4(addresses);
            subnetMask = null;
            if (selected != null && masks != null) masks.TryGetValue(selected, out subnetMask);
            return selected;
        }

        private static bool UsableIPv4(string value)
        {
            IPAddress address;
            if (!IPAddress.TryParse(value, out address) || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] != 0 && bytes[0] != 127 && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
        }

        private static void AddError(NetworkSnapshot snapshot, string message)
        {
            if (snapshot.Error == null) snapshot.Error = message;
            else if (!snapshot.Error.Contains(message)) snapshot.Error += " " + message;
        }

        private static bool IsPhysicalAdapter(NetworkInterface adapter, int index, NetworkSnapshot snapshot)
        {
            NetworkInterfaceType type = adapter.NetworkInterfaceType;
            if (type != NetworkInterfaceType.Ethernet && type != NetworkInterfaceType.Wireless80211 &&
                type != NetworkInterfaceType.GigabitEthernet && type != NetworkInterfaceType.FastEthernetFx &&
                type != NetworkInterfaceType.FastEthernetT) return false;
            try
            {
                InterfaceRow row = new InterfaceRow { InterfaceIndex = (uint)index };
                if (GetIfEntry2(ref row) == 0) return (row.Flags & 1) != 0;
            }
            catch (Exception) { }
            AddError(snapshot, "Hardware classification unavailable; adapter type/name fallback used.");
            string label = (adapter.Name + " " + adapter.Description).ToLowerInvariant();
            foreach (string marker in new[] { "virtual", "vethernet", "hyper-v", "hyperv", "wsl", "tailscale", "vpn", "wireguard", "tunnel", "tap-", "vmware" })
                if (label.Contains(marker)) return false;
            return true;
        }

        private static List<DefaultRoute> ReadDefaultRoutes()
        {
            List<DefaultRoute> routes = new List<DefaultRoute>();
            uint size = 0;
            uint result = GetIpForwardTable(IntPtr.Zero, ref size, false);
            if (result == 232) return routes; // ERROR_NO_DATA
            if (result != 0 && result != 122) throw new Win32Exception((int)result);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int capacity = checked((int)size);
                IntPtr buffer = Marshal.AllocHGlobal(capacity);
                try
                {
                    result = GetIpForwardTable(buffer, ref size, false);
                    if (result == 122) continue; // Table grew while being read.
                    if (result == 232) return routes;
                    if (result != 0) throw new Win32Exception((int)result);
                    if (capacity < 4) throw new InvalidOperationException("Invalid route table size.");
                    int count = Marshal.ReadInt32(buffer);
                    int rowSize = Marshal.SizeOf(typeof(ForwardRow));
                    if (count < 0 || 4L + (long)count * rowSize > capacity) throw new InvalidOperationException("Invalid route table length.");
                    for (int i = 0; i < count; i++)
                    {
                        ForwardRow row = (ForwardRow)Marshal.PtrToStructure(IntPtr.Add(buffer, 4 + i * rowSize), typeof(ForwardRow));
                        if (row.Destination != 0 || row.Mask != 0 || row.Type != 4 || row.Metric1 == uint.MaxValue) continue;
                        string gateway = new IPAddress(BitConverter.GetBytes(row.NextHop)).ToString();
                        if (UsableIPv4(gateway)) routes.Add(new DefaultRoute { InterfaceIndex = (int)row.InterfaceIndex,
                            Gateway = gateway, Metric = row.Metric1 });
                    }
                    return routes;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new InvalidOperationException("The IPv4 routing table kept changing.");
        }

        private sealed class DefaultRoute
        {
            public int InterfaceIndex;
            public string Gateway;
            public long Metric;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ForwardRow
        {
            public uint Destination, Mask, Policy, NextHop, InterfaceIndex, Type, Protocol, Age, NextHopAS;
            // GetIpForwardTable Metric1 already includes the interface metric on Vista+.
            public uint Metric1, Metric2, Metric3, Metric4, Metric5;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct InterfaceRow
        {
            public ulong InterfaceLuid;
            public uint InterfaceIndex;
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
            public uint PhysicalAddressLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
            public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
            public byte Flags;
            public uint OperStatus, AdminStatus, MediaConnectState;
            public Guid NetworkGuid;
            public uint ConnectionType;
            public ulong TransmitLinkSpeed, ReceiveLinkSpeed;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)] public ulong[] Counters;
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetIpForwardTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetIfEntry2(ref InterfaceRow row);
    }
}
