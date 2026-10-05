using System;

namespace KoreEngine
{
    public struct Vector2 : IEquatable<Vector2>
    {
        public float X;
        public float Y;

        public Vector2(float x, float y) { X = x; Y = y; }
        public Vector2(float v) { X = v; Y = v; }

        // Unary operators
        public static Vector2 operator +(Vector2 a) => a;
        public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);

        // Vector Arithmetic
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);
        public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.X / b.X, a.Y / b.Y);

        // Scalar Arithmetic
        public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
        public static Vector2 operator *(float s, Vector2 a) => new(a.X * s, a.Y * s);
        public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);

        // Direction Constants
        public static Vector2 Zero => new(0f, 0f);
        public static Vector2 One => new(1f, 1f);
        public static Vector2 NegativeOne => new(-1f, -1f);
        public static Vector2 Up => new(0f, 1f);
        public static Vector2 Down => new(0f, -1f);
        public static Vector2 Left => new(-1f, 0f);
        public static Vector2 Right => new(1f, 0f);

        // Length / Distance
        public float Length() => MathF.Sqrt(X * X + Y * Y);
        public float LengthSquared() => X * X + Y * Y;

        public Vector2 Normalize()
        {
            float len = Length();
            return len > 0.00001f ? new Vector2(X / len, Y / len) : Zero;
        }

        public static float Distance(Vector2 a, Vector2 b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public static float DistanceSquared(Vector2 a, Vector2 b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return dx * dx + dy * dy;
        }

        // Vector Products
        public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

        // 2D Cross product returns a scalar representing the Z-component of 3D cross product
        public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        // Interpolation
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector2(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t
            );
        }

        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
        {
            return new Vector2(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t
            );
        }

        // Equality & Overrides
        public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

        public bool Equals(Vector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X}, {Y})";
    }
}