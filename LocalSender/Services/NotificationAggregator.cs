using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace EdgeDrop;

public enum TransferDirection
{
    Received,
    Sent
}

public class NotificationAggregator
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;
    private readonly Settings _settings;
    private Forms.NotifyIcon? _notifyIcon;
    private readonly object _lock = new();

    public Action? IncomingRequestClickAction { get; set; }
    public bool IsIncomingRequestPending { get; set; }

    private class BatchState
    {
        public readonly List<string> Files = new();
        public System.Timers.Timer Timer;

        public BatchState(Action onFlush)
        {
            Timer = new System.Timers.Timer(1500) { AutoReset = false };

            Timer.Elapsed += (_, _) => onFlush();
        }

        public void Add(string filePath)
        {
            Timer.Stop();

            Files.Add(filePath);

            Timer.Start();
        }

        public List<string> Reset()
        {
            Timer.Stop();

            var res = new List<string>(Files);

            Files.Clear();

            return res;
        }
    }

    private readonly Dictionary<(TransferDirection, string), BatchState> _batches = new();
    private string? _lastNotificationPath;
    public event Action<string>? StatusMessage;

    public NotificationAggregator(Settings settings)
    {
        _settings = settings;
    }

    public void FlushAll()
    {
        List<(TransferDirection dir, string dev)> keys;

        lock (_lock)
        {
            keys = _batches.Keys.ToList();
        }

        foreach (var key in keys)
        {
            Flush(key.dir, key.dev);
        }
    }

    public void AttachNotifyIcon(Forms.NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;

        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            if (IsIncomingRequestPending)
            {
                IsIncomingRequestPending = false;

                IncomingRequestClickAction?.Invoke();

                return;
            }

            string? path;

            lock (_lock)
            {
                path = _lastNotificationPath;
            }

            if (!string.IsNullOrEmpty(path))
            {
                OpenInExplorer(path);
            }
        };
    }

    public void AddReceived(string filePath, string? deviceName = null)
    {
        lock (_lock)
        {
            string keyDevice = string.IsNullOrWhiteSpace(deviceName) ? "default" : deviceName.Trim();

            var key = (TransferDirection.Received, keyDevice);

            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = new BatchState(() => Flush(TransferDirection.Received, keyDevice));

                _batches[key] = batch;
            }

            batch.Add(filePath);
        }
    }

    public void AddSent(string filePath, string? deviceName = null)
    {
        lock (_lock)
        {
            string keyDevice = string.IsNullOrWhiteSpace(deviceName) ? "default" : deviceName.Trim();

            var key = (TransferDirection.Sent, keyDevice);

            if (!_batches.TryGetValue(key, out var batch))
            {
                batch = new BatchState(() => Flush(TransferDirection.Sent, keyDevice));

                _batches[key] = batch;
            }

            batch.Add(filePath);
        }
    }

    private void Flush(TransferDirection direction, string deviceName)
    {
        List<string> files;

        lock (_lock)
        {
            var key = (direction, deviceName);

            if (!_batches.TryGetValue(key, out var batch)) return;

            files = batch.Reset();

            if (files.Count == 0) return;

            _lastNotificationPath = files[^1];
        }

        int totalCount = files.Count;

        string firstFileName = Path.GetFileName(files[0]);

        string maskedFirst = firstFileName;

        string deviceTitle = (deviceName == "default" || string.IsNullOrWhiteSpace(deviceName)) ? "" : $" ({deviceName})";

        string title;

        string body;

        bool isRu = LocalizationManager.EffectiveLanguage == AppLanguage.Russian;

        if (totalCount == 1)
        {
            title = direction == TransferDirection.Received

                ? (isRu ? LocalizationManager.GetString("Str_Notification_FileReceived", "File received") : "File received") + deviceTitle

                : (isRu ? LocalizationManager.GetString("Str_Notification_FileSent", "File sent") : "File sent") + deviceTitle;

            body = maskedFirst;
        }

        else
        {
            title = direction == TransferDirection.Received

                ? (isRu ? string.Format(LocalizationManager.GetString("Str_Notification_FilesReceived", "Files received: {0}"), totalCount) : $"Files received: {totalCount}") + deviceTitle

                : (isRu ? string.Format(LocalizationManager.GetString("Str_Notification_FilesSent", "Files sent: {0}"), totalCount) : $"Files sent: {totalCount}") + deviceTitle;

            body = isRu

                ? $"{maskedFirst} " + string.Format(LocalizationManager.GetString("Str_Notification_AndMore", "and {0} more"), totalCount - 1) + " " + GetPluralFiles(totalCount - 1)

                : $"{maskedFirst} and {totalCount - 1} more";
        }

        StatusMessage?.Invoke(direction == TransferDirection.Received

            ? (isRu ? string.Format(LocalizationManager.GetString("Str_Notification_ReceivedPrefix", "Received: {0}"), body) : $"Received: {body}")

            : (isRu ? string.Format(LocalizationManager.GetString("Str_Notification_SentPrefix", "Sent: {0}"), body) : $"Sent: {body}"));

        if (_settings.NotificationsEnabled && _notifyIcon != null)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(3500, title, body, Forms.ToolTipIcon.None);
            }

            catch { }
        }
    }

    private static string GetPluralFiles(int n)
    {
        int rem100 = n % 100;

        int rem10 = n % 10;

        if (rem100 is >= 11 and <= 19) return LocalizationManager.GetString("Str_Plural_Files", "files");

        if (rem10 == 1) return LocalizationManager.GetString("Str_Plural_File", "file");

        if (rem10 is >= 2 and <= 4) return LocalizationManager.GetString("Str_Plural_Files2", "files");

        return LocalizationManager.GetString("Str_Plural_Files", "files");
    }

    public static string MaskFileName(string fileName)
    {
        return fileName ?? "";
    }

    public static void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => OpenInExplorerInternal(path));

            return;
        }

        OpenInExplorerInternal(path);
    }

    private static void OpenInExplorerInternal(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            string targetDir = Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? "");

            string fileName = File.Exists(path) ? Path.GetFileName(path) : "";

            bool handledInExistingWindow = false;

            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");

                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);

                    dynamic? windows = shell?.Windows();

                    if (windows is not null)
                    {
                        dynamic? targetWindow = null;

                        bool exactFolderMatch = false;

                        foreach (dynamic w in windows)
                        {
                            try
                            {
                                string docUrl = (string)(w.LocationURL ?? "");

                                if (!string.IsNullOrEmpty(docUrl) && docUrl.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                                {
                                    var uri = new Uri(docUrl);

                                    string localPath = uri.LocalPath.TrimEnd('\\', '/');

                                    if (string.Equals(localPath, targetDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                                    {
                                        targetWindow = w;

                                        exactFolderMatch = true;

                                        break;
                                    }

                                    targetWindow ??= w;
                                }
                            }

                            catch { }
                        }

                        if (targetWindow != null)
                        {
                            IntPtr hwnd = (IntPtr)targetWindow.HWND;

                            ShowWindow(hwnd, SW_RESTORE);

                            SetForegroundWindow(hwnd);

                            if (!exactFolderMatch && !string.IsNullOrEmpty(targetDir))
                            {
                                targetWindow.Navigate2(targetDir);
                            }

                            if (!string.IsNullOrEmpty(fileName))
                            {
                                void SelectFile()
                                {
                                    try
                                    {
                                        dynamic doc = targetWindow.Document;

                                        dynamic folder = doc.Folder;

                                        dynamic item = folder.ParseName(fileName);

                                        if (item != null)
                                        {
                                            doc.SelectItem(item, 1 | 8 | 16);
                                        }
                                    }

                                    catch { }
                                }

                                if (exactFolderMatch)
                                {
                                    SelectFile();
                                }

                                else
                                {
                                    var timer = new System.Windows.Threading.DispatcherTimer
                                    {
                                        Interval = TimeSpan.FromMilliseconds(250)
                                    };

                                    timer.Tick += (_, _) =>
                                    {
                                        timer.Stop();

                                        SelectFile();
                                    };

                                    timer.Start();
                                }
                            }

                            handledInExistingWindow = true;
                        }
                    }
                }
            }

            catch { }

            if (!handledInExistingWindow)
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",

                        UseShellExecute = true
                    });
                }

                else if (Directory.Exists(targetDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{targetDir}\"",

                        UseShellExecute = true
                    });
                }
            }
        }

        catch { }
    }
}
