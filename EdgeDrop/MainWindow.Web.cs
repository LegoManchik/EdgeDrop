using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private void DisplayLocalIp()
    {
        try
        {
            string myIp = GetLocalIp();

            if (myIp == "?") myIp = "127.0.0.1";

            if (DevicesThisPcIpText != null) DevicesThisPcIpText.Text = myIp;

            UpdateWebQrCode();
        }

        catch
        {
            if (DevicesThisPcIpText != null) DevicesThisPcIpText.Text = "127.0.0.1";
        }
    }

    public static string GetLocalIp()
    {
        return EdgeDrop.Core.Utils.NetworkHelper.GetPrimaryLocalIp();
    }

    private void OnWebChevronClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        ToggleWebCardCollapse();
    }

    private void OnWebHeaderClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var current = dep;

            while (current != null && current != sender)
            {
                if (current is System.Windows.Controls.Primitives.ToggleButton)

                    return;

                current = VisualTreeHelper.GetParent(current);
            }
        }

        ToggleWebCardCollapse();
    }

    private void OnWebToggleSwitchClick(object sender, RoutedEventArgs e)
    {
        if (WebToggleSwitch == null) return;

        bool enabled = WebToggleSwitch.IsChecked == true;

        _settings.WebServerEnabled = enabled;

        _settings.Save();

        UpdateWebButtonState();

        if (_running)
        {
            Stop();

            Start();
        }
    }

    private async Task StartWebServerAsync()
    {
        try
        {
            await StopWebServerAsync();

            int port = _settings.WebPort;

            if (WebPortBox != null && int.TryParse(WebPortBox.Text.Trim(), out var p) && p > 0 && p <= 65535)
            {
                port = p;

                _settings.WebPort = p;
            }

            var web = new WebServer(port, _settings.SaveFolder);

            web.RequireAcceptIncoming = _settings.RequireAcceptIncoming;

            web.SafeModeEnabled = _settings.SafeModeEnabled;

            web.AcceptFileRequested = (fileName, remoteIp, size) =>

                Dispatcher.InvokeAsync(() => ShowIncomingFileDialogAsync(fileName, remoteIp, size)).Task.Unwrap();

            web.Uploaded += path => Dispatcher.Invoke(() =>
            {
                string fileName = System.IO.Path.GetFileName(path);

                AddHistoryRecord(fileName, LocalizationManager.GetString("Str_History_Received"), isIncoming: true, filePath: path);

                _notifications.AddReceived(path);

                ShowPingToast(isTesting: false, text: LocalizationManager.GetString("Str_Web_UploadSuccess", fileName), isSuccess: true);

                AppLogger.Success($"File received via web: {fileName} -> {path}", "Web");
            });

            web.Error += msg => Dispatcher.Invoke(() =>
            {
                ShowPingToast(isTesting: false, text: string.Format(LocalizationManager.GetString("Str_Web_ServerError"), msg), isSuccess: false);

                AppLogger.Error($"Web server error: {msg}", "Web");
            });

            web.ActiveSessionsChanged += () => Dispatcher.Invoke(RefreshDevicesUi);

            await web.StartAsync();

            _web = web;

            _transferService.AttachWebServer(web);

            UpdateWebButtonState();

            UpdateWebQrCode();

            RefreshDevicesUi();

            AppLogger.Info($"Web server started on http://0.0.0.0:{port}", "Web");
        }

        catch (Exception ex)
        {
            UpdateWebButtonState();

            ShowPingToast(isTesting: false, text: string.Format(LocalizationManager.GetString("Str_Web_StartError"), ex.Message), isSuccess: false);

            AppLogger.Error($"Web server start error: {ex.Message}", "Web");
        }
    }

    private void StartWebServer()
    {
        _ = StartWebServerAsync();
    }

    private async Task StopWebServerAsync()
    {
        var web = _web;

        _web = null;

        _transferService.DetachWebServer();

        UpdateWebButtonState();

        if (web != null)
        {
            AppLogger.Info("Web server stopped", "Web");

            try
            {
                await web.StopAsync();
            }

            catch { }
        }

        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            Dispatcher.Invoke(RefreshDevicesUi);
        }
    }

    private void StopWebServer()
    {
        var web = _web;

        _web = null;

        _transferService.DetachWebServer();

        UpdateWebButtonState();

        if (web != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await web.StopAsync();
                }

                catch { }

                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    Dispatcher.Invoke(RefreshDevicesUi);
                }
            });
        }

        else
        {
            RefreshDevicesUi();
        }
    }

    private void UpdateWebButtonState()
    {
        if (WebToggleSwitch != null)
        {
            WebToggleSwitch.IsChecked = _settings.WebServerEnabled;

            WebToggleSwitch.ToolTip = _settings.WebServerEnabled

                ? LocalizationManager.GetString("Str_Web_ToggleEnabled")

                : LocalizationManager.GetString("Str_Web_ToggleDisabled");
        }

        if (WebQrContainer != null)
        {
            bool isActivelyRunning = _running && _settings.WebServerEnabled && _web != null && _web.IsRunning;

            WebQrContainer.Opacity = isActivelyRunning ? 1.0 : 0.45;
        }

        UpdateWebPinDisplay();
    }

    private bool _isPinRevealed = false;
    private bool _pinClickAttached = false;

    private void UpdateWebPinDisplay()
    {
        bool isActivelyRunning = _running && _settings.WebServerEnabled && _web != null && _web.IsRunning;

        if (WebPinText != null)
        {
            if (!_pinClickAttached)
            {
                _pinClickAttached = true;

                WebPinText.MouseLeftButtonUp += (s, e) =>
                {
                    if (_running && _settings.WebServerEnabled && _web != null && _web.IsRunning)
                    {
                        _isPinRevealed = !_isPinRevealed;

                        AnimatePinToggle();

                        UpdateWebPinDisplay();
                    }
                };
            }

            if (isActivelyRunning)
            {
                WebPinText.Text = _isPinRevealed ? _web!.Pin : "••••";

                WebPinText.Cursor = System.Windows.Input.Cursors.Hand;

                WebPinText.ToolTip = _isPinRevealed

                    ? LocalizationManager.GetString("Str_Web_HidePinTooltip", "Click to hide PIN")

                    : LocalizationManager.GetString("Str_Web_ShowPinTooltip", "Click to show PIN");
            }

            else
            {
                WebPinText.Text = "----";

                WebPinText.Cursor = System.Windows.Input.Cursors.Arrow;

                WebPinText.ToolTip = null;
            }

            WebPinText.Opacity = isActivelyRunning ? 1.0 : 0.45;
        }

        if (WebRegeneratePinBtn != null)
        {
            WebRegeneratePinBtn.IsEnabled = isActivelyRunning;

            WebRegeneratePinBtn.Opacity = isActivelyRunning ? 1.0 : 0.45;
        }
    }

    private void AnimatePinToggle()
    {
        if (WebPinScale != null)
        {
            var anim = new DoubleAnimation(0.75, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
            };

            WebPinScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);

            WebPinScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        if (WebPinText != null)
        {
            var fade = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            WebPinText.BeginAnimation(OpacityProperty, fade);
        }
    }

    private CancellationTokenSource? _pinCounterCts;

    private async void OnRegeneratePinClick(object sender, RoutedEventArgs e)
    {
        if (_web != null && _web.IsRunning)
        {
            string newPin = _web.RegeneratePin();

            _isPinRevealed = true;
            UpdateWebPinDisplay();
            UpdateWebQrCode();
            RefreshDevicesUi();

            if (WebRegeneratePinRotate != null)
            {
                var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(550))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                WebRegeneratePinRotate.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
            }

            _pinCounterCts?.Cancel();
            _pinCounterCts = new CancellationTokenSource();
            var ct = _pinCounterCts.Token;

            await AnimatePinCounterRollAsync(newPin, ct);

            ShowToast(isTesting: false, isSuccess: true, text: string.Format(LocalizationManager.GetString("Str_Web_PinReset"), newPin));

            AppLogger.Info($"PIN regenerated: {newPin}. All web sessions invalidated.", "Web");
        }
    }

    private async Task AnimatePinCounterRollAsync(string targetPin, CancellationToken ct)
    {
        if (WebPinText == null) return;

        char[] display = WebPinText.Text != null && WebPinText.Text.Length == 4 && !WebPinText.Text.Contains("-") && !WebPinText.Text.Contains("•")
            ? WebPinText.Text.ToCharArray()
            : new char[] { '0', '0', '0', '0' };

        char[] target = targetPin.PadRight(4, '0').ToCharArray();
        int[] lockSteps = { 7, 12, 17, 22 };
        int totalSteps = 22;

        try
        {
            for (int step = 0; step <= totalSteps; step++)
            {
                if (ct.IsCancellationRequested) break;

                bool anyLocked = false;
                for (int d = 0; d < 4; d++)
                {
                    if (step >= lockSteps[d])
                    {
                        if (display[d] != target[d])
                        {
                            display[d] = target[d];
                            anyLocked = true;
                        }
                    }
                    else
                    {
                        int nextVal = ((display[d] - '0') + 1) % 10;
                        display[d] = (char)('0' + nextVal);
                    }
                }

                WebPinText.Text = new string(display);

                if (anyLocked && WebPinScale != null)
                {
                    var tickAnim = new DoubleAnimation(1.12, 1.0, TimeSpan.FromMilliseconds(110))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    WebPinScale.BeginAnimation(ScaleTransform.ScaleXProperty, tickAnim);
                    WebPinScale.BeginAnimation(ScaleTransform.ScaleYProperty, tickAnim);
                }

                await Task.Delay(26, ct);
            }

            WebPinText.Text = targetPin;

            if (WebPinScale != null)
            {
                var finalPop = new DoubleAnimation(1.22, 1.0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
                };
                WebPinScale.BeginAnimation(ScaleTransform.ScaleXProperty, finalPop);
                WebPinScale.BeginAnimation(ScaleTransform.ScaleYProperty, finalPop);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void UpdateWebQrCode()
    {
        try
        {
            string ip = GetLocalIp();

            if (ip == "?") ip = "127.0.0.1";

            int port = _settings.WebPort;

            if (WebPortBox != null && int.TryParse(WebPortBox.Text.Trim(), out var p) && p > 0 && p <= 65535)
            {
                port = p;
            }

            string baseUrl = $"http://{ip}:{port}";
            string qrUrl = baseUrl;
            if (_settings.SafeModeEnabled && _web != null && !string.IsNullOrEmpty(_web.Pin))
            {
                qrUrl = $"{baseUrl}/?pin={_web.Pin}";
            }

            var qr = QrCode.EncodeText(qrUrl, QrCode.Ecc.MEDIUM);

            var bmp = qr.ToBitmapSource(scale: 6, border: 4);

            if (WebQrImage != null)
            {
                WebQrImage.Source = bmp;

                WebQrImage.ToolTip = qrUrl;
            }

            if (WebIpAddressText != null)
            {
                WebIpAddressText.Text = baseUrl;
            }

            if (WebQrContainer != null)
            {
                WebQrContainer.Opacity = (_web != null && _web.IsRunning) ? 1.0 : 0.45;
            }

            UpdateWebPinDisplay();
        }

        catch { }
    }

    private void OnCopyWebIpClick(object sender, RoutedEventArgs e)
    {
        CopyWebIp();
    }

    private void OnCopyWebIpClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CopyWebIp();
    }

    private void CopyWebIp()
    {
        try
        {
            string ip = GetLocalIp();

            if (ip == "?") ip = "127.0.0.1";

            int port = _settings.WebPort;

            if (WebPortBox != null && int.TryParse(WebPortBox.Text.Trim(), out var p) && p > 0 && p <= 65535)
            {
                port = p;
            }

            string url = $"http://{ip}:{port}";

            System.Windows.Clipboard.SetText(url);

            ShowToast(isTesting: false, isSuccess: true, text: LocalizationManager.GetString("Str_Web_UrlCopied"));

            SetStatus($"{LocalizationManager.GetString("Str_Web_UrlCopied")}: {url}");
        }

        catch { }
    }

    private void OnPortPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private void OnWebPortBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (WebPortBox == null) return;

        if (int.TryParse(WebPortBox.Text.Trim(), out var p) && p > 0 && p <= 9999)
        {
            _settings.WebPort = p;

            _settings.Save();

            UpdateWebQrCode();
        }
    }

    private void OnWebEdgeBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WebEdgeBox?.SelectedItem is ComboBoxItem sel &&

            Enum.TryParse<ScreenEdge>(sel.Tag?.ToString(), out var parsedEdge))
        {
            if (_settings.WebDropEdge != parsedEdge)
            {
                _settings.WebDropEdge = parsedEdge;

                _settings.Save();

                RestartEdges();
            }
        }
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        var primaryDev = _settings.GetDevicesSnapshot().FirstOrDefault();

        string targetIp = _settings.LoopbackMode ? "127.0.0.1" : (primaryDev != null && !string.IsNullOrWhiteSpace(primaryDev.Ip) ? primaryDev.Ip.Trim() : _settings.PeerIp);

        int port = (primaryDev != null && primaryDev.Port > 0) ? primaryDev.Port : _settings.Port;

        string? name = primaryDev?.Name;

        await PerformPingTestAsync(targetIp, port, name);
    }

    private async Task PerformPingTestAsync(string targetIp, int port, string? deviceName = null)
    {
        if (string.IsNullOrWhiteSpace(targetIp)) targetIp = "127.0.0.1";

        if (port <= 0) port = 11000;

        string targetLabel = !string.IsNullOrEmpty(deviceName) ? $"{deviceName} ({targetIp}:{port})" : $"{targetIp}:{port}";

        ShowPingToast(isTesting: true, text: LocalizationManager.GetString("Str_Ping_Testing"));

        AppLogger.Info($"Pinging {targetLabel}...", "Ping");

        long latencyMs = -1;

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var sw = System.Diagnostics.Stopwatch.StartNew();

            await client.ConnectAsync(targetIp, port, cts.Token);

            sw.Stop();

            latencyMs = Math.Max(1, sw.ElapsedMilliseconds);

            try
            {
                var stream = client.GetStream();

                await stream.WriteAsync("EDPI"u8.ToArray(), cts.Token);
            }

            catch { }
        }

        catch
        {
            try
            {
                using var pinger = new System.Net.NetworkInformation.Ping();

                var reply = await pinger.SendPingAsync(targetIp, 2000);

                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    latencyMs = Math.Max(1, reply.RoundtripTime);
                }
            }

            catch { }
        }

        if (latencyMs >= 0)
        {
            lock (_settings.DevicesLock)
            {
                var matched = _settings.Devices.FirstOrDefault(d => string.Equals(d.Ip?.Trim(), targetIp, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                {
                    matched.IsOnline = true;

                    matched.LastSeen = DateTime.UtcNow;

                    Dispatcher.Invoke(() => UpdateDeviceOnlineVisual(matched));
                }
            }

            _discovery?.MarkDeviceActive(targetIp);

            ShowPingToast(isTesting: false, isSuccess: true, text: LocalizationManager.GetString("Str_Ping_Result", latencyMs), tooltip: $"{targetLabel} ({latencyMs} ms)");

            AppLogger.Success($"Connection to {targetLabel} verified (ping {latencyMs} ms)", "Ping");
        }

        else
        {
            lock (_settings.DevicesLock)
            {
                var matched = _settings.Devices.FirstOrDefault(d => string.Equals(d.Ip?.Trim(), targetIp, StringComparison.OrdinalIgnoreCase));

                if (matched != null && matched.IsOnline)
                {
                    matched.IsOnline = false;

                    Dispatcher.Invoke(() => UpdateDeviceOnlineVisual(matched));
                }
            }

            ShowPingToast(isTesting: false, isSuccess: false, text: LocalizationManager.GetString("Str_Ping_Failed"), tooltip: targetLabel);

            AppLogger.Error($"Connection to {targetLabel} failed", "Ping");
        }
    }
}
