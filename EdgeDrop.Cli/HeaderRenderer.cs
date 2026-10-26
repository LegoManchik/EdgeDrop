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

    public const int HeaderHeight = 5; // Rows 1-4 are header & divider, Row 5 is workspace start

    public static void Initialize()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            int height = Math.Max(12, Console.WindowHeight);
            int width = Math.Max(40, Console.WindowWidth);
            _lastWidth = width;
            _lastHeight = height;

            // Set scrolling region from line 5 to terminal bottom.
            // Lines 1-4 are PINNED at the top and will NEVER scroll!
            Console.Write($"\x1b[{HeaderHeight};{height}r");

            Redraw();

            // Place cursor at line 5 (start of scrollable workspace)
            Console.Write($"\x1b[{HeaderHeight};1H");
        }
        catch { }
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

            // Row 1: Logo line 1 + App Name & Version
            Console.Write("\x1b[1;1H\x1b[2K");
            Console.Write($"  {ConsoleRenderer.Brand}{ConsoleRenderer.Bold}█████╗ ██████╗{ConsoleRenderer.Reset}   {ConsoleRenderer.TextLight}{ConsoleRenderer.Bold}EdgeDrop{ConsoleRenderer.Reset} {ConsoleRenderer.MutedColor}v{Program.Version}{ConsoleRenderer.Reset}");

            // Row 2: Logo line 2 + Port & IP
            Console.Write("\x1b[2;1H\x1b[2K");
            Console.Write($"  {ConsoleRenderer.Brand}{ConsoleRenderer.Bold}██╔══╝ ██╔══██╗{ConsoleRenderer.Reset}  {ConsoleRenderer.MutedColor}Port:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{settings.Port}{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset} {ConsoleRenderer.MutedColor}IP:{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{_cachedIp}{ConsoleRenderer.Reset}");

            // Row 3: Logo line 3 + Subtitle & Status dot
            Console.Write("\x1b[3;1H\x1b[2K");
            Console.Write($"  {ConsoleRenderer.Brand}{ConsoleRenderer.Bold}█████╗ ██████╔╝{ConsoleRenderer.Reset}  {ConsoleRenderer.MutedColor}LAN File Transfer{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset} {ConsoleRenderer.SuccessColor}Online ●{ConsoleRenderer.Reset}");

            // Row 4: Subtle divider line across width
            Console.Write("\x1b[4;1H\x1b[2K");
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
