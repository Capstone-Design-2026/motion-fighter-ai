using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

/// <summary>Local interface candidates only; never DNS, discovery, joining, or a reachability promise.</summary>
public static class RedboolLanAddress
{
    public sealed class Snapshot
    {
        readonly string[] addresses, labels;
        public string[] Addresses => (string[])addresses.Clone();
        public string[] Labels => (string[])labels.Clone();
        public bool TranslationOnly { get; private set; }
        public string Error { get; private set; }
        public Snapshot(string[] addresses, string[] labels, bool translationOnly = false, string error = "")
        {
            addresses = addresses ?? new string[0]; labels = labels ?? new string[0];
            if (addresses.Length != labels.Length) throw new ArgumentException("Address labels must stay aligned.");
            this.addresses = (string[])addresses.Clone(); this.labels = (string[])labels.Clone();
            TranslationOnly = translationOnly; Error = error ?? "";
        }
    }

    // Plain input facts keep address policy testable without depending on the
    // developer's current network, interface order, gateway, or platform APIs.
    public sealed class InterfaceRecord
    {
        public string Name = "", Description = "";
        public NetworkInterfaceType Type = NetworkInterfaceType.Unknown;
        public bool IsUp;
        public string[] Addresses = new string[0];
    }

    public static Snapshot Capture()
    {
        var records = new List<InterfaceRecord>(); bool partial = false;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    var record = new InterfaceRecord { Name = adapter.Name, Description = adapter.Description,
                        Type = adapter.NetworkInterfaceType, IsUp = true };
                    if (Excluded(record)) continue;
                    var addresses = new List<string>();
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                        if (unicast.Address != null) addresses.Add(unicast.Address.ToString());
                    record.Addresses = addresses.ToArray(); records.Add(record);
                }
                catch (Exception) { partial = true; }
            }
            return Evaluate(records, partial ? "NETWORK_ENUMERATION_PARTIAL" : "");
        }
        catch (Exception) { return Evaluate(records, "NETWORK_ENUMERATION_FAILED"); }
    }

    public static Snapshot Evaluate(IEnumerable<InterfaceRecord> interfaces, string error = "")
    {
        var candidates = new SortedDictionary<string, string>(StringComparer.Ordinal);
        bool translation = false;
        if (interfaces != null) foreach (var adapter in interfaces)
        {
            if (adapter == null || !adapter.IsUp || Excluded(adapter)) continue;
            foreach (string value in adapter.Addresses ?? new string[0])
            {
                IPAddress address;
                if (!IPAddress.TryParse(value, out address) || address.AddressFamily != AddressFamily.InterNetwork) continue;
                byte[] b = address.GetAddressBytes();
                // RFC7335: IPv4 service continuity/CLAT addresses are not a
                // shareable peer LAN address, even though IPv4 sockets can bind.
                if (b[0] == 192 && b[1] == 0 && b[2] == 0 && b[3] < 8) { translation = true; continue; }
                if (b[0] == 0 || b[0] == 127 || b[0] >= 224 || (b[0] == 169 && b[1] == 254)) continue;
                string canonical = address.ToString(), label = Label(adapter);
                // Duplicate assignments do not create duplicate copy buttons;
                // select labels deterministically rather than interface order.
                string previous;
                if (!candidates.TryGetValue(canonical, out previous) || StringComparer.Ordinal.Compare(label, previous) < 0)
                    candidates[canonical] = label;
            }
        }
        var addresses = new List<string>(); var labels = new List<string>();
        foreach (var pair in candidates) { addresses.Add(pair.Key); labels.Add(pair.Value); }
        return new Snapshot(addresses.ToArray(), labels.ToArray(), translation && addresses.Count == 0, error);
    }

    static bool Has(string value, string token) => (value ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    static bool WifiDirect(InterfaceRecord adapter) => Has(adapter.Name, "Wi-Fi Direct") || Has(adapter.Description, "Wi-Fi Direct")
        || Has(adapter.Name, "WiFi Direct") || Has(adapter.Description, "WiFi Direct");
    static bool NumberedTunnel(string name, string prefix)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || name.Length == prefix.Length) return false;
        for (int i = prefix.Length; i < name.Length; i++) if (name[i] < '0' || name[i] > '9') return false;
        return true;
    }
    static bool Excluded(InterfaceRecord adapter)
    {
        if (adapter.Type == NetworkInterfaceType.Loopback) return true;
        // A mobile-hotspot adapter is deliberately virtual and may have no
        // gateway. Never reject it merely for either of those properties.
        if (WifiDirect(adapter)) return false;
        if (adapter.Type == NetworkInterfaceType.Tunnel || adapter.Type == NetworkInterfaceType.Ppp) return true;
        string name = adapter.Name ?? "", description = adapter.Description ?? "";
        if (NumberedTunnel(name, "utun") || NumberedTunnel(name, "tun") || NumberedTunnel(name, "tap")
            || NumberedTunnel(name, "vboxnet") || string.Equals(name, "vmnet1", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string marker in new[] { "VirtualBox Host-Only", "VMware Network Adapter VMnet1", "WireGuard Tunnel",
            "Wintun", "TAP-Windows Adapter", "Tailscale", "ZeroTier", "Hamachi" })
            if (Has(name, marker) || Has(description, marker)) return true;
        return false;
    }
    static string Label(InterfaceRecord adapter)
    {
        // Mono may identify macOS Wi-Fi en0 as Ethernet. Do not claim a wired
        // connection based on that category; retain the interface name instead.
        string kind = WifiDirect(adapter) ? "Windows 모바일 핫스팟" : adapter.Type == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "네트워크";
        string name = (adapter.Name ?? "").Trim();
        var clean = new System.Text.StringBuilder();
        foreach (char c in name) { if (!char.IsControl(c)) clean.Append(c); if (clean.Length >= 40) break; }
        return clean.Length == 0 || string.Equals(clean.ToString(), kind, StringComparison.OrdinalIgnoreCase)
            ? kind : kind + " · " + clean;
    }
}
