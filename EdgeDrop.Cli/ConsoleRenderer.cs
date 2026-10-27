using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeDrop.Cli;

public static class ConsoleRenderer
{
    public static bool JsonMode { get; set; } = false;
    public static bool Quiet { get; set; } = false;

    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";

    public const string Brand = "\x1b[38;2;14;165;233m";
    public const string BrandLight = "\x1b[38;2;56;189;248m";
    public const string SuccessColor = "\x1b[38;2;16;185;129m";
    public const string WarningColor = "\x1b[38;2;245;158;11m";
    public const string ErrorColor = "\x1b[38;2;244;63;94m";
    public const string MutedColor = "\x1b[38;2;148;163;184m";
    public const string DarkMuted = "\x1b[38;2;100;116;139m";
    public const string TextLight = "\x1b[38;2;241;245;249m";

    private static bool _ansiEnabled = false;
    private static readonly object ConsoleLock = new();

    static ConsoleRenderer()
    {
        TryEnableAnsi();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    private static void TryEnableAnsi()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (GetConsoleMode(handle, out uint mode))
                {
                    _ansiEnabled = SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                }
            }
            catch
            {
                _ansiEnabled = false;
            }
        }
        else
        {
            _ansiEnabled = true;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonLineOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void WriteJson(object data)
    {
        if (!JsonMode) return;
        lock (ConsoleLock)
        {
            string json = JsonSerializer.Serialize(data, JsonOpts);
            Console.WriteLine(json);
        }
    }

    public static void WriteJsonEvent(string eventName, object? details = null)
    {
        if (!JsonMode) return;
        lock (ConsoleLock)
        {
            var dict = new Dictionary<string, object?>
            {
                ["event"] = eventName,
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };

            if (details != null)
            {
                string json = JsonSerializer.Serialize(details, JsonLineOpts);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
            }

            Console.WriteLine(JsonSerializer.Serialize(dict, JsonLineOpts));
        }
    }

    public static void Info(string message)
    {
        if (Quiet) return;
        lock (ConsoleLock)
        {
            if (JsonMode)
            {
                Console.Error.WriteLine(message);
                return;
            }
            if (_ansiEnabled)
            {
                Console.WriteLine($"  {Brand}➜{Reset} {message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  ➜ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }
    }

    public static void Success(string message)
    {
        lock (ConsoleLock)
        {
            if (JsonMode)
            {
                Console.Error.WriteLine(message);
                return;
            }
            if (_ansiEnabled)
            {
                Console.WriteLine($"  {SuccessColor}✔{Reset} {message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  ✔ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }
    }

    public static void Warning(string message)
    {
        lock (ConsoleLock)
        {
            if (JsonMode)
            {
                Console.Error.WriteLine(message);
                return;
            }
            if (_ansiEnabled)
            {
                Console.WriteLine($"  {WarningColor}▲{Reset} {message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  ▲ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }
    }

    public static void Error(string message)
    {
        lock (ConsoleLock)
        {
            if (JsonMode)
            {
                Console.Error.WriteLine(message);
                return;
            }
            if (_ansiEnabled)
            {
                Console.WriteLine($"  {ErrorColor}✖{Reset} {message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  ✖ ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }
    }

    public static void Muted(string message)
    {
        if (Quiet || JsonMode) return;
        lock (ConsoleLock)
        {
            if (_ansiEnabled)
            {
                Console.WriteLine($"    {MutedColor}{message}{Reset}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    {message}");
                Console.ResetColor();
            }
        }
    }

    public static void RenderProgress(string fileName, double ratio, double bytesPerSec, double remainingSec, long bytesSent, long totalBytes)
    {
        ProgressRenderer.Render(fileName, ratio, bytesPerSec, remainingSec, bytesSent, totalBytes);
    }

    public static void FinishProgress()
    {
        ProgressRenderer.Finish();
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):F1} MB/s";
    }

    private static string Truncate(string str, int maxLen)
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLen) return str;
        return str.Substring(0, maxLen - 2) + "..";
    }
}
