using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace EdgeDrop.Cli.Commands;

public static class PingCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = Settings.Load();
        string? target = args.FirstOrDefault(a => !a.StartsWith("-"));
        int port = settings.Port;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--port" or "-p" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
            {
                port = p;
            }
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            ConsoleRenderer.Error("Target required: ping <ip|name>");
            return 2;
        }

        string targetIp = target;
        if (target.Contains(':'))
        {
            var parts = target.Split(':');
            targetIp = parts[0];
            if (int.TryParse(parts[1], out int p)) port = p;
        }
        else
        {
            var dev = settings.Devices.FirstOrDefault(d => string.Equals(d.Name, target, StringComparison.OrdinalIgnoreCase));
            if (dev != null)
            {
                targetIp = dev.Ip;
                port = dev.Port > 0 ? dev.Port : port;
            }
        }

        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(targetIp, port, cts.Token);
            sw.Stop();

            ConsoleRenderer.Success($"Reply from {targetIp}:{port} · {sw.ElapsedMilliseconds} ms");
            ConsoleRenderer.WriteJson(new
            {
                target = targetIp,
                port,
                status = "online",
                latency_ms = sw.ElapsedMilliseconds
            });
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"Ping failed to {targetIp}:{port} · {ex.Message}");
            ConsoleRenderer.WriteJson(new
            {
                target = targetIp,
                port,
                status = "offline",
                error = ex.Message
            });
            return 1;
        }
    }
}
