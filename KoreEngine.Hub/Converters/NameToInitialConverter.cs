using System.Globalization;
using System.Windows.Data;

namespace KoreEngine.Hub.Converters;

public class NameToInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string name = value as string ?? string.Empty;
        return name.Length > 0 ? name[0].ToString().ToUpperInvariant() : "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
