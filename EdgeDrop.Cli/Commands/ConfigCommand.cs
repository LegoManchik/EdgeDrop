using System;
using System.IO;
using System.Linq;

namespace EdgeDrop.Cli.Commands;

public static class ConfigCommand
{
    public static int Run(string[] args)
    {
        var settings = Settings.Load();

        if (args.Length == 0 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            return PrintAllConfig(settings);
        }

        string action = args[0].ToLowerInvariant();

        if (action == "get" && args.Length > 1)
        {
            return GetConfigValue(settings, args[1]);
        }

        if (action == "set" && args.Length > 2)
        {
            return SetConfigValue(settings, args[1], string.Join(" ", args.Skip(2)));
        }

        if (action == "reset")
        {
            var def = new Settings();
            def.Save();
            if (ConsoleRenderer.JsonMode)
            {
                ConsoleRenderer.WriteJson(new { status = "reset" });
            }
            else
            {
                ConsoleRenderer.Success("Configuration reset to defaults.");
            }
            return 0;
        }

        if (args.Length == 2 && !args[0].StartsWith("-"))
        {
            return SetConfigValue(settings, args[0], args[1]);
        }

        if (args.Length == 1 && !args[0].StartsWith("-"))
        {
            return GetConfigValue(settings, args[0]);
        }

        ConsoleRenderer.Error("Usage: config [list | get <key> | set <key> <value> | reset]");
        return 2;
    }

    private static int PrintAllConfig(Settings settings)
    {
        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new
            {
                port = settings.Port,
                save_folder = settings.SaveFolder,
                web_port = settings.WebPort,
                safe_mode = settings.SafeModeEnabled,
                auto_discovery = settings.AutoDiscoveryEnabled,
                require_accept = settings.RequireAcceptIncoming,
                device_name = settings.DeviceName,
                speed_limit_kbps = settings.SpeedLimitKBps,
                discovery_port = settings.DiscoveryPort,
                web_server_enabled = settings.WebServerEnabled
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.DarkMuted}{"KEY",-22} {"VALUE",-38} {"DESCRIPTION"}{ConsoleRenderer.Reset}");
        PrintRow("port", settings.Port.ToString(), "Local peer transfer port");
        PrintRow("save-folder", settings.SaveFolder, "Directory for received files");
        PrintRow("web-port", settings.WebPort.ToString(), "Port for mobile web server");
        PrintRow("safe-mode", settings.SafeModeEnabled ? "on" : "off", "PIN code verification for web");
        PrintRow("autodiscovery", settings.AutoDiscoveryEnabled ? "on" : "off", "Broadcast beacons in LAN");
        PrintRow("require-accept", settings.RequireAcceptIncoming ? "on" : "off", "Ask before receiving files");
        PrintRow("device-name", settings.DeviceName, "Name visible to peers");
        PrintRow("speed-limit", settings.SpeedLimitKBps > 0 ? $"{settings.SpeedLimitKBps} KB/s" : "0 (unlimited)", "Bandwidth throttle");
        PrintRow("web-server", settings.WebServerEnabled ? "on" : "off", "Auto-start mobile web service");
        Console.WriteLine();
        Console.WriteLine($"  {ConsoleRenderer.DarkMuted}Use 'config set <key> <value>' to update any setting.{ConsoleRenderer.Reset}");
        return 0;
    }

    private static int GetConfigValue(Settings settings, string key)
    {
        string norm = NormalizeKey(key);
        string? val = norm switch
        {
            "port" => settings.Port.ToString(),
            "save-folder" => settings.SaveFolder,
            "web-port" => settings.WebPort.ToString(),
            "safe-mode" => settings.SafeModeEnabled ? "on" : "off",
            "autodiscovery" => settings.AutoDiscoveryEnabled ? "on" : "off",
            "require-accept" => settings.RequireAcceptIncoming ? "on" : "off",
            "device-name" => settings.DeviceName,
            "speed-limit" => settings.SpeedLimitKBps.ToString(),
            "web-server" => settings.WebServerEnabled ? "on" : "off",
            _ => null
        };

        if (val == null)
        {
            ConsoleRenderer.Error($"Unknown configuration key '{key}'. Use 'config list' to see all keys.");
            return 2;
        }

        if (ConsoleRenderer.JsonMode)
        {
            ConsoleRenderer.WriteJson(new { key = norm, value = val });
        }
        else
        {
            Console.WriteLine($"  {ConsoleRenderer.MutedColor}{norm}:{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{val}{ConsoleRenderer.Reset}");
        }
        return 0;
    }

