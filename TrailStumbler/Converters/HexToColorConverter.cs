using System.Globalization;

namespace TrailStumbler.Converters;

public class HexToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Color.TryParse(value as string, out var color) ? color : Colors.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? c.ToArgbHex() : "#808080";
}
