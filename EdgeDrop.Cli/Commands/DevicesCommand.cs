using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeDrop.Cli.Commands;

public static class DevicesCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = Settings.Load();
        bool scan = true;
        int timeoutSec = 2;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--timeout" or "-t" && i + 1 < args.Length && int.TryParse(args[++i], out int t))
            {
                timeoutSec = Math.Clamp(t, 1, 15);
            }
            else if (arg is "--no-scan")
            {
                scan = false;
            }
            else if (arg is "--scan" or "-s")
            {
                scan = true;
            }
        }

        var results = new Dictionary<string, (string Name, string Ip, int Port, bool IsOnline, string Source)>(StringComparer.OrdinalIgnoreCase);

        // Add saved devices
        foreach (var dev in settings.Devices)
        {
            if (!string.IsNullOrWhiteSpace(dev.Ip))
            {
                results[dev.Ip] = (dev.Name, dev.Ip, dev.Port > 0 ? dev.Port : settings.Port, false, "Saved");
            }
        }

        if (scan)
        {
            ConsoleRenderer.Info($"Scanning local network ({timeoutSec}s)...");
            using var discovery = new DeviceDiscoveryService(settings);

            discovery.NewDeviceDiscovered += dev =>
            {
                lock (results)
                {
                    results[dev.Ip] = (dev.Name, dev.Ip, dev.Port, true, "Discovered");
                }
            };

            discovery.DeviceOnlineStatusChanged += dev =>
            {
                lock (results)
                {
                    results[dev.Ip] = (dev.Name, dev.Ip, dev.Port, dev.IsOnline, dev.IsOnline ? "Discovered" : "Offline");
                }
            };

            discovery.Start();
            await Task.Delay(TimeSpan.FromSeconds(timeoutSec));
            discovery.Stop();
        }

        if (ConsoleRenderer.JsonMode)
        {
            var jsonList = results.Values.Select(v => new
            {
                name = v.Name,
                ip = v.Ip,
                port = v.Port,
                online = v.IsOnline,
                source = v.Source
            }).ToList();

            ConsoleRenderer.WriteJson(jsonList);
            return 0;
        }

        if (results.Count == 0)
        {
            ConsoleRenderer.Warning("No devices found on local network.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.DarkMuted}{"DEVICE",-22} {"IP ADDRESS",-18} {"PORT",-8} {"STATUS"}{ConsoleRenderer.Reset}");

        foreach (var item in results.Values)
        {
            string statusIcon = item.IsOnline ? "●" : "○";
            string statusColor = item.IsOnline ? ConsoleRenderer.SuccessColor : ConsoleRenderer.DarkMuted;
            string statusText = item.IsOnline ? "Online" : "Offline";

            Console.WriteLine($"  {ConsoleRenderer.TextLight}{item.Name,-22}{ConsoleRenderer.Reset} {ConsoleRenderer.MutedColor}{item.Ip,-18}{ConsoleRenderer.Reset} {ConsoleRenderer.MutedColor}{item.Port,-8}{ConsoleRenderer.Reset} {statusColor}{statusIcon} {statusText}{ConsoleRenderer.Reset}");
        }

        Console.WriteLine();
        return 0;
    }
}
