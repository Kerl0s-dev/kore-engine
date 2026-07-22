namespace KoreEngine.Core;

public class Color
{
    public int R, G, B;

    public Color(int red, int green, int blue)
    {
        R = red;
        G = green;
        B = blue;
    }

    public static Color Black => new(0, 0, 0);
    public static Color White => new(255, 255, 255);
    public static Color Red => new(255, 0, 0);
    public static Color Green => new(0, 255, 0);
    public static Color Blue => new(0, 0, 255);
}
