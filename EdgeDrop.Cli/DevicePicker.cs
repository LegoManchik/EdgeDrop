using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeDrop.Cli;

public static class DevicePicker
{
    private class DeviceOption
    {
        public string Name { get; set; } = "";
        public string Ip { get; set; } = "";
        public int Port { get; set; }
        public bool IsOnline { get; set; }
        public string Source { get; set; } = "";
        public bool IsManual { get; set; }
    }

    public static async Task<string?> PickDeviceAsync(Settings settings)
    {
        if (Console.IsInputRedirected || ConsoleRenderer.JsonMode)
        {
            return null;
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.BrandLight}🔍 Scanning local network for EdgeDrop peers (1.5s)...{ConsoleRenderer.Reset}");

        var discovered = new Dictionary<string, DeviceOption>(StringComparer.OrdinalIgnoreCase);

        // Saved devices
        foreach (var dev in settings.Devices)
        {
            if (!string.IsNullOrWhiteSpace(dev.Ip))
            {
                discovered[dev.Ip] = new DeviceOption
                {
                    Name = dev.Name,
                    Ip = dev.Ip,
                    Port = dev.Port > 0 ? dev.Port : settings.Port,
                    IsOnline = false,
                    Source = "Saved"
                };
            }
        }

        // Live scan
        using (var discovery = new DeviceDiscoveryService(settings))
        {
            discovery.NewDeviceDiscovered += dev =>
            {
                lock (discovered)
                {
                    discovered[dev.Ip] = new DeviceOption
                    {
                        Name = dev.Name,
                        Ip = dev.Ip,
                        Port = dev.Port,
                        IsOnline = true,
                        Source = "Discovered"
                    };
                }
            };

            discovery.DeviceOnlineStatusChanged += dev =>
            {
                lock (discovered)
                {
                    discovered[dev.Ip] = new DeviceOption
                    {
                        Name = dev.Name,
                        Ip = dev.Ip,
                        Port = dev.Port,
                        IsOnline = dev.IsOnline,
                        Source = dev.IsOnline ? "Discovered" : "Offline"
                    };
                }
            };

            discovery.Start();
            await Task.Delay(1500);
            discovery.Stop();
        }

        var options = discovered.Values
            .OrderByDescending(d => d.IsOnline)
            .ThenBy(d => d.Name)
            .ToList();

        // Always add manual IP entry option
        options.Add(new DeviceOption
        {
            Name = "Enter IP address manually...",
            Ip = "",
            IsManual = true
        });

        Console.WriteLine($"  {ConsoleRenderer.Bold}Select destination device{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}(↑/↓ navigate, Enter select, Esc cancel){ConsoleRenderer.Reset}:");
        Console.WriteLine();

        int selectedIndex = 0;
        int initialCursorTop = Console.CursorTop;

        RenderOptions(options, selectedIndex, initialCursorTop);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.UpArrow)
            {
                selectedIndex = (selectedIndex - 1 + options.Count) % options.Count;
                RenderOptions(options, selectedIndex, initialCursorTop);
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                selectedIndex = (selectedIndex + 1) % options.Count;
                RenderOptions(options, selectedIndex, initialCursorTop);
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                // Move cursor to below options
                Console.SetCursorPosition(0, initialCursorTop + options.Count);
                Console.WriteLine();

                var chosen = options[selectedIndex];
                if (chosen.IsManual)
                {
                    Console.Write($"  {ConsoleRenderer.BrandLight}Enter target IP: {ConsoleRenderer.Reset}");
                    string? manualIp = Console.ReadLine()?.Trim();
                    return string.IsNullOrWhiteSpace(manualIp) ? null : manualIp;
                }

                return chosen.Port > 0 && chosen.Port != settings.Port
                    ? $"{chosen.Ip}:{chosen.Port}"
                    : chosen.Ip;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                Console.SetCursorPosition(0, initialCursorTop + options.Count);
                Console.WriteLine();
                return null;
            }
        }
    }

    private static void RenderOptions(List<DeviceOption> options, int selectedIndex, int startTop)
    {
        try
        {
            for (int i = 0; i < options.Count; i++)
            {
                Console.SetCursorPosition(0, startTop + i);
                Console.Write("\x1b[2K"); // Clear entire line

                var opt = options[i];
                bool isSelected = (i == selectedIndex);

                if (opt.IsManual)
                {
                    if (isSelected)
                    {
                        Console.Write($"    {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}❯ ✎ {opt.Name}{ConsoleRenderer.Reset}");
                    }
                    else
                    {
                        Console.Write($"      {ConsoleRenderer.MutedColor}✎ {opt.Name}{ConsoleRenderer.Reset}");
                    }
                }
                else
                {
                    string statusBullet = opt.IsOnline
                        ? $"{ConsoleRenderer.SuccessColor}●{ConsoleRenderer.Reset}"
                        : $"{ConsoleRenderer.DarkMuted}○{ConsoleRenderer.Reset}";

                    string ipPort = opt.Port > 0 ? $"{opt.Ip}:{opt.Port}" : opt.Ip;

                    if (isSelected)
                    {
                        Console.Write($"    {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}❯ {statusBullet} {opt.Name} {ConsoleRenderer.BrandLight}({ipPort}){ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}[{opt.Source}]{ConsoleRenderer.Reset}");
                    }
                    else
                    {
                        Console.Write($"      {statusBullet} {ConsoleRenderer.TextLight}{opt.Name}{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}({ipPort}) [{opt.Source}]{ConsoleRenderer.Reset}");
                    }
                }
            }
        }
        catch { }
    }
}
