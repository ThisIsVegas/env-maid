using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace EnvMaid.App.Converters;

public class PathExistsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path) return false;
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Directory.Exists(expanded);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
