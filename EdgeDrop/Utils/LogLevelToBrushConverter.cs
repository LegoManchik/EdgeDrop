using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EdgeDrop;

public class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush SuccessBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0x54, 0xB0, 0x54));
    private static readonly SolidColorBrush WarningBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00));
    private static readonly SolidColorBrush ErrorBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0xFF, 0x52, 0x52));
    private static readonly SolidColorBrush DefaultBrush = CreateFrozen(System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));

    private static SolidColorBrush CreateFrozen(System.Windows.Media.Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => InfoBrush,
                LogLevel.Success => SuccessBrush,
                LogLevel.Warning => WarningBrush,
                LogLevel.Error => ErrorBrush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
