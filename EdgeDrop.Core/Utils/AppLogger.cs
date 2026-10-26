using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace EdgeDrop;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Source { get; set; } = "App";
    public string Message { get; set; } = string.Empty;

    public string LevelDisplay => Level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Success => "OK",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERR",
        _ => "INFO"
    };

    public string FormattedLine => $"[{TimeFormatted}] [{LevelDisplay}] [{Source}] {Message}";
}

public static class AppLogger
{
    private static readonly ObservableCollection<LogEntry> _entries = new();
    private static readonly object _lock = new();
    private static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeDrop");
    private static readonly string LogFilePath = Path.Combine(LogDir, "app.log");
    public static ObservableCollection<LogEntry> Entries => _entries;
    public static event Action<LogEntry>? LogAdded;

    /// <summary>
    /// Optional thread dispatcher callback for UI frameworks (e.g. WPF Dispatcher).
    /// </summary>
    public static Action<Action>? DispatcherRunner { get; set; }

    public static void Log(string message, LogLevel level = LogLevel.Info, string source = "App")
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        };

        if (DispatcherRunner != null)
        {
            DispatcherRunner(() => AddEntry(entry));
        }
        else
        {
            AddEntry(entry);
        }
    }

    private static void AddEntry(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);

            if (_entries.Count > 1000)
            {
                _entries.RemoveAt(0);
            }

            try
            {
                if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogFilePath, entry.FormattedLine + Environment.NewLine);
            }
            catch { }
        }

        LogAdded?.Invoke(entry);
    }

    public static void Info(string msg, string source = "App") => Log(msg, LogLevel.Info, source);
    public static void Success(string msg, string source = "App") => Log(msg, LogLevel.Success, source);
    public static void Warning(string msg, string source = "App") => Log(msg, LogLevel.Warning, source);
    public static void Error(string msg, string source = "App") => Log(msg, LogLevel.Error, source);

    public static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public static string GetAllText()
    {
        lock (_lock)
        {
            return string.Join(Environment.NewLine, _entries.Select(e => e.FormattedLine));
        }
    }
}
