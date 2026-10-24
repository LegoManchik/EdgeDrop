using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using WpfApp = System.Windows.Application;

namespace EdgeDrop;

public record ThemePreset(string Id, string NameKey, string BgHex, string AccentHex);

public static class ThemeManager
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public static Color BackgroundColor { get; private set; } = Color.FromRgb(0x20, 0x20, 0x20);
    public static Color AccentColor { get; private set; } = Color.FromRgb(0x00, 0x78, 0xD4);

    public static event Action<Color>? AccentColorChanged;
    public static event Action<Color>? BackgroundColorChanged;

    public static readonly ThemePreset[] Presets = new[]
    {
        new ThemePreset("dark_blue", "Str_Preset_DarkBlue", "#202020", "#0078D4"),
        new ThemePreset("light_blue", "Str_Preset_LightBlue", "#F3F3F3", "#0067B8"),
        new ThemePreset("cyberpunk", "Str_Preset_Emerald", "#141A16", "#10893E"),
        new ThemePreset("amethyst", "Str_Preset_Purple", "#1A1524", "#881798"),
        new ThemePreset("sunset", "Str_Preset_Orange", "#1A1722", "#E05D06"),
        new ThemePreset("ruby", "Str_Preset_Ruby", "#1D1516", "#E81123"),
        new ThemePreset("sakura", "Str_Preset_Pink", "#1F151C", "#E3008C"),
        new ThemePreset("ocean", "Str_Preset_Teal", "#121A1F", "#008272"),
        new ThemePreset("amber", "Str_Preset_Amber", "#1D1814", "#FF8C00"),
        new ThemePreset("indigo", "Str_Preset_Indigo", "#141524", "#4F46E5"),
        new ThemePreset("oled_black", "Str_Preset_Oled", "#0C0C0C", "#0078D4"),
        new ThemePreset("paper_white", "Str_Preset_Paper", "#FFFFFF", "#10893E")
    };

    public static bool IsLightBackground =>
        ((0.299 * BackgroundColor.R + 0.587 * BackgroundColor.G + 0.114 * BackgroundColor.B) / 255.0) > 0.5;

    public static Color IconColor => IsLightBackground
        ? Color.FromRgb(0x2C, 0x2C, 0x2C)
        : Color.FromRgb(0xFF, 0xFF, 0xFF);

    public static void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        if (theme == AppTheme.Light)
        {
            ApplyCustomPalette(Color.FromRgb(0xF3, 0xF3, 0xF3), AccentColor);
        }
        else if (theme == AppTheme.Dark)
        {
            ApplyCustomPalette(Color.FromRgb(0x20, 0x20, 0x20), AccentColor);
        }
        else
        {
            ApplyCustomPalette(BackgroundColor, AccentColor);
        }
    }

    public static void ApplyCustomPalette(Color bg, Color accent)
    {
        BackgroundColor = bg;
        AccentColor = accent;

        var res = WpfApp.Current?.Resources;
        if (res == null) return;

        double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        bool isLight = lum > 0.5;

        res["AppBackgroundBrush"] = CreateFrozenBrush(bg);

        var (bgH, bgS, bgV) = RgbToHsv(bg);
        var (accH, accS, accV) = RgbToHsv(accent);

        Color sidebarColor, cardColor, cardStrokeColor, inputBgColor, inputHoverColor, inputStrokeColor;
        Color buttonBgColor, buttonHoverColor, buttonPressedColor;
        Color textForeColor, subtleTextColor, iconColor;

        cardStrokeColor = Color.FromArgb(0, 0, 0, 0);
        inputStrokeColor = Color.FromArgb(0, 0, 0, 0);

        bool useSidebarColorForControls =
            (bg.R == 0x20 && bg.G == 0x20 && bg.B == 0x20) ||
            (bg.R == 0xF3 && bg.G == 0xF3 && bg.B == 0xF3) ||
            (bg.R == 0xFF && bg.G == 0xFF && bg.B == 0xFF);

        if (isLight)
        {
            sidebarColor = HsvToRgb(bgH, Math.Min(1.0, bgS + 0.03), Math.Max(0.0, bgV - 0.05));
            cardColor = HsvToRgb(bgH, Math.Max(0.0, bgS - 0.02), Math.Min(1.0, bgV + 0.04));

            if (useSidebarColorForControls)
            {
                var (sbH, sbS, sbV) = RgbToHsv(sidebarColor);
                buttonBgColor = sidebarColor;
                buttonHoverColor = HsvToRgb(sbH, sbS, Math.Max(0.0, sbV - 0.05));
                buttonPressedColor = HsvToRgb(sbH, sbS, Math.Max(0.0, sbV - 0.10));
                inputBgColor = buttonBgColor;
                inputHoverColor = buttonHoverColor;
            }
            else
            {
                double btnH = accH;
                double btnS = Math.Clamp(Math.Max(bgS, accS * 0.22), 0.08, 0.22);
                double btnV = Math.Clamp(bgV - 0.12, 0.82, 0.92);

                buttonBgColor = HsvToRgb(btnH, btnS, btnV);
                buttonHoverColor = HsvToRgb(btnH, Math.Min(1.0, btnS + 0.06), Math.Max(0.0, btnV - 0.08));
                buttonPressedColor = HsvToRgb(btnH, Math.Min(1.0, btnS + 0.12), Math.Max(0.0, btnV - 0.16));
                inputBgColor = buttonBgColor;
                inputHoverColor = buttonHoverColor;
            }

            textForeColor = Color.FromRgb(0x1A, 0x1A, 0x1A);
            subtleTextColor = Color.FromRgb(0x5F, 0x5F, 0x5F);
            iconColor = Color.FromRgb(0x2C, 0x2C, 0x2C);

            res["NavButtonHoverBrush"] = CreateFrozenBrush(buttonHoverColor);
            res["NavButtonPressedBrush"] = CreateFrozenBrush(buttonPressedColor);
            res["SecondaryButtonBackgroundBrush"] = CreateFrozenBrush(buttonBgColor);
            res["ItemContainerBackgroundBrush"] = CreateFrozenBrush(buttonBgColor);
            res["ScrollBarThumbBrush"] = CreateFrozenBrush(HsvToRgb(bgH, bgS, Math.Max(0.0, bgV - 0.25)));
            res["ScrollBarThumbHoverBrush"] = CreateFrozenBrush(HsvToRgb(bgH, bgS, Math.Max(0.0, bgV - 0.35)));

            res["ErrorBackgroundBrush"] = CreateFrozenBrush(Color.FromRgb(0xFD, 0xEA, 0xEA));
            res["ErrorBorderBrush"] = CreateFrozenBrush(Color.FromArgb(0, 0, 0, 0));
            res["ErrorTitleBrush"] = CreateFrozenBrush(Color.FromRgb(0xC5, 0x22, 0x1F));
            res["ErrorTextBrush"] = CreateFrozenBrush(Color.FromRgb(0x5F, 0x21, 0x20));
            res["MenuBorderBrush"] = CreateFrozenBrush(Color.FromArgb(0x28, 0x00, 0x00, 0x00));
            res["MenuSeparatorBrush"] = CreateFrozenBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            res["MenuItemHoverBrush"] = CreateFrozenBrush(HsvToRgb(bgH, bgS, Math.Max(0.0, bgV - 0.08)));
        }
        else
        {
            sidebarColor = HsvToRgb(bgH, Math.Min(1.0, bgS + 0.04), Math.Max(0.0, bgV - 0.04));
            cardColor = HsvToRgb(bgH, Math.Max(0.0, bgS - 0.03), Math.Min(1.0, bgV + 0.07));

            if (useSidebarColorForControls)
            {
                var (sbH, sbS, sbV) = RgbToHsv(sidebarColor);
                buttonBgColor = sidebarColor;
                buttonHoverColor = HsvToRgb(sbH, sbS, Math.Min(1.0, sbV + 0.05));
                buttonPressedColor = HsvToRgb(sbH, sbS, Math.Max(0.04, sbV - 0.02));
                inputBgColor = buttonBgColor;
                inputHoverColor = buttonHoverColor;
            }
            else
            {
                double cardV = Math.Min(1.0, bgV + 0.07);
                double btnH = (bgS > 0.25) ? bgH : accH;
                double btnS = Math.Clamp(Math.Max(bgS * 1.1, accS * 0.35), 0.18, 0.40);
                double btnV = Math.Clamp(cardV - 0.07, 0.07, 0.14);

                buttonBgColor = HsvToRgb(btnH, btnS, btnV);
                buttonHoverColor = HsvToRgb(btnH, Math.Min(1.0, btnS + 0.06), Math.Min(1.0, btnV + 0.06));
                buttonPressedColor = HsvToRgb(btnH, Math.Min(1.0, btnS + 0.14), Math.Max(0.04, btnV - 0.04));
                inputBgColor = buttonBgColor;
                inputHoverColor = buttonHoverColor;
            }

            textForeColor = Color.FromRgb(0xF3, 0xF3, 0xF3);
            subtleTextColor = Color.FromRgb(0x9E, 0x9E, 0x9E);
            iconColor = Color.FromRgb(0xFF, 0xFF, 0xFF);

            res["NavButtonHoverBrush"] = CreateFrozenBrush(buttonHoverColor);
            res["NavButtonPressedBrush"] = CreateFrozenBrush(buttonPressedColor);
            res["SecondaryButtonBackgroundBrush"] = CreateFrozenBrush(buttonBgColor);
            res["ItemContainerBackgroundBrush"] = CreateFrozenBrush(buttonBgColor);
            res["ScrollBarThumbBrush"] = CreateFrozenBrush(HsvToRgb(bgH, bgS, Math.Min(1.0, bgV + 0.15)));
            res["ScrollBarThumbHoverBrush"] = CreateFrozenBrush(HsvToRgb(bgH, bgS, Math.Min(1.0, bgV + 0.28)));

            res["ErrorBackgroundBrush"] = CreateFrozenBrush(Color.FromRgb(0x33, 0x1A, 0x1A));
            res["ErrorBorderBrush"] = CreateFrozenBrush(Color.FromArgb(0, 0, 0, 0));
            res["ErrorTitleBrush"] = CreateFrozenBrush(Color.FromRgb(0xFF, 0x52, 0x52));
            res["ErrorTextBrush"] = CreateFrozenBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
            res["MenuBorderBrush"] = CreateFrozenBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            res["MenuSeparatorBrush"] = CreateFrozenBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            res["MenuItemHoverBrush"] = CreateFrozenBrush(HsvToRgb(bgH, bgS, Math.Min(1.0, bgV + 0.15)));
        }

        res["SidebarBackgroundBrush"] = CreateFrozenBrush(sidebarColor);
        res["CardBackgroundBrush"] = CreateFrozenBrush(cardColor);
        res["CardStrokeBrush"] = CreateFrozenBrush(cardStrokeColor);
        res["InputBackgroundBrush"] = CreateFrozenBrush(inputBgColor);
        res["InputHoverBackgroundBrush"] = CreateFrozenBrush(inputHoverColor);
        res["InputStrokeBrush"] = CreateFrozenBrush(inputStrokeColor);
        res["TextForegroundBrush"] = CreateFrozenBrush(textForeColor);
        res["SubtleTextBrush"] = CreateFrozenBrush(subtleTextColor);
        res["IconBrush"] = CreateFrozenBrush(iconColor);

        res["SystemAccentBrush"] = CreateFrozenBrush(accent);
        res["PowerActiveBrush"] = CreateFrozenBrush(accent);
        res["PowerInactiveBrush"] = CreateFrozenBrush(isLight ? Color.FromRgb(0x8A, 0x8A, 0x8A) : Color.FromRgb(0x60, 0x60, 0x60));

        Color accHoverColor, accPressedColor;
        if (isLight)
        {
            accHoverColor = HsvToRgb(accH, Math.Max(0, accS - 0.1), Math.Min(1.0, accV + 0.08));
            accPressedColor = HsvToRgb(accH, Math.Min(1.0, accS + 0.1), Math.Max(0, accV - 0.12));
        }
        else
        {
            accHoverColor = HsvToRgb(accH, Math.Max(0, accS - 0.05), Math.Min(1.0, accV + 0.12));
            accPressedColor = HsvToRgb(accH, Math.Min(1.0, accS + 0.1), Math.Max(0, accV - 0.15));
        }
        res["SystemAccentHoverBrush"] = CreateFrozenBrush(accHoverColor);
        res["SystemAccentPressedBrush"] = CreateFrozenBrush(accPressedColor);

        AccentColorChanged?.Invoke(accent);
        BackgroundColorChanged?.Invoke(bg);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static void ApplyAccentColor(Color accent)
    {
        ApplyCustomPalette(BackgroundColor, accent);
    }

    public static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Color.FromRgb(0x00, 0x78, 0xD4);
        if (string.IsNullOrWhiteSpace(hex)) return false;

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            color = Color.FromRgb(r, g, b);
            return true;
        }
        else if (clean.Length == 8 &&
                 uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            byte a = (byte)((argb >> 24) & 0xFF);
            byte r = (byte)((argb >> 16) & 0xFF);
            byte g = (byte)((argb >> 8) & 0xFF);
            byte b = (byte)(argb & 0xFF);
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    public static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        double r1 = 0, g1 = 0, b1 = 0;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        byte r = (byte)Math.Clamp(Math.Round((r1 + m) * 255), 0, 255);
        byte g = (byte)Math.Clamp(Math.Round((g1 + m) * 255), 0, 255);
        byte b = (byte)Math.Clamp(Math.Round((b1 + m) * 255), 0, 255);
        return Color.FromRgb(r, g, b);
    }

    public static (double h, double s, double v) RgbToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0.00001)
        {
            if (Math.Abs(max - r) < 0.00001) h = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < 0.00001) h = 60 * (((b - r) / delta) + 2);
            else h = 60 * (((r - g) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0.00001 ? 0 : delta / max;
        double v = max;

        return (h, s, v);
    }
}
