using System;
using EdgeDrop.Core.Utils;

namespace EdgeDrop.Cli;

public static class HeaderRenderer
{
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

    public static void PrintBanner()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            Console.Write("\x1b[r");

            int width = Math.Max(50, Console.WindowWidth);
            var settings = Settings.Load();
            _cachedIp ??= NetworkHelper.GetPrimaryLocalIp();

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

            Console.WriteLine();
            for (int i = 0; i < Logo.Length; i++)
            {
                string logoPart = $"{ConsoleRenderer.Brand}{Logo[i]}{ConsoleRenderer.Reset}";
                if (showInfo && i < infoLines.Length && !string.IsNullOrEmpty(infoLines[i]))
                {
                    Console.WriteLine($"  {logoPart}   {infoLines[i]}");
                }
                else
                {
                    Console.WriteLine($"  {logoPart}");
                }
            }

            Console.WriteLine($" {ConsoleRenderer.DarkMuted}{new string('─', Math.Max(10, width - 2))}{ConsoleRenderer.Reset}");
            Console.WriteLine();
        }
        catch { }
    }
}
