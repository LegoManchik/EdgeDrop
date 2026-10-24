using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private double _currentTransferProgress = 0.0;
    private bool _hasTransferError = false;
    private bool _isFooterVisible = false;
    private TaskCompletionSource<bool>? _incomingFileTcs;
    private DispatcherTimer? _incomingFileTimer;
    private DispatcherTimer? _safeModeAcceptTimer;
    private int _incomingFileRemainingSec = 30;
    private string? _currentIncomingSenderIp;

    private void WireTransferServiceEvents()
    {
        _transferService.TransferStarted += fileName => Dispatcher.Invoke(() =>
        {
            _hasTransferError = false;

            CurrentSendingFileName.Text = fileName;

            if (OverlayEtaText != null) OverlayEtaText.Text = LocalizationManager.GetString("Str_Overlay_CalculatingEta");

            if (OverlaySpeedText != null) OverlaySpeedText.Text = "...";

            UpdateProgress(0);

            UpdateQueueDisplay();

            ShowTransferFooter(true);

            UpdateFooterPauseButtonVisual();
        });

        _transferService.TransferProgress += (fileName, report) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () =>
            {
                if (!_isFooterVisible)
                {
                    ShowTransferFooter(true);
                }

                CurrentSendingFileName.Text = fileName;

                UpdateProgressWithReport(report, fileName);
            });
        };

        _transferService.PauseChanged += isPaused => Dispatcher.Invoke(() =>
        {
            UpdateFooterPauseButtonVisual();

            if (isPaused)
            {
                if (OverlaySpeedText != null) OverlaySpeedText.Text = LocalizationManager.GetString("Str_Transfer_Paused", "Paused");
            }
        });

        _transferService.StatusChanged += msg => Dispatcher.Invoke(() => SetStatus(msg));

        _transferService.FileSent += (fileName, filePath) => Dispatcher.Invoke(() =>
        {
            AddHistoryRecord(fileName, LocalizationManager.GetString("Str_History_Sent"), isIncoming: false, filePath: filePath);

            _notifications.AddSent(filePath);

            AppLogger.Success($"File sent successfully: {fileName}", "Sender");

            UpdateQueueDisplay();
        });

        _transferService.FileReceived += (fileName, filePath) => Dispatcher.Invoke(() =>
        {
            SetStatus($"Received: {fileName}");

            AddHistoryRecord(fileName, LocalizationManager.GetString("Str_History_Received"), isIncoming: true, filePath: filePath);

            _notifications.AddReceived(filePath);

            AppLogger.Success($"File received: {fileName} -> {filePath}", "Receiver");
        });

        _transferService.TransferError += (fileName, errorMsg) => Dispatcher.Invoke(() => OnFileSendError(fileName, errorMsg));

        _transferService.TransferCompleted += () => Dispatcher.Invoke(async () =>
        {
            UpdateProgress(1.0, animate: false);

            if (!_hasTransferError)
            {
                CurrentSendingFileName.Text = LocalizationManager.GetString("Str_Overlay_Idle");
            }

            UpdateQueueDisplay();

            _notifications.FlushAll();

            await Task.Delay(350);

            if (!_transferService.IsProcessingQueue && _transferService.Queue.Count == 0 && !_transferService.IsReceiving)
            {
                ShowTransferFooter(false);

                UpdateProgress(0, animate: false);
            }
        });

        _transferService.AcceptFileRequested = (fileName, remoteIp, size) =>

            Dispatcher.InvokeAsync(() => ShowIncomingFileDialogAsync(fileName, remoteIp, size)).Task.Unwrap();
    }

    private void RestartEdges()
    {
        try
        {
            foreach (var edge in _edges)
            {
                try { edge.Close(); } catch { }
            }

            _edges.Clear();

            if (!_running) return;

            var devicesSnapshot = _settings.GetDevicesSnapshot();

            if (devicesSnapshot.Count == 0)
            {
                try
                {
                    var edge = new EdgeDropWindow(_settings, _transferService, null);
                    _edges.Add(edge);
                    edge.Show();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Failed to show default edge: {ex.Message}", "Edges");
                }
            }
            else
            {
                foreach (var dev in devicesSnapshot)
                {
                    try
                    {
                        var edge = new EdgeDropWindow(_settings, _transferService, dev);
                        _edges.Add(edge);
                        edge.Show();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Failed to show edge for {dev.Name}: {ex.Message}", "Edges");
                    }
                }
            }

            if (_settings.WebServerEnabled)
            {
                try
                {
                    var webEdge = new EdgeDropWindow(_settings, _transferService, null, isWebTarget: true, customEdge: _settings.WebDropEdge);
                    _edges.Add(webEdge);
                    webEdge.Show();
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"Failed to show web edge: {ex.Message}", "Edges");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"RestartEdges encountered an error: {ex.Message}", "Edges");
        }
    }

    private void OnFileSendError(string fileName, string errorMessage)
    {
        _hasTransferError = true;

        CurrentSendingFileName.Text = LocalizationManager.GetString("Str_History_Error");

        string recordTitle = string.IsNullOrWhiteSpace(fileName) ? LocalizationManager.GetString("Str_History_Error") : fileName;

        AddHistoryRecord(recordTitle, $"{LocalizationManager.GetString("Str_History_Error")}: {errorMessage}", isIncoming: false, isError: true);

        ShowNotification(LocalizationManager.GetString("Str_Notify_SendError", recordTitle), $"{recordTitle}: {errorMessage}");

        AppLogger.Error($"Failed to send file {recordTitle}: {errorMessage}", "Sender");

        UpdateQueueDisplay();
    }

    private void ShowTransferFooter(bool show)
    {
        if (TransferFooter == null || TransferFooterTransform == null) return;

        if (show)
        {
            if (_isFooterVisible && TransferFooter.Visibility == Visibility.Visible) return;

            _isFooterVisible = true;

            TransferFooter.Visibility = Visibility.Visible;

            var animY = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var animFade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

            TransferFooterTransform.BeginAnimation(TranslateTransform.YProperty, animY);

            TransferFooter.BeginAnimation(UIElement.OpacityProperty, animFade);
        }

        else
        {
            if (!_isFooterVisible && TransferFooter.Visibility == Visibility.Collapsed) return;

            _isFooterVisible = false;

            if (FooterQueueFlyout != null && _isQueueFlyoutOpen)
            {
                ToggleQueueFlyout(forceOpen: false);
            }

            var animY = new DoubleAnimation(80, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var animFade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));

            animY.Completed += (_, _) =>
            {
                if (!_isFooterVisible)
                {
                    TransferFooter.Visibility = Visibility.Collapsed;
                }
            };

            TransferFooterTransform.BeginAnimation(TranslateTransform.YProperty, animY);

            TransferFooter.BeginAnimation(UIElement.OpacityProperty, animFade);
        }
    }

    private void UpdateFooterPauseButtonVisual()
    {
        if (FooterPauseBtn == null) return;

        if (_transferService != null && _transferService.IsReceiving)
        {
            FooterPauseBtn.Visibility = Visibility.Collapsed;

            return;
        }

        FooterPauseBtn.Visibility = Visibility.Visible;

        var iconColor = ThemeManager.IconColor;

        bool isPaused = _transferService != null && _transferService.IsPaused;

        string iconName = isPaused ? "play.svg" : "pause.svg";

        var img = SvgIconHelper.LoadSvgIcon(iconName, iconColor);

        if (img != null && FooterPauseIconImage != null)
        {
            FooterPauseIconImage.Source = img;
        }

        FooterPauseBtn.ToolTip = isPaused

            ? LocalizationManager.GetString("Str_Transfer_Resume", "Resume")

            : LocalizationManager.GetString("Str_Transfer_Pause", "Pause");
    }

    private void OnFooterPauseClick(object sender, RoutedEventArgs e)
    {
        _transferService.TogglePause();

        UpdateFooterPauseButtonVisual();
    }

    private void OnFooterCancelClick(object sender, RoutedEventArgs e)
    {
        _transferService.CancelAllTransfers();
    }

    private bool _isQueueFlyoutOpen = false;

    private void OnFooterQueueToggleClick(object sender, RoutedEventArgs e)
    {
        ToggleQueueFlyout();
    }

    private void ToggleQueueFlyout(bool? forceOpen = null)
    {
        if (FooterQueueFlyout == null || FooterQueueTransform == null || FooterQueueScale == null) return;

        bool targetState = forceOpen ?? !_isQueueFlyoutOpen;

        if (targetState == _isQueueFlyoutOpen && (targetState == (FooterQueueFlyout.Visibility == Visibility.Visible))) return;

        _isQueueFlyoutOpen = targetState;

        if (_isQueueFlyoutOpen)
        {
            FooterQueueFlyout.Visibility = Visibility.Visible;

            var animFade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var animY = new DoubleAnimation(12.0, 0.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var animScale = new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            FooterQueueFlyout.BeginAnimation(UIElement.OpacityProperty, animFade);

            FooterQueueTransform.BeginAnimation(TranslateTransform.YProperty, animY);

            FooterQueueScale.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);

            FooterQueueScale.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);
        }

        else
        {
            var animFade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var animY = new DoubleAnimation(10.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var animScale = new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            animFade.Completed += (_, _) =>
            {
                if (!_isQueueFlyoutOpen)
                {
                    FooterQueueFlyout.Visibility = Visibility.Collapsed;
                }
            };

            FooterQueueFlyout.BeginAnimation(UIElement.OpacityProperty, animFade);

            FooterQueueTransform.BeginAnimation(TranslateTransform.YProperty, animY);

            FooterQueueScale.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);

            FooterQueueScale.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);
        }
    }

    private void UpdateProgress(double progressRatio, bool animate = true)
    {
        _currentTransferProgress = Math.Clamp(progressRatio, 0.0, 1.0);

        double targetVal = _currentTransferProgress * 100.0;

        int percent = (int)Math.Round(targetVal);

        if (TransferProgressBar != null)
        {
            if (animate && targetVal > 0 && targetVal < 100 && Math.Abs(TransferProgressBar.Value - targetVal) > 0.05)
            {
                var anim = new DoubleAnimation
                {
                    To = targetVal,
                    Duration = TimeSpan.FromMilliseconds(50),

                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                };

                TransferProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, anim);
            }

            else
            {
                TransferProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);

                TransferProgressBar.Value = targetVal;
            }
        }

        if (ExpandedPercentText != null) ExpandedPercentText.Text = $"{percent}%";
    }

    private void UpdateProgressWithReport(TransferProgressReport report, string fileName)
    {
        UpdateProgress(report.Ratio);

        string etaStr;

        if (report.RemainingSeconds < 1.0)
        {
            etaStr = LocalizationManager.GetString("Str_Eta_LessThanSec");
        }

        else if (report.RemainingSeconds < 60.0)
        {
            int sec = (int)Math.Ceiling(report.RemainingSeconds);

            etaStr = LocalizationManager.GetString("Str_Eta_Seconds", sec);
        }

        else
        {
            int min = (int)(report.RemainingSeconds / 60.0);

            int sec = (int)(report.RemainingSeconds % 60.0);

            etaStr = LocalizationManager.GetString("Str_Eta_MinSec", min, sec);
        }

        if (OverlayEtaText != null) OverlayEtaText.Text = etaStr;

        string speedStr;

        double mbSec = report.BytesPerSecond / (1024.0 * 1024.0);

        if (mbSec >= 1.0)

            speedStr = $"{mbSec:F1} MB/s";

        else

            speedStr = $"{report.BytesPerSecond / 1024.0:F0} KB/s";

        if (OverlaySpeedText != null) OverlaySpeedText.Text = speedStr;

        if (QueueCountText != null && _transferService != null && _transferService.IsReceiving)
        {
            QueueCountText.Text = $"{FormatFileSizeBytes(report.BytesSent)} / {FormatFileSizeBytes(report.TotalBytes)}";
        }
    }

    private void UpdateQueueDisplay()
    {
        int count = _transferService.Queue.Count;

        if (QueueCountText != null)
        {
            if (_transferService != null && _transferService.IsReceiving)
            {
                QueueCountText.Text = LocalizationManager.GetString("Str_Transfer_ReceivingFile", "Receiving file...");
            }

            else
            {
                QueueCountText.Text = LocalizationManager.GetString("Str_Overlay_QueueCount", count);
            }
        }

        if (FooterQueueBtn != null)
        {
            FooterQueueBtn.Opacity = 1.0;
        }

        if (count > 0 || (_transferService != null && _transferService.IsReceiving))
        {
            ShowTransferFooter(true);
        }
    }

    private static string FormatFileSizeBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";

        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";

        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";

        return $"{bytes} B";
    }

    private async Task<bool> ShowIncomingFileDialogAsync(string fileName, string remoteIp, long fileSize)
    {
        if (SenderApprovalManager.IsApproved(remoteIp))
        {
            return true;
        }

        if (IncomingFileOverlay == null) return true;

        var dev = _settings.GetDevicesSnapshot().FirstOrDefault(d => string.Equals(d.Ip?.Trim(), remoteIp.Trim(), StringComparison.OrdinalIgnoreCase));

        string senderLabel = dev != null ? $"{dev.Name} ({remoteIp})" : remoteIp;

        string sizeStr = FormatFileSizeBytes(fileSize);

        _currentIncomingSenderIp = remoteIp;

        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        bool isDangerous = FileReceiver.IsDangerousFile(fileName);

        string promptTemplate = LocalizationManager.GetString("Str_Incoming_Prompt");

        if (IncomingFilePromptText != null)
        {
            string basePrompt = string.Format(promptTemplate, fileName, sizeStr, senderLabel);

            if (isDangerous)
            {
                basePrompt += "\n⚠️ " + LocalizationManager.GetString("Str_Transfer_DangerousWarning", "Warning: executable file! Running unverified programs may be dangerous.");
            }

            IncomingFilePromptText.Text = basePrompt;
        }

        _incomingFileRemainingSec = 30;

        UpdateIncomingFileTimeoutText();

        _incomingFileTcs = new TaskCompletionSource<bool>();

        _safeModeAcceptTimer?.Stop();

        if (isDangerous && _settings.SafeModeEnabled)
        {
            if (IncomingFileAcceptBtn != null)
            {
                IncomingFileAcceptBtn.IsEnabled = false;

                int delaySec = 5;

                string acceptText = LocalizationManager.GetString("Str_Incoming_Accept");

                IncomingFileAcceptBtn.Content = $"{acceptText} ({delaySec})";

                _safeModeAcceptTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

                _safeModeAcceptTimer.Tick += (_, _) =>
                {
                    delaySec--;

                    if (delaySec <= 0)
                    {
                        _safeModeAcceptTimer?.Stop();

                        IncomingFileAcceptBtn.IsEnabled = true;

                        IncomingFileAcceptBtn.Content = acceptText;
                    }

                    else
                    {
                        IncomingFileAcceptBtn.Content = $"{acceptText} ({delaySec})";
                    }
                };

                _safeModeAcceptTimer.Start();
            }
        }

        else if (IncomingFileAcceptBtn != null)
        {
            IncomingFileAcceptBtn.IsEnabled = true;

            IncomingFileAcceptBtn.Content = LocalizationManager.GetString("Str_Incoming_Accept");
        }

        _incomingFileTimer?.Stop();

        _incomingFileTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _incomingFileTimer.Tick += (_, _) =>
        {
            _incomingFileRemainingSec--;

            if (_incomingFileRemainingSec <= 0)
            {
                _safeModeAcceptTimer?.Stop();

                _incomingFileTimer?.Stop();

                AnimateModalClose(IncomingFileOverlay, IncomingFileModalCard);

                _incomingFileTcs?.TrySetResult(false);
            }

            else
            {
                UpdateIncomingFileTimeoutText();
            }
        };

        AnimateModalOpen(IncomingFileOverlay, IncomingFileModalCard);

        _notifications.IsIncomingRequestPending = true;

        _notifications.IncomingRequestClickAction = () => Dispatcher.Invoke(() =>
        {
            ShowFromTray();

            WindowState = WindowState.Normal;

            Activate();

            Topmost = true;

            Topmost = false;

            Focus();
        });

        if (_settings.NotificationsEnabled && _notifyIcon != null)
        {
            try
            {
                string notifyTitle = LocalizationManager.GetString("Str_Incoming_Title");

                string notifyMsg = string.Format(LocalizationManager.GetString("Str_Incoming_NotifyPrompt"), fileName, sizeStr, senderLabel);

                _notifyIcon.ShowBalloonTip(15000, notifyTitle, notifyMsg, Forms.ToolTipIcon.None);
            }

            catch { }
        }

        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Topmost = true;

            Topmost = false;

            Activate();

            Focus();
        }

        _incomingFileTimer.Start();

        return await _incomingFileTcs.Task;
    }

    private void UpdateIncomingFileTimeoutText()
    {
        if (IncomingFileTimeoutText != null)
        {
            IncomingFileTimeoutText.Text = string.Format(LocalizationManager.GetString("Str_Incoming_Timeout"), _incomingFileRemainingSec);
        }
    }

    private void OnIncomingFileAcceptClick(object sender, RoutedEventArgs e)
    {
        _safeModeAcceptTimer?.Stop();

        _incomingFileTimer?.Stop();

        _notifications.IsIncomingRequestPending = false;

        AnimateModalClose(IncomingFileOverlay, IncomingFileModalCard);

        SenderApprovalManager.Approve(_currentIncomingSenderIp);

        _transferService.ApproveSender(_currentIncomingSenderIp);

        _incomingFileTcs?.TrySetResult(true);
    }

    private void OnIncomingFileRejectClick(object sender, RoutedEventArgs e)
    {
        _safeModeAcceptTimer?.Stop();

        _incomingFileTimer?.Stop();

        _notifications.IsIncomingRequestPending = false;

        AnimateModalClose(IncomingFileOverlay, IncomingFileModalCard);

        SenderApprovalManager.Revoke(_currentIncomingSenderIp);

        _transferService.RevokeSenderApproval(_currentIncomingSenderIp);

        _incomingFileTcs?.TrySetResult(false);
    }

    private void OnSendFileClick(object sender, RoutedEventArgs e)
    {
        DeviceItem? device = null;

        if (sender is FrameworkElement fe && fe.DataContext is DeviceItem dev)
        {
            device = dev;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,

            Title = LocalizationManager.GetString("Str_Dialog_SelectFiles")
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            if (!_running) Start();

            _transferService.SendFiles(device, dialog.FileNames, isWebTarget: false);
        }
    }

    public void PickAndSendFilesToDevice(DeviceItem dev)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,

            Title = LocalizationManager.GetString("Str_Dialog_SelectFiles")
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            if (!_running) Start();

            _transferService.SendFiles(dev, dialog.FileNames, isWebTarget: false);
        }
    }

    public void PickAndSendFilesToWebSession(ActiveSessionInfo sess)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,

            Title = LocalizationManager.GetString("Str_Dialog_SelectFiles")
        };

        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            if (!_running) Start();

            _transferService.SendFiles(null, dialog.FileNames, isWebTarget: true);
        }
    }
}
