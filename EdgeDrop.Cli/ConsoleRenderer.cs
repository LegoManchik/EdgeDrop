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

    // ANSI Color Palettes (TrueColor / 24-bit with fallback)
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";

    public const string Brand = "\x1b[38;2;14;165;233m";      // Sky Blue #0EA5E9
    public const string BrandLight = "\x1b[38;2;56;189;248m"; // Light Sky #38BDF8
    public const string SuccessColor = "\x1b[38;2;16;185;129m"; // Emerald #10B981
    public const string WarningColor = "\x1b[38;2;245;158;11m"; // Amber #F59E0B
    public const string ErrorColor = "\x1b[38;2;244;63;94m";    // Rose Red #F43F5E
    public const string MutedColor = "\x1b[38;2;148;163;184m";  // Slate #94A3B8
    public const string DarkMuted = "\x1b[38;2;100;116;139m";   // Dark Slate #64748B
    public const string TextLight = "\x1b[38;2;241;245;249m";   // Soft White #F1F5F9

    private static bool _ansiEnabled = false;
    private static readonly object ConsoleLock = new();
    private static int _lastProgressLength = 0;

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
        if (Quiet) return;

        if (JsonMode)
        {
            WriteJsonEvent("progress", new
            {
                file = fileName,
                bytes = bytesSent,
                total = totalBytes,
                percent = Math.Round(ratio * 100, 1),
                speed_bps = Math.Round(bytesPerSec, 0),
                eta_sec = Math.Round(remainingSec, 0)
            });
            return;
        }

        lock (ConsoleLock)
        {
            int barWidth = 20;
            double clampedRatio = Math.Clamp(ratio, 0.0, 1.0);
            int filled = (int)(clampedRatio * barWidth);
            int empty = barWidth - filled;

            string speedText = FormatSpeed(bytesPerSec);
            string etaText = TimeSpan.FromSeconds(Math.Max(0, remainingSec)).ToString(@"mm\:ss");
            string percentText = $"{(clampedRatio * 100):F0}%";
            string name = Truncate(fileName, 18);

            string line;
            if (_ansiEnabled)
            {
                string barFilled = new string('█', filled);
                string barEmpty = new string('░', empty);
                line = $"\r  {name,-18} {Brand}{barFilled}{DarkMuted}{barEmpty}{Reset} {TextLight}{percentText,4}{Reset} {MutedColor}· {speedText,9} · {etaText}{Reset}";
            }
            else
            {
                string bar = new string('=', Math.Max(0, filled > 0 ? filled - 1 : 0))
                           + (filled > 0 ? ">" : "")
                           + new string(' ', empty);
                line = $"\r  {name,-18} [{bar}] {percentText,4} {speedText,9} {etaText}";
            }

            // Clean previous line overhang
            int visualLen = 2 + 18 + 1 + 20 + 1 + 4 + 3 + 9 + 3 + 5;
            if (visualLen < _lastProgressLength)
            {
                line += new string(' ', _lastProgressLength - visualLen);
            }
            _lastProgressLength = visualLen;

            Console.Write(line);

            if (clampedRatio >= 1.0)
            {
                Console.WriteLine();
                _lastProgressLength = 0;
            }
        }
    }

    public static void FinishProgress()
    {
        if (JsonMode || Quiet) return;
        lock (ConsoleLock)
        {
            if (_lastProgressLength > 0)
            {
                Console.WriteLine();
                _lastProgressLength = 0;
            }
        }
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
