using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeDrop.Cli;

public static class ConsoleRenderer
{
    public static bool JsonMode { get; set; } = false;
    public static bool Quiet { get; set; } = false;

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

    private static readonly object ConsoleLock = new();
    private static int _lastProgressLength = 0;

    public static void WriteJson(object data)
    {
        lock (ConsoleLock)
        {
            string json = JsonSerializer.Serialize(data, JsonOpts);
            Console.WriteLine(json);
        }
    }

    public static void WriteJsonEvent(string eventName, object? details = null)
    {
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
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("--> ");
            Console.ResetColor();
            Console.WriteLine(message);
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
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[OK] ");
            Console.ResetColor();
            Console.WriteLine(message);
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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[WARN] ");
            Console.ResetColor();
            Console.WriteLine(message);
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
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ERROR] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }
    }

    public static void Muted(string message)
    {
        if (Quiet || JsonMode) return;
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(message);
            Console.ResetColor();
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
            int barWidth = 24;
            double clampedRatio = Math.Clamp(ratio, 0.0, 1.0);
            int filled = (int)(clampedRatio * barWidth);
            int empty = barWidth - filled;

            string bar = new string('=', Math.Max(0, filled > 0 ? filled - 1 : 0))
                       + (filled > 0 ? ">" : "")
                       + new string(' ', empty);

            string speedText = FormatSpeed(bytesPerSec);
            string etaText = TimeSpan.FromSeconds(Math.Max(0, remainingSec)).ToString(@"mm\:ss");
            string percentText = $"{(clampedRatio * 100):F1}%";

            string line = $"\r  {Truncate(fileName, 20),-20} [{bar}] {percentText,6} ({speedText,9}) ETA: {etaText}";
            if (line.Length < _lastProgressLength)
            {
                line = line.PadRight(_lastProgressLength);
            }
            _lastProgressLength = line.Length;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(line);
            Console.ResetColor();

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
        return str.Substring(0, maxLen - 3) + "...";
    }
}
