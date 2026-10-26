using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EdgeDrop.Cli.Commands;

public static class QrCommand
{
    public static int Run(string[] args)
    {
        string? targetUrl = args.FirstOrDefault(a => !a.StartsWith("-"));

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            var settings = Settings.Load();
            int webPort = settings.WebPort > 0 ? settings.WebPort : 11000;
            string ip = GetLocalIpAddress() ?? "127.0.0.1";
            targetUrl = $"http://{ip}:{webPort}/";
        }

        QrRenderer.Print(targetUrl, "EdgeDrop Web Transfer QR Code");
        return 0;
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork && !ip.Address.ToString().StartsWith("169.254"))
                .Select(ip => ip.Address.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
