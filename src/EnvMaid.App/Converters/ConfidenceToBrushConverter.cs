using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EnvMaid.App.Models;

namespace EnvMaid.App.Converters;

public class ConfidenceToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush HighBrush = new(Color.FromRgb(0x45, 0x28, 0x2E));
    private static readonly SolidColorBrush LowBrush = new(Color.FromRgb(0x45, 0x3F, 0x28));
    private static readonly SolidColorBrush NoneBrush = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            FlagConfidence.High => HighBrush,
            FlagConfidence.Low => LowBrush,
            _ => NoneBrush
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
