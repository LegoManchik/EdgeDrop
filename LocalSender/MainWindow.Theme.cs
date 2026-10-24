using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private enum ColorWheelTarget { Background, Accent }
    private ColorWheelTarget _colorWheelTarget = ColorWheelTarget.Accent;
    private bool _isDraggingWheel;

    private void SelectThemeInComboBox(AppTheme theme)
    {
        if (ThemeBox == null) return;
        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (item.Tag?.ToString() == theme.ToString())
            {
                ThemeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || ThemeBox == null) return;
        if (ThemeBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppTheme>(item.Tag?.ToString(), out var selectedTheme))
        {
            _settings.Theme = selectedTheme;
            _settings.Save();
            ThemeManager.ApplyTheme(selectedTheme);
            UpdateCustomThemeSwatches();
            ApplyCustomSvgIcons();
            UpdateIpDisplay();
            UpdatePowerButtonVisual();
            RefreshDevicesUi();
        }
    }

    private void UpdateCustomThemeSwatches()
    {
        if (CurrentBgColorPreview != null)
        {
            CurrentBgColorPreview.Background = new SolidColorBrush(ThemeManager.BackgroundColor);
        }
        if (CurrentBgHexText != null)
        {
            CurrentBgHexText.Text = $"#{ThemeManager.BackgroundColor.R:X2}{ThemeManager.BackgroundColor.G:X2}{ThemeManager.BackgroundColor.B:X2}";
        }
        if (CurrentAccentColorPreview != null)
        {
            CurrentAccentColorPreview.Background = new SolidColorBrush(ThemeManager.AccentColor);
        }
        if (CurrentAccentHexText != null)
        {
            CurrentAccentHexText.Text = $"#{ThemeManager.AccentColor.R:X2}{ThemeManager.AccentColor.G:X2}{ThemeManager.AccentColor.B:X2}";
        }
    }

    private void OnPresetThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr)
        {
            var parts = tagStr.Split('|');
            if (parts.Length == 2 &&
                ThemeManager.TryParseHexColor(parts[0], out var bg) &&
                ThemeManager.TryParseHexColor(parts[1], out var accent))
            {
                _settings.BackgroundColorHex = parts[0];
                _settings.AccentColorHex = parts[1];
                _settings.Theme = AppTheme.Custom;
                _settings.Save();

                ThemeManager.ApplyCustomPalette(bg, accent);
                SelectThemeInComboBox(AppTheme.Custom);
                UpdateCustomThemeSwatches();
                ApplyCustomSvgIcons();
                UpdateIpDisplay();
                UpdatePowerButtonVisual();
                RefreshDevicesUi();
            }
        }
    }

    private void OnOpenBgColorWheelClick(object sender, RoutedEventArgs e)
    {
        _colorWheelTarget = ColorWheelTarget.Background;
        if (ColorWheelDialogTitle != null)
        {
            ColorWheelDialogTitle.Text = LocalizationManager.GetString("Str_ColorDialog_BgTitle");
        }
        OpenColorWheelOverlay(ThemeManager.BackgroundColor);
    }

    private void OnOpenAccentColorWheelClick(object sender, RoutedEventArgs e)
    {
        _colorWheelTarget = ColorWheelTarget.Accent;
        if (ColorWheelDialogTitle != null)
        {
            ColorWheelDialogTitle.Text = LocalizationManager.GetString("Str_ColorDialog_AccentTitle");
        }
        OpenColorWheelOverlay(ThemeManager.AccentColor);
    }

    private void OpenColorWheelOverlay(Color initialColor)
    {
        if (ColorWheelOverlay == null) return;
        AnimateModalOpen(ColorWheelOverlay, ColorWheelModalCard);
        LoadColorIntoWheel(initialColor);
    }

    private void OnCloseColorWheelOverlayClick(object sender, RoutedEventArgs e)
    {
        if (ColorWheelOverlay != null)
        {
            AnimateModalClose(ColorWheelOverlay, ColorWheelModalCard);
        }
    }

    private void OnApplyColorWheelOverlayClick(object sender, RoutedEventArgs e)
    {
        if (ColorWheelOverlay != null)
        {
            AnimateModalClose(ColorWheelOverlay, ColorWheelModalCard);
        }
        RefreshDevicesUi();
        _settings.Save();
    }

    private void OnColorWheelOverlayBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ColorWheelOverlay != null)
        {
            AnimateModalClose(ColorWheelOverlay, ColorWheelModalCard);
        }
    }

    private void OnColorWheelOverlayCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void LoadColorIntoWheel(Color color)
    {
        var (h, s, v) = ThemeManager.RgbToHsv(color);

        _isUpdatingColorInternally = true;
        if (BrightnessSlider != null)
        {
            BrightnessSlider.Value = Math.Round(v * 100);
        }
        if (BrightnessValueText != null)
        {
            BrightnessValueText.Text = $"{(int)Math.Round(v * 100)}%";
        }
        if (HexColorBox != null)
        {
            HexColorBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        if (WheelPreviewSwatch != null)
        {
            WheelPreviewSwatch.Background = new SolidColorBrush(color);
        }
        _isUpdatingColorInternally = false;

        RenderColorWheelBitmap(v);

        double size = 170;
        double radius = size / 2.0;
        double rad = h * Math.PI / 180.0;
        double dist = s * radius;
        double thumbX = radius + Math.Cos(rad) * dist;
        double thumbY = radius + Math.Sin(rad) * dist;

        if (WheelThumb != null)
        {
            Canvas.SetLeft(WheelThumb, thumbX - 10);
            Canvas.SetTop(WheelThumb, thumbY - 10);
            WheelThumb.Fill = new SolidColorBrush(color);
        }
    }

    private void RenderColorWheelBitmap(double brightness)
    {
        if (ColorWheelImage == null) return;
        int size = 170;
        int width = size;
        int height = size;
        double radius = size / 2.0;
        double cx = radius;
        double cy = radius;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - cx + 0.5;
                double dy = y - cy + 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                int index = y * stride + x * 4;

                if (dist <= radius)
                {
                    double alpha = 1.0;
                    if (dist > radius - 1.0)
                    {
                        alpha = Math.Clamp(radius - dist, 0.0, 1.0);
                    }

                    double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    if (angle < 0) angle += 360.0;

                    double saturation = Math.Clamp(dist / radius, 0.0, 1.0);
                    var c = ThemeManager.HsvToRgb(angle, saturation, brightness);

                    pixels[index] = (byte)(c.B * alpha);
                    pixels[index + 1] = (byte)(c.G * alpha);
                    pixels[index + 2] = (byte)(c.R * alpha);
                    pixels[index + 3] = (byte)(255 * alpha);
                }
                else
                {
                    pixels[index] = 0;
                    pixels[index + 1] = 0;
                    pixels[index + 2] = 0;
                    pixels[index + 3] = 0;
                }
            }
        }

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            width, height,
            96, 96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        ColorWheelImage.Source = bitmap;
    }

    private void OnColorWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ColorWheelImage == null || WheelThumb == null || _settings == null) return;
        _isDraggingWheel = true;
        ColorWheelImage.CaptureMouse();
        UpdateColorFromWheelPoint(e.GetPosition(ColorWheelImage), commit: false);
    }

    private void OnColorWheelMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingWheel && ColorWheelImage != null && _settings != null)
        {
            UpdateColorFromWheelPoint(e.GetPosition(ColorWheelImage), commit: false);
        }
    }

    private void OnColorWheelMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingWheel && ColorWheelImage != null && _settings != null)
        {
            _isDraggingWheel = false;
            ColorWheelImage.ReleaseMouseCapture();
            UpdateColorFromWheelPoint(e.GetPosition(ColorWheelImage), commit: true);
        }
    }

    private void UpdateColorFromWheelPoint(Point pt, bool commit)
    {
        if (_settings == null || ColorWheelImage == null) return;
        double size = 170;
        double radius = size / 2.0;
        double dx = pt.X - radius;
        double dy = pt.Y - radius;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;

        double sat = Math.Clamp(dist / radius, 0.0, 1.0);
        double val = (BrightnessSlider?.Value ?? 100.0) / 100.0;

        var color = ThemeManager.HsvToRgb(angle, sat, val);

        double clampedDist = Math.Min(dist, radius);
        double thumbX = radius + (dist > 0.0001 ? (dx / dist) * clampedDist : 0);
        double thumbY = radius + (dist > 0.0001 ? (dy / dist) * clampedDist : 0);

        if (WheelThumb != null)
        {
            Canvas.SetLeft(WheelThumb, thumbX - 10);
            Canvas.SetTop(WheelThumb, thumbY - 10);
            WheelThumb.Fill = new SolidColorBrush(color);
        }

        if (WheelPreviewSwatch != null)
        {
            WheelPreviewSwatch.Background = new SolidColorBrush(color);
        }

        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _isUpdatingColorInternally = true;
        if (HexColorBox != null) HexColorBox.Text = hex;
        _isUpdatingColorInternally = false;

        ApplyTargetColor(color, hex, commit);
    }

    private void ApplyColorFromHex(string hex, bool commit)
    {
        if (_settings == null) return;
        if (!ThemeManager.TryParseHexColor(hex, out var color)) return;

        var (h, s, v) = ThemeManager.RgbToHsv(color);

        if (BrightnessSlider != null)
        {
            _isUpdatingColorInternally = true;
            BrightnessSlider.Value = Math.Round(v * 100);
            if (BrightnessValueText != null) BrightnessValueText.Text = $"{(int)Math.Round(v * 100)}%";
            _isUpdatingColorInternally = false;
            RenderColorWheelBitmap(v);
        }

        double size = 170;
        double radius = size / 2.0;
        double rad = h * Math.PI / 180.0;
        double dist = s * radius;
        double thumbX = radius + Math.Cos(rad) * dist;
        double thumbY = radius + Math.Sin(rad) * dist;

        if (WheelThumb != null)
        {
            Canvas.SetLeft(WheelThumb, thumbX - 10);
            Canvas.SetTop(WheelThumb, thumbY - 10);
            WheelThumb.Fill = new SolidColorBrush(color);
        }

        if (WheelPreviewSwatch != null)
        {
            WheelPreviewSwatch.Background = new SolidColorBrush(color);
        }

        ApplyTargetColor(color, hex, commit);
    }

    private void ApplyTargetColor(Color color, string hex, bool commit)
    {
        if (_colorWheelTarget == ColorWheelTarget.Background)
        {
            _settings.BackgroundColorHex = hex;
            _settings.Theme = AppTheme.Custom;
            ThemeManager.ApplyCustomPalette(color, ThemeManager.AccentColor);
        }
        else
        {
            _settings.AccentColorHex = hex;
            if (_settings.Theme == AppTheme.Custom)
            {
                ThemeManager.ApplyCustomPalette(ThemeManager.BackgroundColor, color);
            }
            else
            {
                ThemeManager.ApplyAccentColor(color);
            }
        }

        SelectThemeInComboBox(_settings.Theme);
        UpdateCustomThemeSwatches();
        ApplyCustomSvgIcons();
        UpdateIpDisplay();
        UpdatePowerButtonVisual();
        RefreshDevicesUi();

        if (commit)
        {
            _settings.Save();
        }
    }

    private void OnHexColorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || HexColorBox == null) return;
        string text = HexColorBox.Text.Trim();
        if (text.StartsWith("#") && (text.Length == 7 || text.Length == 9))
        {
            ApplyColorFromHex(text, commit: false);
        }
    }

    private void OnBrightnessValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingColorInternally || _settings == null || ColorWheelImage == null) return;
        double val = e.NewValue / 100.0;
        if (BrightnessValueText != null)
        {
            BrightnessValueText.Text = $"{(int)Math.Round(e.NewValue)}%";
        }
        RenderColorWheelBitmap(val);

        if (WheelThumb != null)
        {
            double thumbLeft = Canvas.GetLeft(WheelThumb) + 10;
            double thumbTop = Canvas.GetTop(WheelThumb) + 10;
            UpdateColorFromWheelPoint(new Point(thumbLeft, thumbTop), commit: false);
        }
    }
}
