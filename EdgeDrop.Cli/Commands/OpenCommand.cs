using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace EdgeDrop.Cli.Commands;

public static class OpenCommand
{
    public static int Run(string[] args)
    {
        var settings = Settings.Load();
        string targetPath = settings.SaveFolder;

        if (args.Length > 0 && !args[0].StartsWith("-"))
        {
            string custom = args[0].ToLowerInvariant();
            if (custom is "config" or "settings")
            {
                targetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeDrop");
            }
            else if (custom is "history" or "log" or "logs")
            {
                targetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeDrop");
            }
            else if (Directory.Exists(args[0]))
            {
                targetPath = args[0];
            }
        }

        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
        }

        try
        {
            string launcher = "explorer.exe";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) launcher = "xdg-open";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) launcher = "open";

            Process.Start(new ProcessStartInfo
            {
                FileName = launcher,
                Arguments = $"\"{targetPath}\"",
                UseShellExecute = true
            });

            if (ConsoleRenderer.JsonMode)
            {
                ConsoleRenderer.WriteJson(new { opened_path = targetPath });
            }
            else
            {
                ConsoleRenderer.Success($"Opened '{targetPath}' in Explorer.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Error($"Failed to open Explorer: {ex.Message}");
            return 1;
        }
    }
}
