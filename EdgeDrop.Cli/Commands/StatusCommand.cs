using System;
using System.Linq;
using EdgeDrop.Core.Utils;

namespace EdgeDrop.Cli.Commands;

public static class StatusCommand
{
    public static int Run(string[] args)
    {
        var settings = Settings.Load();
        string primaryIp = NetworkHelper.GetPrimaryLocalIp();
        var localIps = NetworkHelper.GetAllActiveLocalIps();

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new
            {
                port = settings.Port,
                primary_ip = primaryIp,
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
        PrintRow("Primary IP", primaryIp);
        PrintRow("Port", settings.Port.ToString());
        PrintRow("Folder", settings.SaveFolder);
        PrintRow("All LAN IPs", string.Join(", ", localIps));
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