    private static int SetConfigValue(Settings settings, string key, string value)
    {
        string norm = NormalizeKey(key);
        bool success = true;

        switch (norm)
        {
            case "port":
                if (int.TryParse(value, out int p) && p is > 0 and <= 65535)
                {
                    settings.Port = p;
                }
                else
                {
                    ConsoleRenderer.Error("Port must be a number between 1 and 65535.");
                    return 2;
                }
                break;

            case "save-folder":
                string path = value.Trim().Trim('"');
                try
                {
                    if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                    settings.SaveFolder = Path.GetFullPath(path);
                }
                catch (Exception ex)
                {
                    ConsoleRenderer.Error($"Invalid folder path: {ex.Message}");
                    return 2;
                }
                break;

            case "web-port":
                if (int.TryParse(value, out int wp) && wp is > 0 and <= 65535)
                {
                    settings.WebPort = wp;
                }
                else
                {
                    ConsoleRenderer.Error("Web port must be between 1 and 65535.");
                    return 2;
                }
                break;

            case "safe-mode":
                settings.SafeModeEnabled = ParseBool(value);
                break;

            case "autodiscovery":
                settings.AutoDiscoveryEnabled = ParseBool(value);
                break;

            case "require-accept":
                settings.RequireAcceptIncoming = ParseBool(value);
                break;

            case "device-name":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    settings.DeviceName = value.Trim();
                }
                else
                {
                    ConsoleRenderer.Error("Device name cannot be empty.");
                    return 2;
                }
                break;

            case "speed-limit":
                if (int.TryParse(value, out int limit) && limit >= 0)
                {
                    settings.SpeedLimitKBps = limit;
                }
                else
                {
                    ConsoleRenderer.Error("Speed limit must be a positive integer (KB/s, 0 = unlimited).");
                    return 2;
                }
                break;

            case "web-server":
                settings.WebServerEnabled = ParseBool(value);
                break;

            default:
                ConsoleRenderer.Error($"Unknown configuration key '{key}'. Use 'config list' to see all keys.");
                return 2;
        }

        if (success)
        {
            settings.Save();
            if (ConsoleRenderer.JsonMode)
            {
                ConsoleRenderer.WriteJson(new { key = norm, value, status = "saved" });
            }
            else
            {
                ConsoleRenderer.Success($"Updated '{norm}' to '{value}'.");
            }
            return 0;
        }

        return 2;
    }

    private static string NormalizeKey(string k)
    {
        string lower = k.ToLowerInvariant().Replace("_", "-");
        return lower switch
        {
            "port" or "-p" => "port",
            "save-folder" or "savefolder" or "folder" or "dir" or "path" => "save-folder",
            "web-port" or "webport" => "web-port",
            "safe-mode" or "safemode" => "safe-mode",
            "autodiscovery" or "discovery" or "beacon" => "autodiscovery",
            "require-accept" or "requireaccept" or "accept" => "require-accept",
            "device-name" or "devicename" or "name" => "device-name",
            "speed-limit" or "speedlimit" or "throttle" => "speed-limit",
            "web-server" or "webserver" => "web-server",
            _ => lower
        };
    }

    private static bool ParseBool(string val)
    {
        string v = val.Trim().ToLowerInvariant();
        return v is "1" or "true" or "yes" or "on" or "enable" or "enabled";
    }

    private static void PrintRow(string key, string value, string desc)
    {
        Console.WriteLine($"  {ConsoleRenderer.BrandLight}{key,-22}{ConsoleRenderer.Reset} {ConsoleRenderer.TextLight}{value,-38}{ConsoleRenderer.Reset} {ConsoleRenderer.DarkMuted}{desc}{ConsoleRenderer.Reset}");
    }
}
