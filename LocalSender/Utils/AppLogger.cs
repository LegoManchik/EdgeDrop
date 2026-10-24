using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

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

    private static readonly SolidColorBrush InfoBrush = CreateFrozenBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush SuccessBrush = CreateFrozenBrush(Color.FromRgb(0x54, 0xB0, 0x54));
    private static readonly SolidColorBrush WarningBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x8C, 0x00));
    private static readonly SolidColorBrush ErrorBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x52, 0x52));
    private static readonly SolidColorBrush DefaultBrush = CreateFrozenBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var b = new SolidColorBrush(color);

        b.Freeze();

        return b;
    }

    public System.Windows.Media.Brush LevelBrush => Level switch
    {
        LogLevel.Info => InfoBrush,
        LogLevel.Success => SuccessBrush,
        LogLevel.Warning => WarningBrush,
        LogLevel.Error => ErrorBrush,

        _ => DefaultBrush
    };

    public string FormattedLine => $"[{TimeFormatted}] [{LevelDisplay}] [{Source}] {Message}";
}

public static class AppLogger
{
    private static readonly ObservableCollection<LogEntry> _entries = new();
    private static readonly object _lock = new();
    public static ObservableCollection<LogEntry> Entries => _entries;
    public static event Action<LogEntry>? LogAdded;

    public static void Log(string message, LogLevel level = LogLevel.Info, string source = "App")
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,

            Message = message
        };

        if (System.Windows.Application.Current?.Dispatcher != null &&

            !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => AddEntry(entry));
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
