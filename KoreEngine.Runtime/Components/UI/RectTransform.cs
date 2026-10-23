namespace KoreEngine.UI;

public readonly struct RectF
{
    public readonly Vector2 Min, Max;   // monde, Y-haut : Min = coin bas-gauche
    public RectF(Vector2 min, Vector2 max) { Min = min; Max = max; }
    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
}

public class RectTransform : Transform
{
    public Vector2 AnchorMin = new(0.5f, 0.5f);
    public Vector2 AnchorMax = new(0.5f, 0.5f);
    public Vector2 Pivot = new(0.5f, 0.5f);
    public Vector2 SizeDelta = new(100f, 100f);
    // Position (héritée) = AnchoredPosition : pivot relatif au point d'ancrage.

    RectF ParentRect
    {
        get
        {
            if (Parent?.transform is RectTransform pr) return pr.Rect;
            var p = Parent != null ? Parent.transform.WorldPosition : new Vector2(0f, 0f);
            return new RectF(p, p);   // parent non-rect : zone de taille nulle
        }
    }

    Vector2 AnchorReference(RectF p) => new(
        p.Min.X + p.Width * (AnchorMin.X + (AnchorMax.X - AnchorMin.X) * Pivot.X),
        p.Min.Y + p.Height * (AnchorMin.Y + (AnchorMax.Y - AnchorMin.Y) * Pivot.Y));

    public RectF Rect
    {
        get
        {
            var p = ParentRect;
            var r = AnchorReference(p);
            float w = p.Width * (AnchorMax.X - AnchorMin.X) + SizeDelta.X;
            float h = p.Height * (AnchorMax.Y - AnchorMin.Y) + SizeDelta.Y;
            float minX = r.X + Position.X - Pivot.X * w;
            float minY = r.Y + Position.Y - Pivot.Y * h;
            return new RectF(new Vector2(minX, minY), new Vector2(minX + w, minY + h));
        }
    }

    // Position monde du pivot : c'est ce que lisent les gizmos.
    public override Vector2 WorldPosition
    {
        get { var r = Rect; return new Vector2(r.Min.X + Pivot.X * r.Width, r.Min.Y + Pivot.Y * r.Height); }
    }

    protected override Vector2 ComputeLocalPosition(Vector2 worldPos, Transform parentTransform, Vector2 safeParentScale)
    {
        var r = AnchorReference(ParentRect);
        return new Vector2(worldPos.X - r.X, worldPos.Y - r.Y);
    }

    /// Aide à la migration : ancien format (coin haut-gauche du canvas, Y vers le bas).
    public void SetTopLeft(float x, float y, float w, float h)
    {
        AnchorMin = AnchorMax = Pivot = new Vector2(0f, 1f);
        Position = new Vector2(x, -y);
        SizeDelta = new Vector2(w, h);
    }

    public void SetWorldRect(Vector2 min, Vector2 max)
    {
        var p = ParentRect;
        var r = AnchorReference(p);
        float w = MathF.Max(1f, max.X - min.X);
        float h = MathF.Max(1f, max.Y - min.Y);
        SizeDelta = new Vector2(w - p.Width * (AnchorMax.X - AnchorMin.X),
                                h - p.Height * (AnchorMax.Y - AnchorMin.Y));
        Position = new Vector2(min.X - r.X + Pivot.X * w, min.Y - r.Y + Pivot.Y * h);
    }

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new FloatField { Label = "Pos X", Get = () => Position.X, Set = v => Position = new Vector2(v, Position.Y) };
        yield return new FloatField { Label = "Pos Y", Get = () => Position.Y, Set = v => Position = new Vector2(Position.X, v) };
        yield return new FloatField { Label = "Width", Get = () => SizeDelta.X, Set = v => SizeDelta = new Vector2(v, SizeDelta.Y) };
        yield return new FloatField { Label = "Height", Get = () => SizeDelta.Y, Set = v => SizeDelta = new Vector2(SizeDelta.X, v) };
        yield return new FloatField { Label = "Anchor Min X", Get = () => AnchorMin.X, Set = v => AnchorMin = new Vector2(v, AnchorMin.Y) };
        yield return new FloatField { Label = "Anchor Min Y", Get = () => AnchorMin.Y, Set = v => AnchorMin = new Vector2(AnchorMin.X, v) };
        yield return new FloatField { Label = "Anchor Max X", Get = () => AnchorMax.X, Set = v => AnchorMax = new Vector2(v, AnchorMax.Y) };
        yield return new FloatField { Label = "Anchor Max Y", Get = () => AnchorMax.Y, Set = v => AnchorMax = new Vector2(AnchorMax.X, v) };
        yield return new FloatField { Label = "Pivot X", Get = () => Pivot.X, Set = v => Pivot = new Vector2(v, Pivot.Y) };
        yield return new FloatField { Label = "Pivot Y", Get = () => Pivot.Y, Set = v => Pivot = new Vector2(Pivot.X, v) };
    }
}