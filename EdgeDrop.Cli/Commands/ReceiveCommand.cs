using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeDrop.Cli.Commands;

public static class ReceiveCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = Settings.Load();
        int port = settings.Port;
        string saveDir = settings.SaveFolder;
        bool autoAccept = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--port" or "-p" && i + 1 < args.Length && int.TryParse(args[++i], out int parsedPort))
            {
                port = parsedPort;
            }
            else if (arg is "--dir" or "-d" && i + 1 < args.Length)
            {
                saveDir = args[++i];
            }
            else if (arg is "--yes" or "-y" or "--auto-accept")
            {
                autoAccept = true;
            }
        }

        if (!Directory.Exists(saveDir))
        {
            try
            {
                Directory.CreateDirectory(saveDir);
            }
            catch (Exception ex)
            {
                ConsoleRenderer.Error($"Cannot create '{saveDir}': {ex.Message}");
                return 2;
            }
        }

        var receiver = new FileReceiver(port, saveDir, settings);

        receiver.AcceptFileRequested = async (fileName, remoteIp, size) =>
        {
            if (autoAccept)
            {
                ConsoleRenderer.Info($"Auto-accepted '{fileName}' from {remoteIp}");
                return true;
            }

            ConsoleRenderer.WriteJsonEvent("incoming_request", new { file = fileName, size, sender_ip = remoteIp });

            if (ConsoleRenderer.JsonMode)
            {
                return !settings.RequireAcceptIncoming;
            }

            Console.WriteLine();
            Console.Write($"  {ConsoleRenderer.WarningColor}?{ConsoleRenderer.Reset} Incoming '{fileName}' ({ConsoleRenderer.FormatBytes(size)}) from {remoteIp} · Accept? [Y/n]: ");

            var tcs = new TaskCompletionSource<bool>();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            timeoutCts.Token.Register(() => tcs.TrySetResult(false));

            _ = Task.Run(() =>
            {
                string? input = Console.ReadLine()?.Trim();
                bool accepted = string.IsNullOrEmpty(input) || input.StartsWith("y", StringComparison.OrdinalIgnoreCase);
                tcs.TrySetResult(accepted);
            });

            bool result = await tcs.Task;
            if (result)
            {
                ConsoleRenderer.Success($"Accepted '{fileName}'");
            }
            else
            {
                ConsoleRenderer.Warning($"Rejected '{fileName}'");
            }
            return result;
        };

        receiver.ReceiveStarted += (fileName, totalBytes) =>
        {
            ConsoleRenderer.Info($"Receiving '{fileName}' ({ConsoleRenderer.FormatBytes(totalBytes)})...");
            ConsoleRenderer.WriteJsonEvent("receive_start", new { file = fileName, size = totalBytes });
        };

        receiver.ReceiveProgress += (fileName, report) =>
        {
            ConsoleRenderer.RenderProgress(fileName, report.Ratio, report.BytesPerSecond, report.RemainingSeconds, report.BytesSent, report.TotalBytes);
        };

        receiver.ReceiveCompleted += fileName =>
        {
            ConsoleRenderer.FinishProgress();
            ConsoleRenderer.Success($"Received '{fileName}'");
            ConsoleRenderer.WriteJsonEvent("receive_complete", new { file = fileName, save_dir = saveDir });
        };

        receiver.ReceiveCancelled += fileName =>
        {
            ConsoleRenderer.FinishProgress();
            ConsoleRenderer.Warning($"Cancelled '{fileName}'");
            ConsoleRenderer.WriteJsonEvent("receive_cancelled", new { file = fileName });
        };

        receiver.Error += err =>
        {
            ConsoleRenderer.FinishProgress();
            ConsoleRenderer.Error($"{err}");
            ConsoleRenderer.WriteJsonEvent("receive_error", new { error = err });
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            receiver.Start();
            ConsoleRenderer.Info($"Listening on port {port} · {saveDir}");
            ConsoleRenderer.Muted("Press Ctrl+C to stop.");
            ConsoleRenderer.WriteJsonEvent("listening", new { port, save_folder = saveDir, auto_accept = autoAccept });

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
            receiver.Stop();
        }

        ConsoleRenderer.Info("Stopped.");
        return 0;
    }
}
