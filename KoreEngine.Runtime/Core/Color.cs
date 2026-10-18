namespace KoreEngine;

public class Color
{
    public int R, G, B, A;

    public Color(int red, int green, int blue, int alpha)
    {
        R = red;
        G = green;
        B = blue;
        A = alpha;
    }

    public Color(int red, int green, int blue)
    {
        R = red;
        G = green;
        B = blue;
        A = 255;
    }

    public static Color Black => new(0, 0, 0, 255);
    public static Color White => new(255, 255, 255, 255);
    public static Color Red => new(255, 0, 0, 255);
    public static Color Green => new(0, 255, 0, 255);
    public static Color Blue => new(0, 0, 255, 255);
}
