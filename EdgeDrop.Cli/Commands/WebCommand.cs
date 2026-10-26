using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EdgeDrop.Core.Utils;

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

        string localIp = NetworkHelper.GetPrimaryLocalIp();
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
            string pin = web.Pin;

            Console.WriteLine();
            ConsoleRenderer.Success($"Web server running on {url}");
            if (settings.SafeModeEnabled && !string.IsNullOrEmpty(pin))
            {
                Console.WriteLine($"  {ConsoleRenderer.BrandLight}{ConsoleRenderer.Bold}PIN CODE:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{ConsoleRenderer.Bold}{pin}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}(enter on phone){ConsoleRenderer.Reset}");
            }
            ConsoleRenderer.Muted($"Saving uploads to: {saveDir}");

            if (!ConsoleRenderer.JsonMode && !ConsoleRenderer.Quiet)
            {
                QrRenderer.Print(url, "Connect Mobile / Peer via Browser", settings.SafeModeEnabled ? pin : null);
            }

            ConsoleRenderer.Muted("Press Ctrl+C to stop.");

            ConsoleRenderer.WriteJsonEvent("web_started", new { url, port, pin, safe_mode = settings.SafeModeEnabled, save_folder = saveDir });

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
}
