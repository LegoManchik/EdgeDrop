using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EdgeDrop.Models;

namespace EdgeDrop.Services;

public static class HistoryService
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EdgeDrop",
        "history.json");

    private static readonly object FileLock = new();

    public static List<HistoryRecord> Load(int maxCount = 100)
    {
        lock (FileLock)
        {
            try
            {
                if (!File.Exists(HistoryPath)) return new List<HistoryRecord>();

                string json = File.ReadAllText(HistoryPath);
                var items = JsonSerializer.Deserialize<List<HistoryRecord>>(json);
                return items != null ? items.Take(maxCount).ToList() : new List<HistoryRecord>();
            }
            catch
            {
                return new List<HistoryRecord>();
            }
        }
    }

    public static void Add(string fileName, string statusText, bool isIncoming, bool isError = false, string? filePath = null)
    {
        lock (FileLock)
        {
            try
            {
                var list = Load(200);
                list.Insert(0, new HistoryRecord
                {
                    FileName = fileName,
                    StatusText = statusText,
                    Timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
                    IsIncoming = isIncoming,
                    IsError = isError,
                    FilePath = filePath
                });

                while (list.Count > 200) list.RemoveAt(list.Count - 1);

                string dir = Path.GetDirectoryName(HistoryPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HistoryPath, json);
            }
            catch { }
        }
    }

    public static bool Clear()
    {
        lock (FileLock)
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    File.Delete(HistoryPath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
