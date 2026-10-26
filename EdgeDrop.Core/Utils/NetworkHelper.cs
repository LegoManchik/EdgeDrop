using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EdgeDrop.Core.Utils;

public static class NetworkHelper
{
    private static readonly string[] VirtualKeywords = new[]
    {
        "virtual", "vmware", "virtualbox", "wsl", "hyper-v", "vethernet",
        "tap", "vpn", "radmin", "tailscale", "zerotier", "hamachi", "bluetooth"
    };

    /// <summary>
    /// Accurately detects the primary local network IP address of this machine,
    /// filtering out VPNs (Radmin, Tailscale, OpenVPN, Outline) and virtual adapters (WSL, VirtualBox, VMware, Hyper-V).
    /// </summary>
    public static string GetPrimaryLocalIp()
    {
        // 1. First probe via UDP socket routing table.
        // This queries the Windows routing table to see which local interface routes traffic to the default gateway.
        // (UDP connect does NOT send any network packets).
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                string ipStr = ep.Address.ToString();
                if (!IsInvalidOrVpnIp(ipStr))
                {
                    return ipStr;
                }
            }
        }
        catch { }

        // 2. Fallback heuristic scoring across active network interfaces
        try
        {
            var candidates = new List<(string ip, int score)>();

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var ni in interfaces)
            {
                string fullName = (ni.Name + " " + ni.Description).ToLowerInvariant();
                bool isVirtualOrVpn = VirtualKeywords.Any(kw => fullName.Contains(kw));

                var ipProps = ni.GetIPProperties();
                bool hasGateway = ipProps.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.ToString().StartsWith("0.0.0.0") &&
                    !g.Address.ToString().StartsWith("26.")); // Radmin default gateway

                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ua.Address)) continue;

                    string ipStr = ua.Address.ToString();
                    if (IsInvalidOrVpnIp(ipStr)) continue;

                    int score = 0;

                    if (hasGateway) score += 100;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 60;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 50;

                    if (ipStr.StartsWith("192.168.")) score += 40;
                    else if (ipStr.StartsWith("10.")) score += 20;
                    else if (ipStr.StartsWith("172.")) score += 10;

                    if (isVirtualOrVpn) score -= 150;

                    candidates.Add((ipStr, score));
                }
            }

            var best = candidates.OrderByDescending(c => c.score).FirstOrDefault();
            if (!string.IsNullOrEmpty(best.ip))
            {
                return best.ip;
            }
        }
        catch { }

        return "127.0.0.1";
    }

    /// <summary>
    /// Checks if the IP is link-local, loopback, or belongs to a known VPN range (e.g. Radmin 26.x.x.x, Tailscale 100.64.0.0/10).
    /// </summary>
    public static bool IsInvalidOrVpnIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return true;
        if (ip.StartsWith("127.")) return true;
        if (ip.StartsWith("169.254.")) return true; // Link-local APIPA
        if (ip.StartsWith("26.")) return true;      // Radmin VPN
        if (ip.StartsWith("100.64.") || ip.StartsWith("100.1")) return true; // Tailscale CGNAT

        return false;
    }

    /// <summary>
    /// Returns all valid non-virtual local IPv4 addresses.
    /// </summary>
    public static List<string> GetAllActiveLocalIps()
    {
        var list = new List<string>();
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            foreach (var ni in interfaces)
            {
                string fullName = (ni.Name + " " + ni.Description).ToLowerInvariant();
                if (VirtualKeywords.Any(kw => fullName.Contains(kw))) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IsInvalidOrVpnIp(ua.Address.ToString()))
                    {
                        list.Add(ua.Address.ToString());
                    }
                }
            }
        }
        catch { }

        if (list.Count == 0) list.Add("127.0.0.1");
        return list.Distinct().ToList();
    }
}
