using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KoreEngine.Hub.Converters;

/// <summary>Attribue une couleur stable à un projet à partir de son nom, pour la vignette de la liste.</summary>
public class NameToColorConverter : IValueConverter
{
    static readonly Color[] Palette =
    {
        Color.FromRgb(0x3A, 0x80, 0xC8), // bleu
        Color.FromRgb(0x5C, 0x9A, 0x5C), // vert
        Color.FromRgb(0x7A, 0x5C, 0x9A), // violet
        Color.FromRgb(0xC8, 0x86, 0x3A), // orange
        Color.FromRgb(0xC8, 0x5A, 0x5A), // rouge
        Color.FromRgb(0x4A, 0xA8, 0xA0), // teal
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string name = value as string ?? string.Empty;
        int index = Math.Abs(name.GetHashCode()) % Palette.Length;
        return new SolidColorBrush(Palette[index]);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
