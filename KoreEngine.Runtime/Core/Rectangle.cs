namespace KoreEngine.Core;

public struct Rectangle
{
    public int X, Y, Width, Height;

    public Rectangle(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public int Left => X;
    public int Right => X + Width;
    public int Top => Y;
    public int Bottom => Y + Height;

    public bool Intersects(Rectangle other)
    {
        return Left < other.Right &&
               Right > other.Left &&
               Top < other.Bottom &&
               Bottom > other.Top;
    }
}