using System;
using System.Linq;
using EdgeDrop.Core.Utils;

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
            string ip = NetworkHelper.GetPrimaryLocalIp();
            targetUrl = $"http://{ip}:{webPort}/";
        }

        QrRenderer.Print(targetUrl, "EdgeDrop Web Transfer QR Code");
        return 0;
    }
}
