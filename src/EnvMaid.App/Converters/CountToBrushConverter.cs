using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EnvMaid.App.Converters;

/// <summary>
/// Count -> brush. Zero is muted (nothing to worry about); a positive count uses
/// the alert color named by ConverterParameter ("red" or "amber", default red).
/// </summary>
public class CountToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x6C, 0x70, 0x86));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xF3, 0x8B, 0xA8));
    private static readonly SolidColorBrush Amber = new(Color.FromRgb(0xFA, 0xB3, 0x87));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value as int? ?? 0;
        if (count == 0)
            return Muted;
        return string.Equals(parameter as string, "amber", StringComparison.OrdinalIgnoreCase) ? Amber : Red;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
