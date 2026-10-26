using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EdgeDrop.Cli;

public static class HeaderRenderer
{
    private static int _lastWidth = 0;
    private static int _lastHeight = 0;
    private static string? _cachedIp = null;

    public static void Initialize()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            int height = Math.Max(10, Console.WindowHeight);
            int width = Math.Max(40, Console.WindowWidth);
            _lastWidth = width;
            _lastHeight = height;

            // Set scrolling region: top margin at line 3, bottom margin at terminal height
            // Lines 1 and 2 are pinned and will NEVER scroll away!
            Console.Write($"\x1b[3;{height}r");

            Redraw();

            // Place cursor at line 3 (start of scrollable workspace)
            Console.Write("\x1b[3;1H");
        }
        catch
        {
            // Fallback for non-VT environments
        }
    }

    public static void Reset()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            // Reset scrolling region to full terminal window
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
            }
        }
        catch { }
    }

    public static void Redraw()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            int width = Math.Max(40, Console.WindowWidth);
            var settings = Settings.Load();
            _cachedIp ??= GetPrimaryIp();

            // Save current cursor position
            Console.Write("\x1b[s");

            // Move to row 1, col 1, clear line
            Console.Write("\x1b[1;1H\x1b[2K");

            // Left: Logo & Brand
            string leftLogo = $"{ConsoleRenderer.Brand}{ConsoleRenderer.Bold} ◈ EdgeDrop{ConsoleRenderer.Reset}";
            int leftVisualLen = 11; // " ◈ EdgeDrop"

            // Right: Version, Port, IP, Status dot
            string rightText = $"{ConsoleRenderer.MutedColor}v{Program.Version}{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}:{settings.Port}{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{_cachedIp}{ConsoleRenderer.Reset} {ConsoleRenderer.SuccessColor}●{ConsoleRenderer.Reset} ";
            int rightVisualLen = $"v{Program.Version} · :{settings.Port} · {_cachedIp} ● ".Length;

            int spacing = Math.Max(2, width - leftVisualLen - rightVisualLen);
            Console.Write($"\x1b[1;1H{leftLogo}{new string(' ', spacing)}{rightText}");

            // Row 2: Divider line
            Console.Write("\x1b[2;1H\x1b[2K");
            Console.Write($" {ConsoleRenderer.DarkMuted}{new string('─', Math.Max(10, width - 2))}{ConsoleRenderer.Reset}");

            // Restore cursor position
            Console.Write("\x1b[u");
        }
        catch { }
    }

    private static string GetPrimaryIp()
    {
        try
        {
            var ip = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork && !ip.Address.ToString().StartsWith("169.254"))
                .Select(ip => ip.Address.ToString())
                .FirstOrDefault();

            return ip ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
