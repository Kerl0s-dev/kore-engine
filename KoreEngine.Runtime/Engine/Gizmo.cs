using System.ComponentModel;

namespace KoreEngine;

public static class Gizmo
{
    // Contexte actif pour la frame en cours (géré par le ScenePanel)
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Renderer? CurrentRenderer { get; set; }
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Camera? CurrentCamera { get; set; }

    /// <summary>Couleur actuelle utilisée pour dessiner les gizmos.</summary>
    public static Color Color { get; set; } = new Color(255, 255, 255, 255);

    /// <summary>Dessine une ligne entre deux points du monde.</summary>
    public static void DrawLine(Vector2 p1, Vector2 p2)
    {
        if (CurrentRenderer == null || CurrentCamera == null) return;
        CurrentRenderer.WorldLine(CurrentCamera, p1, p2, (byte)Color.R, (byte)Color.G, (byte)Color.B, (byte)Color.A);
    }

    /// <summary>Dessine un rectangle filaire (boîte 2D).</summary>
    public static void DrawWireRect(Vector2 center, Vector2 size)
    {
        if (CurrentRenderer == null || CurrentCamera == null) return;

        float hw = size.X * 0.5f;
        float hh = size.Y * 0.5f;

        var p0 = new Vector2(center.X - hw, center.Y + hh); // Top-Left
        var p1 = new Vector2(center.X + hw, center.Y + hh); // Top-Right
        var p2 = new Vector2(center.X + hw, center.Y - hh); // Bottom-Right
        var p3 = new Vector2(center.X - hw, center.Y - hh); // Bottom-Left

        DrawLine(p0, p1);
        DrawLine(p1, p2);
        DrawLine(p2, p3);
        DrawLine(p3, p0);
    }

    /// <summary>Dessine un cercle filaire dans le monde.</summary>
    public static void DrawWireCircle(Vector2 center, float radius, int segments = 24)
    {
        if (CurrentRenderer == null || CurrentCamera == null) return;

        float step = 2f * MathF.PI / segments;
        for (int i = 0; i < segments; i++)
        {
            float a1 = i * step;
            float a2 = (i + 1) * step;

            var p1 = new Vector2(center.X + MathF.Cos(a1) * radius, center.Y + MathF.Sin(a1) * radius);
            var p2 = new Vector2(center.X + MathF.Cos(a2) * radius, center.Y + MathF.Sin(a2) * radius);

            DrawLine(p1, p2);
        }
    }

    /// <summary>Dessine un rayon (flèche/ligne orientée).</summary>
    public static void DrawRay(Vector2 start, Vector2 direction)
    {
        DrawLine(start, start + direction);
    }
}