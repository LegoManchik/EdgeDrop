using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeDrop.Cli.Commands;

public static class SendCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var settings = Settings.Load();
        string? target = null;
        int port = settings.Port;
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--to" or "-t" && i + 1 < args.Length)
            {
                target = args[++i];
            }
            else if (arg is "--port" or "-p" && i + 1 < args.Length && int.TryParse(args[++i], out int parsedPort))
            {
                port = parsedPort;
            }
            else if (!arg.StartsWith("-"))
            {
                files.Add(arg);
            }
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            ConsoleRenderer.Error("Specify target with --to <ip|name>");
            return 2;
        }

        if (files.Count == 0)
        {
            ConsoleRenderer.Error("No files specified.");
            return 2;
        }

        string targetIp = target;
        if (target.Contains(':'))
        {
            var parts = target.Split(':');
            targetIp = parts[0];
            if (int.TryParse(parts[1], out int p)) port = p;
        }
        else
        {
            var knownDevice = settings.Devices.FirstOrDefault(d =>
                string.Equals(d.Name, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Ip, target, StringComparison.OrdinalIgnoreCase));
            if (knownDevice != null)
            {
                targetIp = knownDevice.Ip;
                port = knownDevice.Port > 0 ? knownDevice.Port : port;
            }
        }

        ConsoleRenderer.WriteJsonEvent("transfer_started", new { target_ip = targetIp, port, file_count = files.Count });

        int failed = 0;
        var swTotal = Stopwatch.StartNew();

        foreach (var file in files)
        {
            string actualPath = file;
            bool isTempZip = false;

            if (Directory.Exists(file))
            {
                string dirName = Path.GetFileName(file.TrimEnd('\\', '/'));
                string tempZip = Path.Combine(Path.GetTempPath(), $"{dirName}_{Guid.NewGuid():N}.zip");
                ZipFile.CreateFromDirectory(file, tempZip, CompressionLevel.Fastest, false);
                actualPath = tempZip;
                isTempZip = true;
            }
            else if (!File.Exists(file))
            {
                ConsoleRenderer.Error($"File not found: {file}");
                failed++;
                continue;
            }

            string fileName = Path.GetFileName(actualPath);
            long fileSize = new FileInfo(actualPath).Length;

            ConsoleRenderer.Info($"Sending '{fileName}' ({ConsoleRenderer.FormatBytes(fileSize)}) to {targetIp}:{port}");

            var progress = new Progress<TransferProgressReport>(report =>
            {
                ConsoleRenderer.RenderProgress(fileName, report.Ratio, report.BytesPerSecond, report.RemainingSeconds, report.BytesSent, report.TotalBytes);
            });

            try
            {
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                var swFile = Stopwatch.StartNew();
                await FileSender.SendFileAsync(actualPath, targetIp, port, progress, cts.Token, settings);
                swFile.Stop();

                ConsoleRenderer.FinishProgress();
                ConsoleRenderer.Success($"Sent in {swFile.Elapsed:s\\.f}s");
                ConsoleRenderer.WriteJsonEvent("file_complete", new
                {
                    file = fileName,
                    size = fileSize,
                    duration_ms = swFile.ElapsedMilliseconds,
                    status = "success"
                });
            }
            catch (OperationCanceledException)
            {
                ConsoleRenderer.FinishProgress();
                ConsoleRenderer.Warning($"Cancelled");
                failed++;
                break;
            }
            catch (Exception ex)
            {
                ConsoleRenderer.FinishProgress();
                ConsoleRenderer.Error($"{ex.Message}");
                ConsoleRenderer.WriteJsonEvent("file_error", new { file = fileName, error = ex.Message });
                failed++;
            }
            finally
            {
                if (isTempZip)
                {
                    try { File.Delete(actualPath); } catch { }
                }
            }
        }

        swTotal.Stop();
        if (failed == 0)
        {
            ConsoleRenderer.WriteJsonEvent("finished", new { status = "success", duration_ms = swTotal.ElapsedMilliseconds });
            return 0;
        }
        else
        {
            ConsoleRenderer.WriteJsonEvent("finished", new { status = "failed", failed, duration_ms = swTotal.ElapsedMilliseconds });
            return 1;
        }
    }
}
