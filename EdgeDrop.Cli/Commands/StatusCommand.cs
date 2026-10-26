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
        PrintRow("Port", settings.Port.ToString());
        PrintRow("Folder", settings.SaveFolder);
        PrintRow("IPs", string.Join(", ", localIps));
        PrintRow("Discovery", settings.AutoDiscoveryEnabled ? "Active" : "Disabled");
        PrintRow("Safe Mode", settings.SafeModeEnabled ? "Enabled" : "Disabled");
        PrintRow("Accept", settings.RequireAcceptIncoming ? "Manual confirmation" : "Automatic");
        PrintRow("Devices", $"{settings.Devices.Count} saved");
        Console.WriteLine();

        return 0;
    }

    private static void PrintRow(string label, string value)
    {
        Console.WriteLine($"  {ConsoleRenderer.MutedColor}{label,-12}{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{value}{ConsoleRenderer.Reset}");
    }
}
