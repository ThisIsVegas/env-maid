using System.Globalization;
using System.Windows.Data;

namespace EnvMaid.App.Converters;

public class LengthToWidthConverter : IValueConverter
{
    private const double MaxBarWidth = 700;
    private const double PathLimit = 2047;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var length = value is int i ? i : 0;
        var ratio = System.Math.Min(1.0, length / PathLimit);
        return MaxBarWidth * ratio;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
