using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EdgeDrop.Cli.Commands;

namespace EdgeDrop.Cli;

public static class Program
{
    public const string Version = "1.1.0";

    public static async Task<int> Main(string[] args)
    {
        // When launched with NO arguments (e.g. double-clicked from Explorer or 'edgedrop-cli' in console):
        // Start interactive shell so the window stays open!
        if (args.Length == 0)
        {
            return await RunInteractiveShellAsync();
        }

        return await ExecuteCommandLineAsync(args);
    }

    public static async Task<int> ExecuteCommandLineAsync(string[] args)
    {
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

    private static async Task<int> RunInteractiveShellAsync()
    {
        Console.Title = $"EdgeDrop CLI v{Version}";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=================================================");
        Console.WriteLine($"      EdgeDrop Interactive Shell (v{Version})     ");
        Console.WriteLine($"=================================================");
        Console.ResetColor();
        Console.WriteLine("Type a command or 'help' for options. Type 'exit' to quit.\n");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("edgedrop> ");
            Console.ResetColor();

            string? line = Console.ReadLine();
            if (line == null) break; // EOF or Ctrl+Z
            line = line.Trim().Trim('\uFEFF');
            if (string.IsNullOrEmpty(line)) continue;

            if (line.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("q", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (line.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("cls", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                continue;
            }

            string[] parsedArgs = ParseCommandLine(line);
            if (parsedArgs.Length > 0)
            {
                await ExecuteCommandLineAsync(parsedArgs);
                Console.WriteLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Bye!");
        Console.ResetColor();
        return 0;
    }

    private static string[] ParseCommandLine(string cmdLine)
    {
        var list = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < cmdLine.Length; i++)
        {
            char c = cmdLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    list.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            list.Add(current.ToString());
        }

        return list.ToArray();
    }

    private static int HandleUnknownCommand(string command)
    {
        ConsoleRenderer.Error($"Unknown command '{command}'.");
        ConsoleRenderer.Muted("Type 'help' for a list of available commands.");
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
        Console.WriteLine("  help                            Show this help information");
        Console.WriteLine("  exit, quit                      Exit the interactive shell");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  --json                          Output machine-readable JSON (ideal for scripts)");
        Console.WriteLine("  --quiet, -q                     Suppress non-essential progress and info messages");
        Console.WriteLine("  --version, -v                   Show CLI version");
        Console.WriteLine("  --help, -h                      Show this help information");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  send report.pdf --to 192.168.1.15");
        Console.WriteLine("  send photo.png archive.zip --to \"Laptop\"");
        Console.WriteLine("  receive --yes --dir \"C:\\Incoming\"");
        Console.WriteLine("  devices --json");
        Console.WriteLine("  ping 192.168.1.15");
        Console.WriteLine();
    }
}
