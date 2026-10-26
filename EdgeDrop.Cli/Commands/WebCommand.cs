using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeDrop.Cli.Commands;

public static class WebCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = Settings.Load();
        int port = settings.WebPort > 0 ? settings.WebPort : 11000;
        string saveDir = settings.SaveFolder;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--port" or "-p" && i + 1 < args.Length && int.TryParse(args[++i], out int p))
            {
                port = p;
            }
            else if (arg is "--dir" or "-d" && i + 1 < args.Length)
            {
                saveDir = args[++i];
            }
        }

        if (!Directory.Exists(saveDir))
        {
            Directory.CreateDirectory(saveDir);
        }

        string localIp = GetLocalIpAddress() ?? "127.0.0.1";
        string url = $"http://{localIp}:{port}/";

        var web = new WebServer(port, saveDir);

        web.UploadStarted += (fileName, totalBytes) =>
        {
            ConsoleRenderer.Info($"Web upload: '{fileName}' ({ConsoleRenderer.FormatBytes(totalBytes)})");
            ConsoleRenderer.WriteJsonEvent("web_upload_start", new { file = fileName, size = totalBytes });
        };

        web.UploadProgress += (fileName, report) =>
        {
            ConsoleRenderer.RenderProgress(fileName, report.Ratio, report.BytesPerSecond, report.RemainingSeconds, report.BytesSent, report.TotalBytes);
        };

        web.UploadCompleted += fileName =>
        {
            ConsoleRenderer.FinishProgress();
            ConsoleRenderer.Success($"Uploaded: '{fileName}'");
            ConsoleRenderer.WriteJsonEvent("web_upload_complete", new { file = fileName });
        };

        web.Error += err =>
        {
            ConsoleRenderer.FinishProgress();
            ConsoleRenderer.Error($"{err}");
            ConsoleRenderer.WriteJsonEvent("web_error", new { error = err });
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await web.StartAsync();
            ConsoleRenderer.Success($"Web server running on {url}");
            ConsoleRenderer.Muted($"Saving uploads to: {saveDir}");
            ConsoleRenderer.Muted("Press Ctrl+C to stop.");

            ConsoleRenderer.WriteJsonEvent("web_started", new { url, port, save_folder = saveDir });

            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(500, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"{ex.Message}");
            return 1;
        }
        finally
        {
            await web.StopAsync();
        }

        ConsoleRenderer.Info("Stopped.");
        return 0;
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !ip.Address.ToString().StartsWith("169.254"))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return null;
    }
}
