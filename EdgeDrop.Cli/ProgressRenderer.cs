using System;
using System.Diagnostics;

namespace EdgeDrop.Cli;

public static class ProgressRenderer
{
    private static readonly object SyncLock = new();
    private static bool _isRendering = false;
    private static readonly Stopwatch ThrottleTimer = new();
    private static int _lastLine1Len = 0;
    private static int _lastLine2Len = 0;

    public static void Render(string fileName, double ratio, double bytesPerSec, double remainingSec, long bytesSent, long totalBytes)
    {
        if (ConsoleRenderer.Quiet) return;

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJsonEvent("progress", new
            {
                file = fileName,
                bytes = bytesSent,
                total = totalBytes,
                percent = Math.Round(ratio * 100, 1),
                speed_bps = Math.Round(bytesPerSec, 0),
                eta_sec = Math.Round(remainingSec, 0)
            });
            return;
        }

        if (Console.IsOutputRedirected) return;

        lock (SyncLock)
        {
            if (_isRendering && ratio < 1.0 && ThrottleTimer.IsRunning && ThrottleTimer.ElapsedMilliseconds < 60)
            {
                return;
            }
            ThrottleTimer.Restart();

            int width = Math.Max(60, Console.WindowWidth);
            double clampedRatio = Math.Clamp(ratio, 0.0, 1.0);

            string speedText = ConsoleRenderer.FormatSpeed(bytesPerSec);
            string etaText = TimeSpan.FromSeconds(Math.Max(0, remainingSec)).ToString(@"mm\:ss");
            string bytesInfo = $"{ConsoleRenderer.FormatBytes(bytesSent)} / {ConsoleRenderer.FormatBytes(totalBytes)}";
            string name = Truncate(fileName, Math.Max(15, width - 48));

            string line1 = $"  {ConsoleRenderer.SuccessColor}●{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}{name}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.TextLight}{bytesInfo}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.BrandLight}{speedText}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.MutedColor}ETA: {etaText}{ConsoleRenderer.Reset}";

            int barWidth = Math.Clamp(width - 24, 15, 45);
            int filled = (int)(clampedRatio * barWidth);
            int empty = barWidth - filled;
            string barFilled = new string('█', filled);
            string barEmpty = new string('░', empty);
            string percentText = $"{(clampedRatio * 100):F0}%";

            string line2 = $"  {ConsoleRenderer.Brand}{barFilled}{ConsoleRenderer.DarkMuted}{barEmpty}{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}{percentText,4}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}Transferring...{ConsoleRenderer.Reset}";

            int vis1 = 2 + 2 + name.Length + 3 + bytesInfo.Length + 3 + speedText.Length + 3 + 5 + etaText.Length;
            int vis2 = 2 + barWidth + 1 + 5 + 2 + 15;

            string pad1 = vis1 < _lastLine1Len ? new string(' ', _lastLine1Len - vis1) : "";
            string pad2 = vis2 < _lastLine2Len ? new string(' ', _lastLine2Len - vis2) : "";

            _lastLine1Len = vis1;
            _lastLine2Len = vis2;

            if (!_isRendering)
            {
                Console.WriteLine(line1 + pad1);
                Console.Write(line2 + pad2);
                _isRendering = true;
            }
            else
            {
                Console.Write($"\x1b[1A\r{line1}{pad1}\n{line2}{pad2}");
            }

            if (clampedRatio >= 1.0)
            {
                Finish();
            }
        }
    }

    public static void Finish()
    {
        if (ConsoleRenderer.JsonMode || ConsoleRenderer.Quiet) return;

        lock (SyncLock)
        {
            if (_isRendering)
            {
                Console.WriteLine();
                _isRendering = false;
                _lastLine1Len = 0;
                _lastLine2Len = 0;
                ThrottleTimer.Reset();
            }
        }
    }

    private static string Truncate(string str, int maxLen)
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLen) return str;
        return str.Substring(0, maxLen - 3) + "...";
    }
}
