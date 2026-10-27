namespace EdgeDrop.Models;

public class HistoryRecord
{
    public string FileName { get; set; } = "";
    public string StatusText { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public bool IsIncoming { get; set; }
    public bool IsError { get; set; }
    public string? FilePath { get; set; }
}
