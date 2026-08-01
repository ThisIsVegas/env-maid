using System.Globalization;
using System.Windows.Data;

namespace EnvMaid.App.Converters;

/// <summary>
/// Two-way bind a radio button to one value of an enum: IsChecked is true when the
/// bound enum equals the ConverterParameter (the enum member name).
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter as string;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name && Enum.TryParse(targetType, name, out var result)
            ? result
            : Binding.DoNothing;
}
