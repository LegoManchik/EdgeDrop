using System;
using System.Linq;
using System.Threading.Tasks;
using EdgeDrop.Cli.Commands;

namespace EdgeDrop.Cli;

public static class Program
{
    public const string Version = "1.1.0";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        // Global flag extraction
        var cleanArgs = new List<string>();
        foreach (var arg in args)
        {
            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleRenderer.JsonMode = true;
            }
            else if (arg is "--quiet" or "-q")
            {
                ConsoleRenderer.Quiet = true;
            }
            else if (arg is "--version" or "-v")
            {
                if (ConsoleRenderer.JsonMode)
                {
                    ConsoleRenderer.WriteJson(new { app = "EdgeDrop CLI", version = Version });
                }
                else
                {
                    Console.WriteLine($"EdgeDrop CLI v{Version}");
                }
                return 0;
            }
            else if (arg is "--help" or "-h" or "help")
            {
                PrintHelp();
                return 0;
            }
            else
            {
                cleanArgs.Add(arg);
            }
        }

        if (cleanArgs.Count == 0)
        {
            PrintHelp();
            return 0;
        }

        string command = cleanArgs[0].ToLowerInvariant();
        string[] cmdArgs = cleanArgs.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "send" => await SendCommand.RunAsync(cmdArgs),
                "receive" or "listen" => await ReceiveCommand.RunAsync(cmdArgs),
                "devices" or "scan" => await DevicesCommand.RunAsync(cmdArgs),
                "web" => await WebCommand.RunAsync(cmdArgs),
                "ping" => await PingCommand.RunAsync(cmdArgs),
                "status" => StatusCommand.Run(cmdArgs),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"Fatal error: {ex.Message}");
            if (ConsoleRenderer.JsonMode)
            {
                ConsoleRenderer.WriteJson(new { status = "fatal_error", error = ex.ToString() });
            }
            return 1;
        }
    }

    private static int HandleUnknownCommand(string command)
    {
        ConsoleRenderer.Error($"Unknown command '{command}'.");
        ConsoleRenderer.Muted("Run 'edgedrop-cli --help' for a list of available commands.");
        return 2;
    }

    private static void PrintHelp()
    {
        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new
            {
                app = "EdgeDrop CLI",
                version = Version,
                commands = new[] { "send", "receive", "devices", "web", "ping", "status" }
            });
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"EdgeDrop CLI v{Version} — Fast local network file transfer utility");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  edgedrop-cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine("COMMANDS:");
        Console.WriteLine("  send <files...> --to <target>   Send file(s) to a peer by IP or configured name");
        Console.WriteLine("  receive, listen                 Start listening for incoming file transfers");
        Console.WriteLine("  devices, scan                   Scan local network for active EdgeDrop peers");
        Console.WriteLine("  web                             Start browser-based web transfer server with QR");
        Console.WriteLine("  ping <target>                   Test connectivity and latency to a peer");
        Console.WriteLine("  status                          Show current configuration, ports, and local IPs");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  --json                          Output machine-readable JSON (ideal for scripts)");
        Console.WriteLine("  --quiet, -q                     Suppress non-essential progress and info messages");
        Console.WriteLine("  --version, -v                   Show CLI version");
        Console.WriteLine("  --help, -h                      Show this help information");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  edgedrop-cli send report.pdf --to 192.168.1.15");
        Console.WriteLine("  edgedrop-cli send photo.png archive.zip --to \"Laptop\"");
        Console.WriteLine("  edgedrop-cli receive --yes --dir \"C:\\Incoming\"");
        Console.WriteLine("  edgedrop-cli devices --json");
        Console.WriteLine("  edgedrop-cli ping 192.168.1.15");
        Console.WriteLine();
    }
}
