using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeDrop.Cli.Commands;

public static class DevicesCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = Settings.Load();

        if (args.Length > 0)
        {
            string subCmd = args[0].ToLowerInvariant();

            if (subCmd == "add")
            {
                return AddDevice(settings, args.Skip(1).ToArray());
            }

            if (subCmd is "remove" or "rm" or "delete")
            {
                return RemoveDevice(settings, args.Skip(1).ToArray());
            }

            if (subCmd is "list" or "saved")
            {
                return ListSavedDevices(settings);
            }
        }

        bool scan = true;
        int timeoutSec = 2;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--timeout" or "-t" && i + 1 < args.Length && int.TryParse(args[++i], out int t))
            {
                timeoutSec = Math.Clamp(t, 1, 15);
            }
            else if (arg is "--no-scan" or "--saved")
            {
                scan = false;
            }
            else if (arg is "--scan" or "-s" or "scan")
            {
                scan = true;
            }
        }

        var results = new Dictionary<string, (string Name, string Ip, int Port, bool IsOnline, string Source)>(StringComparer.OrdinalIgnoreCase);

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

            Console.WriteLine($"  {statusColor}{statusIcon}{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}{item.Name,-20}{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{item.Ip,-18}{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{item.Port,-8}{ConsoleRenderer.Reset} {statusColor}{statusText} ({item.Source}){ConsoleRenderer.Reset}");
        }

        Console.WriteLine();
        return 0;
    }

    private static int AddDevice(Settings settings, string[] args)
    {
        if (args.Length < 2)
        {
            ConsoleRenderer.Error("Usage: devices add <name> <ip>[:port]");
            return 2;
        }

        string name = args[0];
        string target = args[1];
        string ip = target;
        int port = settings.Port;

        if (target.Contains(':'))
        {
            var parts = target.Split(':');
            ip = parts[0];
            if (int.TryParse(parts[1], out int p)) port = p;
        }

        if (string.IsNullOrWhiteSpace(ip))
        {
            ConsoleRenderer.Error("Invalid IP address.");
            return 2;
        }

        lock (settings.DevicesLock)
        {
            var existing = settings.Devices.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Ip, ip, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Name = name;
                existing.Ip = ip;
                existing.Port = port;
            }
            else
            {
                settings.Devices.Add(new DeviceItem
                {
                    Name = name,
                    Ip = ip,
                    Port = port
                });
            }

            settings.SavedDeviceNames[ip] = name;
            settings.Save();
        }

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new { status = "added", name, ip, port });
        }
        else
        {
            ConsoleRenderer.Success($"Device '{name}' ({ip}:{port}) saved.");
        }
        return 0;
    }

    private static int RemoveDevice(Settings settings, string[] args)
    {
        if (args.Length == 0)
        {
            ConsoleRenderer.Error("Usage: devices remove <name|ip>");
            return 2;
        }

        string query = args[0];
        bool removed = false;

        lock (settings.DevicesLock)
        {
            var found = settings.Devices.FirstOrDefault(d =>
                string.Equals(d.Name, query, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Ip, query, StringComparison.OrdinalIgnoreCase));

            if (found != null)
            {
                settings.Devices.Remove(found);
                settings.SavedDeviceNames.Remove(found.Ip);
                settings.Save();
                removed = true;
            }
        }

        if (removed)
        {
            if (ConsoleRenderer.JsonMode)
            {
                ConsoleRenderer.WriteJson(new { status = "removed", target = query });
            }
            else
            {
                ConsoleRenderer.Success($"Device '{query}' removed from saved devices.");
            }
            return 0;
        }

        ConsoleRenderer.Warning($"Device '{query}' not found in saved list.");
        return 1;
    }

    private static int ListSavedDevices(Settings settings)
    {
        var devices = settings.GetDevicesSnapshot();

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(devices.Select(d => new { name = d.Name, ip = d.Ip, port = d.Port }));
            return 0;
        }

        if (devices.Count == 0)
        {
            ConsoleRenderer.Info("No saved devices. Use 'devices add <name> <ip>' to add one.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.DarkMuted}{"DEVICE NAME",-22} {"IP ADDRESS",-18} {"PORT"}{ConsoleRenderer.Reset}");

        foreach (var d in devices)
        {
            Console.WriteLine($"  ★ {ConsoleRenderer.Bold}{d.Name,-20}{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{d.Ip,-18}{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{d.Port}{ConsoleRenderer.Reset}");
        }

        Console.WriteLine();
        return 0;
    }
}
