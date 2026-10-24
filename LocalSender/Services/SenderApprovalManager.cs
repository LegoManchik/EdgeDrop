using System;
using System.Collections.Concurrent;

namespace EdgeDrop;
public static class SenderApprovalManager
{
    private static readonly ConcurrentDictionary<string, DateTime> _approved = new(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "";
        ip = ip.Trim();
        if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            ip = ip.Substring(7);
        if (ip == "::1" || ip.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            ip = "127.0.0.1";
        return ip;
    }

    public static void Approve(string? ip, TimeSpan? duration = null)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return;
        var exp = DateTime.UtcNow.Add(duration ?? TimeSpan.FromMinutes(5));
        _approved[norm] = exp;
        if (norm == "127.0.0.1")
        {
            _approved["::1"] = exp;
            _approved["localhost"] = exp;
        }
    }

    public static void Revoke(string? ip)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return;
        _approved.TryRemove(norm, out _);
        if (norm == "127.0.0.1")
        {
            _approved.TryRemove("::1", out _);
            _approved.TryRemove("localhost", out _);
        }
    }

    public static bool IsApproved(string? ip)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return false;
        if (_approved.TryGetValue(norm, out var validUntil) && DateTime.UtcNow < validUntil)
        {
            _approved[norm] = DateTime.UtcNow.AddMinutes(5);
            return true;
        }
        return false;
    }
}
