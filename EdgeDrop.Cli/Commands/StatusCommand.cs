using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EdgeDrop.Cli.Commands;

public static class StatusCommand
{
    public static int Run(string[] args)
    {
        var settings = Settings.Load();

        var localIps = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork && !ip.Address.ToString().StartsWith("169.254"))
            .Select(ip => ip.Address.ToString())
            .ToList();

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new
            {
                port = settings.Port,
                save_folder = settings.SaveFolder,
                local_ips = localIps,
                auto_discovery = settings.AutoDiscoveryEnabled,
                web_server_enabled = settings.WebServerEnabled,
                web_port = settings.WebPort,
                safe_mode = settings.SafeModeEnabled,
                require_accept = settings.RequireAcceptIncoming,
                speed_limit_kbps = settings.SpeedLimitKBps,
                saved_devices_count = settings.Devices.Count
            });
            return 0;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== EdgeDrop Status & Configuration ===");
        Console.ResetColor();
        Console.WriteLine($"  TCP Port:           {settings.Port}");
        Console.WriteLine($"  Save Folder:        {settings.SaveFolder}");
        Console.WriteLine($"  Local IPs:          {string.Join(", ", localIps)}");
        Console.WriteLine($"  Auto Discovery:     {(settings.AutoDiscoveryEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Safe Mode:          {(settings.SafeModeEnabled ? "Enabled" : "Disabled")}");
        Console.WriteLine($"  Require Accept:     {(settings.RequireAcceptIncoming ? "Yes" : "No")}");
        Console.WriteLine($"  Speed Limit:        {(settings.SpeedLimitKBps > 0 ? $"{settings.SpeedLimitKBps} KB/s" : "Unlimited")}");
        Console.WriteLine($"  Saved Devices:      {settings.Devices.Count}");
        Console.WriteLine();

        return 0;
    }
}
