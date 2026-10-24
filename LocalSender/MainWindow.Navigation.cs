using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Point = System.Windows.Point;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private static class DwmApi
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;
        public const int DWMWCP_ROUND = 2;
    }

    private void UpdateDwmFrame()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var margins = new DwmApi.MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 1, cyBottomHeight = 0 };
            DwmApi.DwmExtendFrameIntoClientArea(handle, ref margins);

            bool isLight = ThemeManager.IsLightBackground;
            int darkMode = isLight ? 0 : 1;

            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));

            int cornerPref = DwmApi.DWMWCP_ROUND;
            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            var bg = ThemeManager.BackgroundColor;
            int colorRef = (bg.B << 16) | (bg.G << 8) | bg.R;
            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));

            var fore = ThemeManager.IconColor;
            int textRef = (fore.B << 16) | (fore.G << 8) | fore.R;
            DwmApi.DwmSetWindowAttribute(handle, DwmApi.DWMWA_TEXT_COLOR, ref textRef, sizeof(int));
        }
        catch { }
    }

    private void OnTitleBarMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnTitleBarMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnTitleBarCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        bool isMax = WindowState == WindowState.Maximized;
        if (MaximizeIconPath != null) MaximizeIconPath.Visibility = isMax ? Visibility.Collapsed : Visibility.Visible;
        if (RestoreIconPath != null) RestoreIconPath.Visibility = isMax ? Visibility.Visible : Visibility.Collapsed;
        if (TitleBarMaximizeButton != null)
        {
            TitleBarMaximizeButton.ToolTip = isMax
                ? LocalizationManager.GetString("Str_TitleBar_Restore")
                : LocalizationManager.GetString("Str_TitleBar_Maximize");
        }
    }

    private void OnToggleSidebarClick(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        double targetWidth = _isSidebarCollapsed ? 52.0 : 160.0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animWidth = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = ease
        };
        SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, animWidth);

        if (MenuCustom.RenderTransform is not RotateTransform)
        {
            MenuCustom.RenderTransformOrigin = new Point(0.5, 0.5);
            MenuCustom.RenderTransform = new RotateTransform(0);
        }
        if (MenuDefault.RenderTransform is not RotateTransform)
        {
            MenuDefault.RenderTransformOrigin = new Point(0.5, 0.5);
            MenuDefault.RenderTransform = new RotateTransform(0);
        }
        var animRotate = new DoubleAnimation(_isSidebarCollapsed ? 90 : 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = ease
        };
        MenuCustom.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, animRotate);
        MenuDefault.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, animRotate);

        if (_isSidebarCollapsed)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            fadeOut.Completed += (_, _) =>
            {
                if (_isSidebarCollapsed)
                {
                    NavHomeText.Visibility = Visibility.Collapsed;
                    NavDevicesText.Visibility = Visibility.Collapsed;
                    NavHistoryText.Visibility = Visibility.Collapsed;
                    NavConsoleText.Visibility = Visibility.Collapsed;
                    NavSettingsText.Visibility = Visibility.Collapsed;
                }
            };
            NavHomeText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            NavDevicesText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            NavHistoryText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            NavConsoleText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            NavSettingsText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            NavHomeText.Visibility = Visibility.Visible;
            NavDevicesText.Visibility = Visibility.Visible;
            NavHistoryText.Visibility = Visibility.Visible;
            NavConsoleText.Visibility = Visibility.Visible;
            NavSettingsText.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = ease
            };
            NavHomeText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            NavDevicesText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            NavHistoryText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            NavConsoleText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            NavSettingsText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }
    }

    private void OnNavHomeClick(object sender, RoutedEventArgs e) => ShowPage(HomePageView);
    private void OnNavDevicesClick(object sender, RoutedEventArgs e)
    {
        RefreshDevicesUi();
        ShowPage(DevicesPageView);
    }

    private void OnNavFilesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? boxText = SaveFolderBox?.Text?.Trim();
            string folder = !string.IsNullOrWhiteSpace(_settings?.SaveFolder) && Directory.Exists(_settings.SaveFolder)
                ? _settings.SaveFolder
                : (!string.IsNullOrWhiteSpace(boxText) && Directory.Exists(boxText) ? boxText : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to open save folder: {ex.Message}", "App");
        }
    }
    private void OnNavHistoryClick(object sender, RoutedEventArgs e) => ShowPage(HistoryPageView);
    private void OnNavConsoleClick(object sender, RoutedEventArgs e) => ShowPage(ConsolePageView);
    private void OnNavSettingsClick(object sender, RoutedEventArgs e) => ShowPage(SettingsPageView);
    private void OnBackToMainClick(object sender, RoutedEventArgs e) => ShowPage(HomePageView);

    private void OnConsoleCopyClick(object sender, RoutedEventArgs e)
    {
        var text = AppLogger.GetAllText();
        if (!string.IsNullOrEmpty(text))
        {
            System.Windows.Clipboard.SetText(text);
            ShowToast(isTesting: false, isSuccess: true, text: LocalizationManager.GetString("Str_Console_CopiedToast"));
        }
    }

    private void OnConsoleClearClick(object sender, RoutedEventArgs e)
    {
        AppLogger.Clear();
        if (EmptyConsoleText != null) EmptyConsoleText.Visibility = Visibility.Visible;
    }

    private void ShowPage(UIElement targetPage)
    {
        var pages = new UIElement[] { HomePageView, DevicesPageView, HistoryPageView, ConsolePageView, SettingsPageView };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        foreach (var page in pages)
        {
            if (page == targetPage)
            {
                page.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
                var slideUp = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };

                page.RenderTransform = new TranslateTransform(0, 14);
                page.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                page.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
            else
            {
                page.Visibility = Visibility.Collapsed;
            }
        }
    }
}
