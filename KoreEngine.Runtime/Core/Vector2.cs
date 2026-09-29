namespace KoreEngine.Core;

public struct Vector2
{
    public float X, Y;
    public Vector2(float x, float y) { X = x; Y = y; }

    public Vector2(float v) { X = v; Y = v; }

    public static Vector2 operator +(Vector2 a) => new(+a.X, +a.Y);
    public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);

    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
    public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);

    public static Vector2 Zero => new(0, 0);
    public static Vector2 One => new(1, 1);
    public static Vector2 NegativeOne => new(-1, -1);
    public static Vector2 Up => new(0, 1);
    public static Vector2 Down => new(0, -1);
    public static Vector2 Left => new(-1, 0);
    public static Vector2 Right => new(1, 0);

    public float Length() => MathF.Sqrt(X * X + Y * Y);

    public Vector2 Normalize()
    {
        float len = Length();
        if (len == 0) return Zero;
        return new Vector2(X / len, Y / len);
    }

    public static float Distance(Vector2 a, Vector2 b) {
        float result = (float)Math.Sqrt(Math.Pow((b.X - a.X), 2) + Math.Pow((b.Y - a.Y), 2));

        return result;
    }

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return new Vector2(
            a.X + (b.X - a.X) * Math.Clamp(t, 0f, 1f),
            a.Y + (b.Y - a.Y) * Math.Clamp(t, 0f, 1f)
        );
    }

    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
    {
        return new Vector2(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    public override string ToString() => $"({X}, {Y})";
}