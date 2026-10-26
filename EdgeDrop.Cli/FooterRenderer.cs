using System;
using EdgeDrop.Core.Utils;

namespace EdgeDrop.Cli;

public static class FooterRenderer
{
    public const int FooterHeight = 3; // 1 divider + 1 info + 1 bar

    private static readonly object SyncLock = new();
    private static bool _isActiveTransfer = false;
    private static string _activeFileName = "";
    private static double _activeRatio = 0.0;
    private static double _activeBytesPerSec = 0.0;
    private static double _activeRemainingSec = 0.0;
    private static long _activeBytesSent = 0;
    private static long _activeTotalBytes = 0;
    private static string _customStatus = "";
    private static string? _cachedPrimaryIp = null;

    public static void SetActiveTransfer(string fileName, double ratio, double bytesPerSec, double remainingSec, long bytesSent, long totalBytes)
    {
        lock (SyncLock)
        {
            _isActiveTransfer = true;
            _activeFileName = fileName;
            _activeRatio = ratio;
            _activeBytesPerSec = bytesPerSec;
            _activeRemainingSec = remainingSec;
            _activeBytesSent = bytesSent;
            _activeTotalBytes = totalBytes;
            _customStatus = "";
        }
        Redraw();
    }

    public static void SetIdle(string? statusMessage = null)
    {
        lock (SyncLock)
        {
            _isActiveTransfer = false;
            _customStatus = statusMessage ?? "";
        }
        Redraw();
    }

    public static void Redraw()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode || !HeaderRenderer.IsPinnedLayoutEnabled) return;

        try
        {
            int height = Console.WindowHeight;
            int width = Math.Max(50, Console.WindowWidth);
            if (height < HeaderRenderer.HeaderHeight + FooterHeight + 2) return;

            int dividerRow = height - 2;
            int infoRow = height - 1;
            int barRow = height;

            lock (SyncLock)
            {
                // Save cursor position
                Console.Write("\x1b[s");

                // 1. Divider line
                Console.Write($"\x1b[{dividerRow};1H\x1b[2K");
                Console.Write($" {ConsoleRenderer.DarkMuted}{new string('─', Math.Max(10, width - 2))}{ConsoleRenderer.Reset}");

                // 2. Info line
                Console.Write($"\x1b[{infoRow};1H\x1b[2K");
                if (_isActiveTransfer)
                {
                    string speedText = ConsoleRenderer.FormatSpeed(_activeBytesPerSec);
                    string etaText = TimeSpan.FromSeconds(Math.Max(0, _activeRemainingSec)).ToString(@"mm\:ss");
                    string bytesInfo = $"{ConsoleRenderer.FormatBytes(_activeBytesSent)} / {ConsoleRenderer.FormatBytes(_activeTotalBytes)}";
                    string name = Truncate(_activeFileName, Math.Max(15, width - 50));

                    Console.Write($"  {ConsoleRenderer.SuccessColor}●{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}{name}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.TextLight}{bytesInfo}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.BrandLight}{speedText}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.MutedColor}ETA: {etaText}{ConsoleRenderer.Reset}");
                }
                else
                {
                    _cachedPrimaryIp ??= NetworkHelper.GetPrimaryLocalIp();
                    var settings = Settings.Load();

                    string statusPart = string.IsNullOrEmpty(_customStatus)
                        ? $"{ConsoleRenderer.SuccessColor}●{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}IDLE{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.MutedColor}Listening on{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{_cachedPrimaryIp}:{settings.Port}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}  {ConsoleRenderer.MutedColor}Safe Mode:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{(settings.SafeModeEnabled ? "On" : "Off")}{ConsoleRenderer.Reset}"
                        : $"{ConsoleRenderer.BrandLight}●{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{_customStatus}{ConsoleRenderer.Reset}";

                    Console.Write($"  {statusPart}");
                }

                // 3. Bar / Hint row
                Console.Write($"\x1b[{barRow};1H\x1b[2K");
                if (_isActiveTransfer)
                {
                    double ratio = Math.Clamp(_activeRatio, 0.0, 1.0);
                    int totalBarCols = Math.Clamp(width - 25, 10, 45);
                    int filled = (int)(ratio * totalBarCols);
                    int empty = totalBarCols - filled;

                    string barFilled = new string('█', filled);
                    string barEmpty = new string('░', empty);
                    string percentText = $"{(ratio * 100):F0}%";

                    Console.Write($"  {ConsoleRenderer.Brand}{barFilled}{ConsoleRenderer.DarkMuted}{barEmpty}{ConsoleRenderer.Reset} {ConsoleRenderer.Bold}{percentText,4}{ConsoleRenderer.Reset}  {ConsoleRenderer.DarkMuted}Transferring data...{ConsoleRenderer.Reset}");
                }
                else
                {
                    Console.Write($"  {ConsoleRenderer.DarkMuted}Type 'send <files>' to transfer, 'help' for commands, 'exit' to quit.{ConsoleRenderer.Reset}");
                }

                // Restore cursor position
                Console.Write("\x1b[u");
            }
        }
        catch { }
    }

    private static string Truncate(string str, int maxLen)
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLen) return str;
        return str.Substring(0, maxLen - 3) + "...";
    }
}
