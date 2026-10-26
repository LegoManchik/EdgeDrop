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

    private static readonly string[] Logo = new[]
    {
        "      =++++++++++++-      ",
        "  +++++++++++++++++++++-  ",
        " ++++++++++++++++++++++++ ",
        "-++++++++++++++++++-+++++ ",
        "++++++++-..=+++++++..++++ ",
        "+++++++...+++++++++..++++ ",
        "+++++............=+..++++ ",
        "+++++............=+..++++ ",
        "+++++++...+++++++++..++++ ",
        "++++++++-..=+++++++..++++ ",
        "-++++++++++++++++++=+++++ ",
        " ++++++++++++++++++++++++ ",
        "  +++++++++++++++++++++-  "
    };

    public static int HeaderHeight => Logo.Length + 2; // 13 rows of logo + 1 divider + 1 workspace row = line 15

    public static void Initialize()
    {
        if (Console.IsOutputRedirected || ConsoleRenderer.JsonMode) return;

        try
        {
            int height = Math.Max(HeaderHeight + 5, Console.WindowHeight);
            int width = Math.Max(50, Console.WindowWidth);
            _lastWidth = width;
            _lastHeight = height;

            // Pin lines 1 to (HeaderHeight - 1). Line HeaderHeight is workspace start.
            Console.Write($"\x1b[{HeaderHeight};{height}r");

            Redraw();

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
            int width = Math.Max(50, Console.WindowWidth);
            var settings = Settings.Load();
            _cachedIp ??= GetPrimaryIp();

            // Save cursor position
            Console.Write("\x1b[s");

            string[] infoLines = new string[]
            {
                "",
                "",
                $"{ConsoleRenderer.TextLight}{ConsoleRenderer.Bold}EdgeDrop{ConsoleRenderer.Reset}  {ConsoleRenderer.BrandLight}v{Program.Version}{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.DarkMuted}Local Network Peer-to-Peer Transfer{ConsoleRenderer.Reset}",
                "",
                $"{ConsoleRenderer.MutedColor}Port:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{settings.Port}{ConsoleRenderer.Reset}   {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}   {ConsoleRenderer.MutedColor}IP:{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}{_cachedIp}{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.MutedColor}Discovery:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{(settings.AutoDiscoveryEnabled ? "Active" : "Off")}{ConsoleRenderer.Reset}   {ConsoleRenderer.DarkMuted}·{ConsoleRenderer.Reset}   {ConsoleRenderer.MutedColor}Safe Mode:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{(settings.SafeModeEnabled ? "On" : "Off")}{ConsoleRenderer.Reset}",
                $"{ConsoleRenderer.MutedColor}Status:{ConsoleRenderer.Reset} {ConsoleRenderer.SuccessColor}Online ● Ready{ConsoleRenderer.Reset}",
                "",
                $"{ConsoleRenderer.DarkMuted}Type 'help' for commands, 'exit' to quit.{ConsoleRenderer.Reset}",
                "",
                "",
                ""
            };

            for (int i = 0; i < Logo.Length; i++)
            {
                int row = i + 1;
                Console.Write($"\x1b[{row};1H\x1b[2K");

                string logoPart = $"{ConsoleRenderer.Brand}{Logo[i]}{ConsoleRenderer.Reset}";
                string infoPart = i < infoLines.Length ? infoLines[i] : "";

                Console.Write($"  {logoPart}   {infoPart}");
            }

            // Divider row
            int dividerRow = Logo.Length + 1;
            Console.Write($"\x1b[{dividerRow};1H\x1b[2K");
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
