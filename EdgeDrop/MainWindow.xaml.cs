using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly TransferService _transferService;
    private readonly List<EdgeDropWindow> _edges = new();
    private ObservableCollection<QueueItem> _sharedQueue => _transferService.Queue;
    private bool _running;
    private bool _isExplicitExit;
    private bool _isSidebarCollapsed;
    private bool _isIpHidden;
    private double _ipDisplayMeasuredWidth = 0.0;
    private bool _isUpdatingColorInternally;
    private CancellationTokenSource? _pingToastCts;
    private WebServer? _web;
    private DeviceDiscoveryService? _discovery;
    private readonly NotificationAggregator _notifications;

    public MainWindow()
    {
        _isUpdatingColorInternally = true;

        _settings = Settings.Load();

        _transferService = new TransferService(_settings);

        WireTransferServiceEvents();

        _notifications = new NotificationAggregator(_settings);

        _notifications.StatusMessage += msg => Dispatcher.Invoke(() => SetStatus(msg));

        LocalizationManager.ApplyLanguage(_settings.Language);

        if (ThemeManager.TryParseHexColor(_settings.BackgroundColorHex, out var savedBg) &&

            ThemeManager.TryParseHexColor(_settings.AccentColorHex, out var savedAccent))
        {
            if (_settings.Theme == AppTheme.Custom)
            {
                ThemeManager.ApplyCustomPalette(savedBg, savedAccent);
            }

            else
            {
                ThemeManager.ApplyAccentColor(savedAccent);

                ThemeManager.ApplyTheme(_settings.Theme);
            }
        }

        else
        {
            ThemeManager.ApplyTheme(_settings.Theme);
        }

        InitializeComponent();

        _isUpdatingColorInternally = false;

        SelectLanguageInComboBox(_settings.Language);

        SelectThemeInComboBox(_settings.Theme);

        UpdateCustomThemeSwatches();

        RenderColorWheelBitmap(1.0);

        SaveFolderBox.Text = _settings.SaveFolder;

        if (DevModeBox != null) DevModeBox.IsChecked = _settings.LoopbackMode;

        if (NotificationsEnabledToggle != null) NotificationsEnabledToggle.IsChecked = _settings.NotificationsEnabled;

        if (RequireAcceptToggle != null) RequireAcceptToggle.IsChecked = _settings.RequireAcceptIncoming;

        if (ShellIntegrationToggle != null) ShellIntegrationToggle.IsChecked = ShellContextMenuManager.IsIntegrationEnabled();

        if (AutoStartServicesToggle != null) AutoStartServicesToggle.IsChecked = _settings.AutoStartServicesOnLaunch;

        if (SafeModeToggle != null) SafeModeToggle.IsChecked = _settings.SafeModeEnabled;

        InitSpeedLimitComboBox();

        InitMaxFileSizeComboBox();

        RefreshDevicesUi();

        InitHistoryView();

        LoadHistory();

        UpdateEmptyHistoryVisibility();

        AppLogger.LogAdded += entry => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (EmptyConsoleText != null)
            {
                EmptyConsoleText.Visibility = AppLogger.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ConsoleAutoScrollBox?.IsChecked == true && ConsoleListBox != null && AppLogger.Entries.Count > 0)
            {
                ConsoleListBox.ScrollIntoView(AppLogger.Entries[^1]);
            }
        });

        AppLogger.Info("EdgeDrop initialized successfully", "App");

        DisplayLocalIp();

        UpdateIpDisplay();

        ApplyCustomSvgIcons();

        if (WebPortBox != null) WebPortBox.Text = _settings.WebPort.ToString();

        if (WebEdgeBox != null)
        {
            foreach (ComboBoxItem item in WebEdgeBox.Items)
            {
                if (string.Equals(item.Tag?.ToString(), _settings.WebDropEdge.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    WebEdgeBox.SelectedItem = item;

                    break;
                }
            }
        }

        ApplyWebCardCollapsedState(animate: false);

        UpdateWebQrCode();

        UpdateWebButtonState();

        InitTray();

        InitDiscoveryService();

        Loaded += (_, _) =>
        {
            UpdateDwmFrame();

            if (_settings.AutoStartServicesOnLaunch)
            {
                Start();
            }

            else
            {
                _running = false;

                UpdatePowerButtonVisual();

                UpdateWebButtonState();

                SetStatus(LocalizationManager.GetString("Str_Status_ServicesIdle", "Services idle (auto-start disabled in settings)"));
            }

            var cmdArgs = Environment.GetCommandLineArgs();

            if (cmdArgs.Length > 1)
            {
                HandleCommandLineArgs(cmdArgs);
            }
        };

        StateChanged += OnWindowStateChanged;

        ThemeManager.BackgroundColorChanged += _ => Dispatcher.Invoke(UpdateDwmFrame);

        Closing += OnWindowClosing;

        Closed += (_, _) => CleanUp();
    }

    private void ShowPingToast(bool isTesting, bool isSuccess = true, string text = "", string? tooltip = null)
    {
        ShowToast(isTesting, isSuccess, text, tooltip);
    }

    private void ShowToast(bool isTesting, bool isSuccess = true, string text = "", string? tooltip = null)
    {
        if (GlobalToastOverlay == null) return;

        _pingToastCts?.Cancel();

        _pingToastCts = new CancellationTokenSource();

        var token = _pingToastCts.Token;

        GlobalToastOverlay.Visibility = Visibility.Visible;

        if (GlobalToastDot != null) GlobalToastDot.Visibility = Visibility.Collapsed;

        if (GlobalToastIcon != null) GlobalToastIcon.Visibility = Visibility.Collapsed;

        if (isTesting)
        {
            var amber = System.Windows.Media.Color.FromRgb(0xFA, 0x9D, 0x00);

            var pingImg = SvgIconHelper.LoadSvgIcon("ping.svg", amber);

            if (pingImg != null && GlobalToastIcon != null)
            {
                GlobalToastIcon.Source = pingImg;

                GlobalToastIcon.Visibility = Visibility.Visible;
            }

            else if (GlobalToastDot != null)
            {
                GlobalToastDot.Fill = new SolidColorBrush(amber);

                GlobalToastDot.Visibility = Visibility.Visible;
            }
        }

        else if (isSuccess)
        {
            var successColor = System.Windows.Media.Color.FromRgb(0x54, 0xB0, 0x54);

            string iconName = (text.Contains("Ping", StringComparison.OrdinalIgnoreCase) || text.Contains("ms", StringComparison.OrdinalIgnoreCase))

                ? "ping.svg"

                : "transfer.svg";

            var successImg = SvgIconHelper.LoadSvgIcon(iconName, successColor) ?? SvgIconHelper.LoadSvgIcon("download.svg", successColor);

            if (successImg != null && GlobalToastIcon != null)
            {
                GlobalToastIcon.Source = successImg;

                GlobalToastIcon.Visibility = Visibility.Visible;
            }

            else if (GlobalToastDot != null)
            {
                GlobalToastDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessBrush");

                GlobalToastDot.Visibility = Visibility.Visible;
            }
        }

        else
        {
            var errColor = System.Windows.Media.Color.FromRgb(0xFF, 0x52, 0x52);

            var errImg = SvgIconHelper.LoadSvgIcon("error.svg", errColor) ?? SvgIconHelper.LoadSvgIcon("x.svg", errColor);

            if (errImg != null && GlobalToastIcon != null)
            {
                GlobalToastIcon.Source = errImg;

                GlobalToastIcon.Visibility = Visibility.Visible;
            }

            else if (GlobalToastDot != null)
            {
                GlobalToastDot.Fill = (System.Windows.Media.Brush)FindResource("ErrorTitleBrush");

                GlobalToastDot.Visibility = Visibility.Visible;
            }
        }

        if (GlobalToastText != null) GlobalToastText.Text = text;

        GlobalToastOverlay.ToolTip = tooltip;

        var backEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

        var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animY = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = backEase };

        var animFade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = cubicEase };

        GlobalToastTranslate?.BeginAnimation(TranslateTransform.YProperty, animY);

        GlobalToastOverlay.BeginAnimation(UIElement.OpacityProperty, animFade);

        if (!isTesting)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3200, token);

                    Dispatcher.Invoke(() =>
                    {
                        var animFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(260));

                        var animYOut = new DoubleAnimation(40, TimeSpan.FromMilliseconds(260));

                        animFadeOut.Completed += (_, _) =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                GlobalToastOverlay.Visibility = Visibility.Collapsed;
                            }
                        };

                        GlobalToastOverlay.BeginAnimation(UIElement.OpacityProperty, animFadeOut);

                        GlobalToastTranslate?.BeginAnimation(TranslateTransform.YProperty, animYOut);
                    });
                }

                catch (TaskCanceledException) { }
            }, token);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            e.Cancel = true;

            Hide();

            _notifyIcon?.ShowBalloonTip(1500, "EdgeDrop", LocalizationManager.GetString("Str_Tray_Minimized"), Forms.ToolTipIcon.Info);
        }
    }

    private void CleanUp()
    {
        _notifyIcon?.Dispose();

        _notifyIcon = null;

        FlushHistorySave();

        Stop();
    }

    private void OnPowerButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleService();
    }

    private void OnPowerButtonMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animScale = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);

        if (PowerButtonShadow != null)
        {
            var animBlur = new DoubleAnimation(_running ? 30 : 24, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

            var animOpacity = new DoubleAnimation(_running ? 0.60 : 0.45, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

            PowerButtonShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, animBlur);

            PowerButtonShadow.BeginAnimation(DropShadowEffect.OpacityProperty, animOpacity);
        }
    }

    private void OnPowerButtonMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animScale = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);

        if (PowerButtonShadow != null)
        {
            var animBlur = new DoubleAnimation(_running ? 24 : 18, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

            var animOpacity = new DoubleAnimation(_running ? 0.45 : 0.30, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };

            PowerButtonShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, animBlur);

            PowerButtonShadow.BeginAnimation(DropShadowEffect.OpacityProperty, animOpacity);
        }
    }

    private void OnPowerButtonPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animScale = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(90)) { EasingFunction = ease };

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);
    }

    private void OnPowerButtonPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

        double targetScale = PowerButton.IsMouseOver ? 1.04 : 1.0;

        var animScale = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);

        PowerButtonScale?.BeginAnimation(ScaleTransform.ScaleYProperty, animScale);
    }

    private void ToggleService()
    {
        if (_running) Stop();

        else Start();
    }

    private void Start()
    {
        AppLogger.Info($"Start() called. Port={_settings.Port}", "App");

        try
        {
            _transferService.StartListening(_settings.Port);

            _transferService.AttachWebServer(_web);

            AppLogger.Info($"TCP listener started on port {_settings.Port}, save folder: {_settings.SaveFolder}", "Receiver");

            TransferQueueListBox.ItemsSource = _transferService.Queue;

            _running = true;

            RestartEdges();

            if (_settings.AutoDiscoveryEnabled)
            {
                if (_discovery == null)
                {
                    InitDiscoveryService();
                }
                else
                {
                    _discovery.Start();
                }
            }

            UpdatePowerButtonVisual();

            SetStatus(string.Format(LocalizationManager.GetString("Str_Status_Listening", "Listening on port {0}, folder: {1}"), _settings.Port, _settings.SaveFolder));

            AppLogger.Info("Transfer service activated (ON)", "App");

            if (_settings.WebServerEnabled)
            {
                StartWebServer();
            }

            else
            {
                UpdateWebButtonState();
            }
        }

        catch (Exception ex)
        {
            AppLogger.Error($"Failed to start services: {ex.Message}", "App");

            _running = false;

            UpdatePowerButtonVisual();

            UpdateWebButtonState();

            if (StatusBadge != null)
            {
                StatusBadge.Text = $"{LocalizationManager.GetString("Str_History_Error")}: {ex.Message}";
                StatusBadge.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            }

            ShowToast(false, false, $"{LocalizationManager.GetString("Str_History_Error")}: {ex.Message}");
        }
    }

    private void Stop()
    {
        _transferService.StopListening();

        StopWebServer();

        _discovery?.Stop();

        foreach (var edge in _edges)
        {
            try { edge.Close(); } catch { }
        }

        _edges.Clear();

        _running = false;

        UpdatePowerButtonVisual();

        UpdateWebButtonState();

        SetStatus(LocalizationManager.GetString("Str_Service_Stopped", "Stopped"));

        AppLogger.Info("Transfer service stopped (OFF)", "App");
    }

    private void UpdatePowerButtonVisual()
    {
        if (PowerButton == null) return;

        if (_running)
        {
            var accent = ThemeManager.AccentColor;

            PowerButton.Background = new SolidColorBrush(accent);

            if (PowerButtonShadow != null)
            {
                PowerButtonShadow.Color = accent;

                PowerButtonShadow.BlurRadius = 24;

                PowerButtonShadow.Opacity = 0.45;
            }

            if (StatusBadge != null)
            {
                StatusBadge.Text = LocalizationManager.GetString("Str_Service_Active");

                StatusBadge.Foreground = new SolidColorBrush(accent);
            }
        }

        else
        {
            PowerButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x76, 0x76, 0x76));

            if (PowerButtonShadow != null)
            {
                PowerButtonShadow.Color = System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00);

                PowerButtonShadow.BlurRadius = 18;

                PowerButtonShadow.Opacity = 0.30;
            }

            if (StatusBadge != null)
            {
                StatusBadge.Text = LocalizationManager.GetString("Str_Service_Stopped");

                StatusBadge.Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["SubtleTextBrush"];
            }
        }

        if (PowerLabel != null)
        {
            PowerLabel.Text = LocalizationManager.GetString(_running ? "Str_Power_On" : "Str_Power_Off");
        }

        AnimatePowerButtonIcon(_running);

        UpdateIpDisplay();
    }

    private void AnimatePowerButtonIcon(bool isOn)
    {
        if (PowerIconRotate != null)
        {
            var rotAnim = new DoubleAnimation
            {
                From = isOn ? -60 : 60,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(350),

                EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
            };

            PowerIconRotate.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
        }

        if (PowerIconScale != null)
        {
            var scaleAnim = new DoubleAnimation
            {
                From = 0.8,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300),

                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };

            PowerIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);

            PowerIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
    }

    private void UpdateIpDisplay()
    {
        var iconColor = ThemeManager.IconColor;

        var devicesSnapshot = _settings.GetDevicesSnapshot();

        var primaryDev = devicesSnapshot.FirstOrDefault();

        string firstIp;

        if (_settings.LoopbackMode)
        {
            firstIp = "127.0.0.1 (Loopback)";
        }

        else if (primaryDev != null && !string.IsNullOrWhiteSpace(primaryDev.Ip))
        {
            firstIp = primaryDev.Ip.Trim();
        }

        else if (!string.IsNullOrWhiteSpace(_settings.PeerIp))
        {
            firstIp = _settings.PeerIp.Trim();
        }

        else
        {
            firstIp = LocalizationManager.GetString("Str_Devices_NoDevicesConfigured");
        }

        if (TargetIpDisplayText != null)
        {
            if (!_isIpHidden)
            {
                TargetIpDisplayText.Text = firstIp;

                TargetIpDisplayText.Width = double.NaN;

                TargetIpDisplayText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

                _ipDisplayMeasuredWidth = Math.Max(TargetIpDisplayText.DesiredSize.Width, 80);

                TargetIpDisplayText.Width = _ipDisplayMeasuredWidth;
            }

            else
            {
                if (_ipDisplayMeasuredWidth > 0)
                {
                    TargetIpDisplayText.Width = _ipDisplayMeasuredWidth;
                }

                TargetIpDisplayText.Text = "••••••••••••";
            }
        }

        string eyeIconName = _isIpHidden ? "eye-off.svg" : "eye.svg";

        var eyeImg = SvgIconHelper.LoadSvgIcon(eyeIconName, iconColor);

        if (eyeImg != null && EyeIconCustom != null && EyeIconPath != null)
        {
            EyeIconCustom.Source = eyeImg;

            EyeIconCustom.Visibility = Visibility.Visible;

            EyeIconPath.Visibility = Visibility.Collapsed;
        }

        else if (EyeIconCustom != null && EyeIconPath != null)
        {
            EyeIconCustom.Visibility = Visibility.Collapsed;

            EyeIconPath.Visibility = Visibility.Visible;
        }

        if (TargetIpExtraBadge != null && TargetIpExtraBadgeText != null)
        {
            int extraCount = Math.Max(0, devicesSnapshot.Count - 1);

            if (!_settings.LoopbackMode && extraCount > 0)
            {
                TargetIpExtraBadge.Visibility = Visibility.Visible;

                TargetIpExtraBadgeText.Text = $"+{extraCount}";
            }

            else
            {
                TargetIpExtraBadge.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnToggleIpHideClick(object sender, RoutedEventArgs e)
    {
        _isIpHidden = !_isIpHidden;

        if (EyeIconScale != null)
        {
            var anim = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };

            EyeIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);

            EyeIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        if (TargetIpDisplayText != null)
        {
            var fadeOut = new DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(90));

            fadeOut.Completed += (_, _) =>
            {
                UpdateIpDisplay();

                var fadeIn = new DoubleAnimation(0.15, 1.0, TimeSpan.FromMilliseconds(130));

                TargetIpDisplayText.BeginAnimation(OpacityProperty, fadeIn);
            };

            TargetIpDisplayText.BeginAnimation(OpacityProperty, fadeOut);
        }

        else
        {
            UpdateIpDisplay();
        }
    }

    private void ApplyCustomSvgIcons()
    {
        var iconColor = ThemeManager.IconColor;

        var homeImg = SvgIconHelper.LoadSvgIcon("home.svg", iconColor);

        if (homeImg != null && NavHomeCustom != null && NavHomeDefault != null)
        {
            NavHomeCustom.Source = homeImg;

            NavHomeCustom.Visibility = Visibility.Visible;

            NavHomeDefault.Visibility = Visibility.Collapsed;
        }

        var devImg = SvgIconHelper.LoadSvgIcon("devices.svg", iconColor);

        if (devImg != null && NavDevicesCustom != null && NavDevicesDefault != null)
        {
            NavDevicesCustom.Source = devImg;

            NavDevicesCustom.Visibility = Visibility.Visible;

            NavDevicesDefault.Visibility = Visibility.Collapsed;
        }

        var historyImg = SvgIconHelper.LoadSvgIcon("history.svg", iconColor);

        if (historyImg != null && NavHistoryCustom != null && NavHistoryDefault != null)
        {
            NavHistoryCustom.Source = historyImg;

            NavHistoryCustom.Visibility = Visibility.Visible;

            NavHistoryDefault.Visibility = Visibility.Collapsed;
        }

        var consoleImg = SvgIconHelper.LoadSvgIcon("terminal.svg", iconColor);

        if (consoleImg != null && NavConsoleCustom != null && NavConsoleDefault != null)
        {
            NavConsoleCustom.Source = consoleImg;

            NavConsoleCustom.Visibility = Visibility.Visible;

            NavConsoleDefault.Visibility = Visibility.Collapsed;
        }

        var settingsImg = SvgIconHelper.LoadSvgIcon("settings.svg", iconColor);

        if (settingsImg != null && NavSettingsCustom != null && NavSettingsDefault != null)
        {
            NavSettingsCustom.Source = settingsImg;

            NavSettingsCustom.Visibility = Visibility.Visible;

            NavSettingsDefault.Visibility = Visibility.Collapsed;
        }

        var menuImg = SvgIconHelper.LoadSvgIcon("menu.svg", iconColor);

        if (menuImg != null && MenuCustom != null && MenuDefault != null)
        {
            MenuCustom.Source = menuImg;

            MenuCustom.Visibility = Visibility.Visible;

            MenuDefault.Visibility = Visibility.Collapsed;
        }

        var logoImg = SvgIconHelper.LoadSvgIcon("logo.svg", iconColor);

        if (logoImg != null && AppLogoCustom != null && AppLogoDefault != null)
        {
            AppLogoCustom.Source = logoImg;

            AppLogoCustom.Visibility = Visibility.Visible;

            AppLogoDefault.Visibility = Visibility.Collapsed;
        }

        var powerImg = SvgIconHelper.LoadSvgIcon("power.svg", iconColor);

        if (powerImg != null && PowerIconCustom != null && PowerIconDefault != null)
        {
            PowerIconCustom.Source = powerImg;

            PowerIconCustom.Visibility = Visibility.Visible;

            PowerIconDefault.Visibility = Visibility.Collapsed;
        }

        var pingImg = SvgIconHelper.LoadSvgIcon("ping.svg", iconColor);

        if (pingImg != null && HomePingIconCustom != null && HomePingIconDefault != null)
        {
            HomePingIconCustom.Source = pingImg;

            HomePingIconCustom.Visibility = Visibility.Visible;

            HomePingIconDefault.Visibility = Visibility.Collapsed;
        }

        if (pingImg != null && DiagnosticsBtnIconCustom != null && DiagnosticsBtnIconDefault != null)
        {
            DiagnosticsBtnIconCustom.Source = pingImg;

            DiagnosticsBtnIconCustom.Visibility = Visibility.Visible;

            DiagnosticsBtnIconDefault.Visibility = Visibility.Collapsed;
        }

        if (consoleImg != null && ConsoleHeaderIconCustom != null && ConsoleHeaderIconDefault != null)
        {
            ConsoleHeaderIconCustom.Source = consoleImg;

            ConsoleHeaderIconCustom.Visibility = Visibility.Visible;

            ConsoleHeaderIconDefault.Visibility = Visibility.Collapsed;
        }

        var xImg = SvgIconHelper.LoadSvgIcon("x.svg", iconColor);

        if (xImg != null && ColorWheelCloseCustom != null && ColorWheelCloseDefault != null)
        {
            ColorWheelCloseCustom.Source = xImg;

            ColorWheelCloseCustom.Visibility = Visibility.Visible;

            ColorWheelCloseDefault.Visibility = Visibility.Collapsed;
        }

        if (xImg != null && DiagnosticsCloseIcon != null)
        {
            DiagnosticsCloseIcon.Source = xImg;
        }

        var webImg = SvgIconHelper.LoadSvgIcon("monitor-smartphone.svg", iconColor);

        if (webImg != null && WebIconCustom != null && WebIconDefault != null)
        {
            WebIconCustom.Source = webImg;

            WebIconCustom.Visibility = Visibility.Visible;

            WebIconDefault.Visibility = Visibility.Collapsed;
        }

        var chevronImg = SvgIconHelper.LoadSvgIcon("chevron-down.svg", iconColor);

        if (chevronImg != null && WebChevronCustom != null && WebChevronDefault != null)
        {
            WebChevronCustom.Source = chevronImg;

            WebChevronCustom.Visibility = Visibility.Visible;

            WebChevronDefault.Visibility = Visibility.Collapsed;
        }

        UpdateFooterPauseButtonVisual();

        var ellipsisImg = SvgIconHelper.LoadSvgIcon("ellipsis-vertical.svg", iconColor);

        if (ellipsisImg != null && FooterQueueIconImage != null)
        {
            FooterQueueIconImage.Source = ellipsisImg;
        }

        var cancelImg = SvgIconHelper.LoadSvgIcon("x.svg", iconColor);

        if (cancelImg != null && FooterCancelIconImage != null)
        {
            FooterCancelIconImage.Source = cancelImg;
        }

        var funnelImg = SvgIconHelper.LoadSvgIcon("funnel.svg", iconColor);

        if (funnelImg != null && HistoryFilterFunnelImg != null && HistoryFilterFunnelPath != null)
        {
            HistoryFilterFunnelImg.Source = funnelImg;

            HistoryFilterFunnelImg.Visibility = Visibility.Visible;

            HistoryFilterFunnelPath.Visibility = Visibility.Collapsed;
        }
    }

    private void SetStatus(string text)
    {
        if (StatusBadge != null && !_running)
        {
            StatusBadge.Text = text;
        }
    }
}
