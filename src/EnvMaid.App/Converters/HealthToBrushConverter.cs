using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EnvMaid.App.Converters;

/// <summary>Health label -> accent brush (green healthy, amber minor, red attention).</summary>
public class HealthToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0xA6, 0xE3, 0xA1));
    private static readonly SolidColorBrush Amber = new(Color.FromRgb(0xFA, 0xB3, 0x87));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xF3, 0x8B, 0xA8));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "Needs attention" => Red,
            "Minor issues" => Amber,
            _ => Green,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
