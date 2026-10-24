using System;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace EdgeDrop;

public class HistoryItem
{
    private static readonly SolidColorBrush RedBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));
    private static readonly SolidColorBrush BlueBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush GreenBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0x10, 0x89, 0x3E));
    private static readonly SolidColorBrush ErrorTextBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0xFF, 0x52, 0x52));
    private static readonly SolidColorBrush SubtleBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));

    private static SolidColorBrush CreateFrozen(System.Windows.Media.Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public string FileName { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public bool IsIncoming { get; set; }
    public bool IsError { get; set; }
    public string? FilePath { get; set; }

    [JsonIgnore]
    public string DisplayTimestamp
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Timestamp)) return "";
            if (Timestamp.Contains('.') || Timestamp.Contains('-') || Timestamp.Contains('/'))
                return Timestamp;
            return $"{DateTime.Now:dd.MM.yyyy} {Timestamp}";
        }
    }

    [JsonIgnore]
    public string DisplayFileName => string.IsNullOrWhiteSpace(FileName)
        ? (IsError ? "Transfer error" : "File")
        : FileName;

    [JsonIgnore]
    public System.Windows.Media.Brush StatusForeground => IsError
        ? ErrorTextBrush
        : SubtleBrush;

    [JsonIgnore]
    public System.Windows.Media.Brush DirectionBackground => IsError
        ? RedBrush
        : (IsIncoming ? BlueBrush : GreenBrush);

    [JsonIgnore]
    public ImageSource? IconSource => IsError
        ? SvgIconHelper.LoadSvgIcon("error.svg", System.Windows.Media.Colors.White)
        : (IsIncoming
            ? SvgIconHelper.LoadSvgIcon("download.svg", System.Windows.Media.Colors.White)
            : SvgIconHelper.LoadSvgIcon("upload.svg", System.Windows.Media.Colors.White));
}
