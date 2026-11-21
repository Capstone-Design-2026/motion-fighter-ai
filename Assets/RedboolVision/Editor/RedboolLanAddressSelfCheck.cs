using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

/// <summary>Deterministic address policy; does not enumerate or contact a real network.</summary>
public static class RedboolLanAddressSelfCheck
{
    static readonly List<string> checks = new List<string>();
    static void Check(bool ok, string message) { checks.Add((ok ? "PASS " : "FAIL ") + message); if (!ok) throw new Exception(message); }
    static RedboolLanAddress.InterfaceRecord Adapter(string name, string description, NetworkInterfaceType type, params string[] addresses)
        => new RedboolLanAddress.InterfaceRecord { Name = name, Description = description, Type = type, IsUp = true, Addresses = addresses };
    public static void Run()
    {
        checks.Clear();
        var wifi = Adapter("Wi-Fi", "Physical wireless", NetworkInterfaceType.Wireless80211, "192.168.2.51", "2001:db8::1");
        var hotspot = Adapter("Local Area Connection* 10", "Microsoft Wi-Fi Direct Virtual Adapter", NetworkInterfaceType.Wireless80211, "192.168.137.1");
        var vm = Adapter("VirtualBox Host-Only Network", "VirtualBox Host-Only Ethernet Adapter", NetworkInterfaceType.Ethernet, "192.168.56.1");
        var macClat = Adapter("en0", "Wi-Fi", NetworkInterfaceType.Wireless80211, "192.0.0.2", "2001:db8::2");
        var result = RedboolLanAddress.Evaluate(new[] { macClat });
        Check(result.Addresses.Length == 0 && result.TranslationOnly, "Actual Mac CLAT192.0.0.2 is not advertised as a peer address");
        for (int n = 0; n < 8; n++)
        {
            result = RedboolLanAddress.Evaluate(new[] { Adapter("en0", "", NetworkInterfaceType.Wireless80211, "192.0.0." + n) });
            Check(result.Addresses.Length == 0 && result.TranslationOnly, "Complete RFC7335 service prefix excluded: " + n);
        }
        result = RedboolLanAddress.Evaluate(new[] { wifi, hotspot, vm });
        Check(result.Addresses.Length == 2 && Array.IndexOf(result.Addresses, "192.168.137.1") >= 0 && Array.IndexOf(result.Addresses, "192.168.2.51") >= 0,
            "Physical Wi-Fi and gateway-free Wi-Fi Direct hotspot both remain candidates, host-only VM does not");
        Check(Array.Exists(result.Labels, x => x.Contains("핫스팟")) && result.Labels.Length == result.Addresses.Length, "Multiple interfaces have aligned distinguishing labels");
        result = RedboolLanAddress.Evaluate(new[] { macClat, wifi });
        Check(result.Addresses.Length == 1 && !result.TranslationOnly, "A real LAN candidate takes precedence over translation-only guidance");
        var down = Adapter("Ethernet", "", NetworkInterfaceType.Ethernet, "10.1.0.3"); down.IsUp = false;
        result = RedboolLanAddress.Evaluate(new[] { down });
        Check(result.Addresses.Length == 0 && !result.TranslationOnly, "Disconnected interface is not advertised");
        result = RedboolLanAddress.Evaluate(new[] { Adapter("en0", "", NetworkInterfaceType.Wireless80211, "2001:db8::2", "fe80::1") });
        Check(result.Addresses.Length == 0 && !result.TranslationOnly, "IPv6-only without CLAT uses ordinary unavailable-IPv4 guidance");
        foreach (var ip in new[] { "0.0.0.0", "0.1.2.3", "127.0.0.1", "127.4.3.2", "169.254.20.1", "224.0.0.1", "239.1.2.3", "240.0.0.1", "255.255.255.255", "not an IP", "" })
            Check(RedboolLanAddress.Evaluate(new[] { Adapter("en0", "", NetworkInterfaceType.Wireless80211, ip) }).Addresses.Length == 0, "Non-shareable address rejected: " + ip);
        foreach (var adapter in new[] { Adapter("lo0", "", NetworkInterfaceType.Loopback, "10.0.0.1"),
            Adapter("utun3", "", NetworkInterfaceType.Unknown, "10.0.0.2"), Adapter("VPN", "WireGuard Tunnel", NetworkInterfaceType.Ethernet, "10.0.0.3"),
            Adapter("TAP", "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet, "10.0.0.4"), vm,
            Adapter("vboxnet0", "vboxnet0", NetworkInterfaceType.Ethernet, "192.168.56.1"),
            Adapter("vmnet1", "vmnet1", NetworkInterfaceType.Ethernet, "192.168.56.2") })
            Check(RedboolLanAddress.Evaluate(new[] { adapter }).Addresses.Length == 0, "Obvious non-LAN adapter rejected: " + adapter.Name);
        result = RedboolLanAddress.Evaluate(new[] { Adapter("Virtual Wi-Fi", "Microsoft virtual network", NetworkInterfaceType.Wireless80211, "10.0.0.5") });
        Check(result.Addresses.Length == 1, "Virtual word alone never excludes a usable interface");
        result = RedboolLanAddress.Evaluate(new[] { Adapter("Local Area Connection* 11", "Microsoft Wi-Fi Direct Virtual Adapter #2", NetworkInterfaceType.Unknown, "192.168.137.1") });
        Check(result.Addresses.Length == 1, "Wi-Fi Direct remains eligible when the OS cannot identify its interface type");
        result = RedboolLanAddress.Evaluate(new[] { Adapter("Ethernet", "USB physical", NetworkInterfaceType.Ethernet, "10.0.0.6") });
        Check(result.Addresses.Length == 1, "Up physical IPv4 is eligible without a gateway requirement");
        result = RedboolLanAddress.Evaluate(new[] { Adapter("en0", "en0", NetworkInterfaceType.Ethernet, "192.168.2.180") });
        Check(result.Labels[0].Contains("en0") && !result.Labels[0].Contains("유선"), "macOS Mono Ethernet classification does not mislabel Wi-Fi as wired");
        var first = RedboolLanAddress.Evaluate(new[] { wifi, hotspot, wifi });
        var second = RedboolLanAddress.Evaluate(new[] { hotspot, wifi });
        Check(string.Join("|", first.Addresses) == string.Join("|", second.Addresses) && string.Join("|", first.Labels) == string.Join("|", second.Labels), "Duplicate removal and order do not depend on enumeration order");
        string[] originalAddresses = { "192.168.1.2" }, originalLabels = { "Wi-Fi" };
        var immutable = new RedboolLanAddress.Snapshot(originalAddresses, originalLabels);
        originalAddresses[0] = "bad"; originalLabels[0] = "bad";
        var returned = immutable.Addresses; returned[0] = "bad"; var returnedLabels = immutable.Labels; returnedLabels[0] = "bad";
        Check(immutable.Addresses[0] == "192.168.1.2" && immutable.Labels[0] == "Wi-Fi", "Snapshot owns input arrays and does not leak writable output arrays");
        result = RedboolLanAddress.Evaluate(new[] { wifi }, "NETWORK_ENUMERATION_PARTIAL");
        Check(result.Addresses.Length == 1 && result.Error == "NETWORK_ENUMERATION_PARTIAL", "Partial enumeration keeps obtained candidates and a bounded error code");
        result = RedboolLanAddress.Evaluate(null);
        Check(result.Addresses.Length == 0 && result.Labels.Length == 0 && result.Error == "" && !result.TranslationOnly, "No interfaces has an ordinary empty result");
        string output = Environment.GetEnvironmentVariable("REDBOOL_LAN_ADDRESS_CHECK_OUTPUT");
        if (!string.IsNullOrEmpty(output)) System.IO.File.WriteAllLines(output, checks);
        string summary = "REDBOOL LAN ADDRESS PASS " + checks.Count + "; deterministic policy only, no interface enumeration, DNS or packets";
#if UNITY_EDITOR
        UnityEngine.Debug.Log(summary);
#else
        Console.WriteLine(summary);
#endif
    }
}
