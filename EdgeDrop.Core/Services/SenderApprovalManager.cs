using System;
using System.Collections.Concurrent;

namespace EdgeDrop;

public static class SenderApprovalManager
{
    private static readonly ConcurrentDictionary<string, DateTime> _approvedUntil = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, int> _activeTransfers = new(StringComparer.OrdinalIgnoreCase);

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

        // Grace period for batch transfers: 6 seconds of idle, or specified custom duration
        var exp = DateTime.UtcNow.Add(duration ?? TimeSpan.FromSeconds(6));
        _approvedUntil[norm] = exp;

        if (norm == "127.0.0.1")
        {
            _approvedUntil["::1"] = exp;
            _approvedUntil["localhost"] = exp;
        }
    }

    public static void NotifyTransferStarted(string? ip)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return;

        _activeTransfers.AddOrUpdate(norm, 1, (_, count) => Math.Max(1, count + 1));
        if (norm == "127.0.0.1")
        {
            _activeTransfers["::1"] = _activeTransfers[norm];
            _activeTransfers["localhost"] = _activeTransfers[norm];
        }
    }

    public static void NotifyTransferEnded(string? ip)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return;

        _activeTransfers.AddOrUpdate(norm, 0, (_, count) => Math.Max(0, count - 1));

        // Re-arm grace period for consecutive files in a batch
        var exp = DateTime.UtcNow.AddSeconds(6);
        _approvedUntil[norm] = exp;

        if (norm == "127.0.0.1")
        {
            _activeTransfers["::1"] = _activeTransfers[norm];
            _activeTransfers["localhost"] = _activeTransfers[norm];
            _approvedUntil["::1"] = exp;
            _approvedUntil["localhost"] = exp;
        }
    }

    public static void Revoke(string? ip)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return;

        _approvedUntil.TryRemove(norm, out _);
        _activeTransfers.TryRemove(norm, out _);

        if (norm == "127.0.0.1")
        {
            _approvedUntil.TryRemove("::1", out _);
            _approvedUntil.TryRemove("localhost", out _);
            _activeTransfers.TryRemove("::1", out _);
            _activeTransfers.TryRemove("localhost", out _);
        }
    }

    public static bool IsApproved(string? ip)
    {
        var norm = NormalizeIp(ip);
        if (string.IsNullOrEmpty(norm)) return false;

        // If transfer from this sender is currently in progress, it's approved for this batch
        if (_activeTransfers.TryGetValue(norm, out var activeCount) && activeCount > 0)
        {
            return true;
        }

        // If within batch grace window
        if (_approvedUntil.TryGetValue(norm, out var validUntil) && DateTime.UtcNow < validUntil)
        {
            return true;
        }

        return false;
    }
}
