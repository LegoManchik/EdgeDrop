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
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch { }

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
                "qr" => QrCommand.Run(cmdArgs),
                "config" => ConfigCommand.Run(cmdArgs),
                "set" or "get" => ConfigCommand.Run(new[] { command }.Concat(cmdArgs).ToArray()),
                "history" or "hist" or "log" => HistoryCommand.Run(cmdArgs),
                "open" => OpenCommand.Run(cmdArgs),
                "ping" => await PingCommand.RunAsync(cmdArgs),
                "status" => StatusCommand.Run(cmdArgs),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"Fatal: {ex.Message}");
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
        try
        {
            if (Console.WindowWidth < 105) Console.WindowWidth = 105;
            if (Console.WindowHeight < 34) Console.WindowHeight = 34;
        }
        catch { }

        HeaderRenderer.PrintBanner();

        var history = new List<string>();
        string prompt = $"  {ConsoleRenderer.MutedColor}edgedrop{ConsoleRenderer.Reset} {ConsoleRenderer.BrandLight}❯{ConsoleRenderer.Reset} ";

        try
        {
            while (true)
            {
                string? line = ConsoleInput.ReadLine(prompt, history);
                if (line == null) break;
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
                    HeaderRenderer.PrintBanner();
                    continue;
                }

                string[] parsedArgs = ParseCommandLine(line);
                if (parsedArgs.Length > 0)
                {
                    await ExecuteCommandLineAsync(parsedArgs);
                    Console.WriteLine();
                }
            }
        }
        finally
        {
            Console.Write("\x1b[r");
        }

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
        ConsoleRenderer.Error($"Unknown command '{command}'. Type 'help' for available commands.");
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
                commands = new[] { "send", "receive", "devices", "web", "qr", "config", "history", "open", "ping", "status" }
            });
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.Brand}{ConsoleRenderer.Bold}EdgeDrop CLI{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}v{Version}{ConsoleRenderer.Reset}");
        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.Bold}Usage:{ConsoleRenderer.Reset}");
        Console.WriteLine($"    edgedrop-cli <command> [options]");
        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.Bold}Commands:{ConsoleRenderer.Reset}");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}send{ConsoleRenderer.Reset} <files...> [--to <peer>]       Send file(s) or folders to target");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}receive{ConsoleRenderer.Reset}                             Listen for incoming files");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}devices{ConsoleRenderer.Reset} [scan|list|add|rm]          Manage and discover network peers");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}web{ConsoleRenderer.Reset}                                 Start browser server for mobile devices");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}qr{ConsoleRenderer.Reset} [url]                            Generate terminal QR code");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}history{ConsoleRenderer.Reset} [count|clear]               View or clear transfer history");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}config{ConsoleRenderer.Reset} [list|get|set|reset]         View and modify settings");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}open{ConsoleRenderer.Reset} [folder]                       Open downloads folder in Explorer");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}ping{ConsoleRenderer.Reset} <target>                       Check latency to a peer");
        Console.WriteLine($"    {ConsoleRenderer.BrandLight}status{ConsoleRenderer.Reset}                              Show active config and LAN IPs");
        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.Bold}Options:{ConsoleRenderer.Reset}");
        Console.WriteLine($"    {ConsoleRenderer.MutedColor}--json{ConsoleRenderer.Reset}                              Machine-readable JSON output");
        Console.WriteLine($"    {ConsoleRenderer.MutedColor}--quiet, -q{ConsoleRenderer.Reset}                         Suppress progress and info messages");
        Console.WriteLine($"    {ConsoleRenderer.MutedColor}--version, -v{ConsoleRenderer.Reset}                       Show version");
        Console.WriteLine($"    {ConsoleRenderer.MutedColor}--help, -h{ConsoleRenderer.Reset}                          Show this help");
        Console.WriteLine();
    }
}
