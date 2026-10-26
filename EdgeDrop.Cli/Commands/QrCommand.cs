using System;
using System.Linq;
using EdgeDrop.Core.Utils;

namespace EdgeDrop.Cli.Commands;

public static class QrCommand
{
    public static int Run(string[] args)
    {
        string? targetUrl = null;
        string? pin = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--pin" or "-p" && i + 1 < args.Length)
            {
                pin = args[++i];
            }
            else if (!arg.StartsWith("-") && targetUrl == null)
            {
                targetUrl = arg;
            }
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            var settings = Settings.Load();
            int webPort = settings.WebPort > 0 ? settings.WebPort : 11000;
            string ip = NetworkHelper.GetPrimaryLocalIp();
            targetUrl = $"http://{ip}:{webPort}/";
        }

        QrRenderer.Print(targetUrl, "EdgeDrop Web Transfer QR Code", pin);
        return 0;
    }
}
