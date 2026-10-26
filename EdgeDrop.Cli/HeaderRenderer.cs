using System;
using EdgeDrop.Core.Utils;

namespace EdgeDrop.Cli;

public static class HeaderRenderer
{
    public static bool IsPinnedLayoutEnabled { get; private set; } = false;

    private static int _lastWidth = 0;
    private static int _lastHeight = 0;
    private static string? _cachedIp = null;

    private static readonly string[] Logo = new[]
    {
        "█████ ████   ███  █████    ████  ████   ███  ████    ",
        "█░░░░░█░░░█ █ ░░░ █░░░░░   █░░░█ █░░░█ █ ░░█ █░░░█   ",
        "████░░█░░░█░█░ ██░████░░░  █░░░█░████░░█░ ░█░████░░  ",
        "█░░░░ █░░ █░█░░ █░█░░░░    █░░ █░█░░█░ █░░ █░█░░░░ ░ ",
        "█████░████ ░░███ ░█████░   ████ ░█░░░█░ ███ ░█░░░░░  ",
        " ░░░░░ ░░░░ ░ ░░░ ░░░░░░    ░░░░ ░░░  ░  ░░░ ░░░     ",
        "  ░░░░░ ░░░░   ░░░  ░░░░░    ░░░░  ░   ░  ░░░  ░     "
    };

    public static int HeaderHeight => Logo.Length + 2; // 7 rows logo + 1 divider + 1 workspace start = line 9

    public static void Initialize()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            int height = Console.WindowHeight;
            int width = Math.Max(50, Console.WindowWidth);
            _lastWidth = width;
            _lastHeight = height;

            int scrollBottom = height - FooterRenderer.FooterHeight;

            // Pin Header (lines 1..HeaderHeight - 1) and Footer (lines scrollBottom + 1..height).
            // Scrollable workspace is HeaderHeight..scrollBottom.
            Console.Write($"\x1b[{HeaderHeight};{scrollBottom}r");
            IsPinnedLayoutEnabled = true;

            Redraw();
            FooterRenderer.Redraw();

            // Place cursor at workspace start
            Console.Write($"\x1b[{HeaderHeight};1H");
        }
        catch { }
    }

    public static void Reset()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            IsPinnedLayoutEnabled = false;
            Console.Write("\x1b[r");
            Console.Write($"\x1b[{Console.WindowHeight};1H");
        }
        catch { }
    }

    public static void CheckResize()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            if (Console.WindowWidth != _lastWidth || Console.WindowHeight != _lastHeight)
            {
                Initialize();
            }
            else
            {
                Redraw();
                FooterRenderer.Redraw();
            }
        }
        catch { }
    }

    public static void Redraw()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            int width = Math.Max(50, Console.WindowWidth);
            var settings = Settings.Load();
            _cachedIp ??= NetworkHelper.GetPrimaryLocalIp();

            // Hide cursor and save position to prevent cursor jump flicker
            Console.Write("\x1b[?25l\x1b[s");

            string[] infoLines = new string[]
            {
                $"{ConsoleRenderer.TextLight}{ConsoleRenderer.Bold}EdgeDrop{ConsoleRenderer.Reset}  {ConsoleRenderer.BrandLight}v{Program.Version}{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.DarkMuted}Local Network Peer-to-Peer Transfer{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.MutedColor}Port:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{settings.Port}{ConsoleRenderer.Reset}   {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}   {ConsoleRenderer.MutedColor}IP:{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{_cachedIp}{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.MutedColor}Discovery:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{(settings.AutoDiscoveryEnabled ? "Active" : "Off")}{ConsoleRenderer.Reset}   {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}   {ConsoleRenderer.MutedColor}Safe Mode:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{(settings.SafeModeEnabled ? "On" : "Off")}{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.MutedColor}Status:{ConsoleRenderer.Reset} {ConsoleRenderer.SuccessColor}Online ● Ready{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.DarkMuted}Type 'help' for commands, 'exit' to quit.{ConsoleRenderer.Reset}",
                ""
            };

            bool showInfo = width >= 90;

            for (int i = 0; i < Logo.Length; i++)
            {
                int row = i + 1;
                string logoPart = $"{ConsoleRenderer.Brand}{Logo[i]}{ConsoleRenderer.Reset}";
                string lineContent = (showInfo && i < infoLines.Length && !string.IsNullOrEmpty(infoLines[i]))
                    ? $"  {logoPart}   {infoLines[i]}"
                    : $"  {logoPart}";

                Console.Write($"\x1b[{row};1H\x1b[2K{lineContent}");
            }

            // Divider row
            int dividerRow = Logo.Length + 1;
            Console.Write($"\x1b[{dividerRow};1H\x1b[2K {ConsoleRenderer.DarkMuted}{new string('─', Math.Max(10, width - 2))}{ConsoleRenderer.Reset}");

            // Restore cursor and unhide
            Console.Write("\x1b[u\x1b[?25h");
        }
        catch { }
    }
}
