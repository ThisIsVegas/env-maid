using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EnvMaid.App.Models;

namespace EnvMaid.App.Converters;

/// <summary>ConflictConfidence -> brush (parameter "brush") or label (default).</summary>
public class ConflictConfidenceConverter : IValueConverter
{
    private static readonly SolidColorBrush RealBrush = new(Color.FromRgb(0xF3, 0x8B, 0xA8));   // red
    private static readonly SolidColorBrush MaybeBrush = new(Color.FromRgb(0xFA, 0xB3, 0x87));  // amber
    private static readonly SolidColorBrush FalseBrush = new(Color.FromRgb(0x6C, 0x70, 0x86));  // grey

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var confidence = value as ConflictConfidence? ?? ConflictConfidence.Possibly;
        var asBrush = string.Equals(parameter as string, "brush", StringComparison.OrdinalIgnoreCase);

        return confidence switch
        {
            ConflictConfidence.LikelyReal => asBrush ? RealBrush : "Likely real",
            ConflictConfidence.Possibly => asBrush ? MaybeBrush : "Possibly",
            _ => asBrush ? FalseBrush : "Likely false-positive",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
