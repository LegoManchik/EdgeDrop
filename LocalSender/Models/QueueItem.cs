using System;
using System.IO;

namespace EdgeDrop;

public class QueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = "";

    public string FileName => IsDirectory && !string.IsNullOrWhiteSpace(SourceDirectoryPath)

        ? (Path.GetFileName(SourceDirectoryPath.TrimEnd('\\', '/')) + ".zip")

        : Path.GetFileName(FilePath);

    public long FileSizeBytes { get; set; }
    public DeviceItem? TargetDevice { get; set; }
    public bool IsWebTarget { get; set; }
    public bool IsTemporaryZip { get; set; }
    public bool IsDirectory { get; set; }
    public string? SourceDirectoryPath { get; set; }

    public string FileSizeFormatted
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";

            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";

            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
