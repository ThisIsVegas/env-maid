using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EnvMaid.App.Models;

namespace EnvMaid.App.Converters;

/// <summary>Tints a grid row by the worst thing found on that entry.</summary>
public class ConfidenceToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0x45, 0x28, 0x2E));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0x45, 0x3F, 0x28));
    private static readonly SolidColorBrush NoneBrush = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Severity.Error => ErrorBrush,
            Severity.Warning => WarningBrush,
            // Info is deliberately untinted: a quoted or padded entry is worth mentioning, not
            // worth colouring a row over.
            _ => NoneBrush,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
