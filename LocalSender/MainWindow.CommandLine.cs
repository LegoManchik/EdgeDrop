using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    public void HandleCommandLineArgs(string[] args)
    {
        try
        {
            if (args.Length == 0) return;

            int sendToIndex = -1;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--send-to", StringComparison.OrdinalIgnoreCase))
                {
                    sendToIndex = i;

                    break;
                }
            }

            if (sendToIndex >= 0 && sendToIndex + 2 < args.Length)
            {
                string target = args[sendToIndex + 1];

                string filePath = args[sendToIndex + 2];

                if (!File.Exists(filePath) && !Directory.Exists(filePath)) return;

                if (string.Equals(target, "select", StringComparison.OrdinalIgnoreCase))
                {
                    ShowFromTray();

                    WindowState = WindowState.Normal;

                    Activate();

                    Focus();

                    return;
                }

                if (target.StartsWith("web", StringComparison.OrdinalIgnoreCase))
                {
                    if (_web != null && _web.HasActiveSessions)
                    {
                        string fileName = Path.GetFileName(filePath);

                        _web.OfferFileToPhone(filePath);

                        AddHistoryRecord(fileName, LocalizationManager.GetString("Str_History_Sent"), isIncoming: false, filePath: filePath);

                        _notifications.AddSent(filePath);

                        UpdateQueueDisplay();

                        ShowToast(isTesting: false, isSuccess: true, text: string.Format(LocalizationManager.GetString("Str_Notify_SendSuccess"), fileName));
                    }

                    else
                    {
                        ShowFromTray();

                        WindowState = WindowState.Normal;

                        Activate();

                        ShowToast(isTesting: false, isSuccess: false, text: LocalizationManager.GetString("Str_Web_NoActiveSessions"));
                    }

                    return;
                }

                string targetIp = target;

                if (target.Contains(':'))
                {
                    targetIp = target.Split(':')[0];
                }

                if (!_running) Start();

                var edge = _edges.FirstOrDefault(e => e.Device != null && string.Equals(e.Device.Ip?.Trim(), targetIp.Trim(), StringComparison.OrdinalIgnoreCase))

                           ?? _edges.FirstOrDefault(e => !e.IsWebTarget);

                if (edge != null)
                {
                    edge.EnqueueFiles(new[] { filePath });
                }
            }
        }

        catch (Exception ex)
        {
            AppLogger.Error($"Command line handler error: {ex.Message}", "App");
        }
    }
}
