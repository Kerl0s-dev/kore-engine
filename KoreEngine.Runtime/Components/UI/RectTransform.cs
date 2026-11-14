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
    public Vector2 SizeDelta = new(1f, 1f);
    // Position (héritée) = AnchoredPosition : pivot relatif au point d'ancrage, en MÈTRES (unités monde).
    // SizeDelta est en PIXELS (convertis via Renderer.PixelsPerUnit dans Rect). Taille minimale : 1 px.
    static float MinSizeWorld => 1f / Renderer.PixelsPerUnit;

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

            // Si SizeDelta est en pixels, convertissez-le en unités monde :
            float sizeFactor = 1f / Renderer.PixelsPerUnit;

            float w = p.Width * (AnchorMax.X - AnchorMin.X) + (SizeDelta.X * sizeFactor);
            float h = p.Height * (AnchorMax.Y - AnchorMin.Y) + (SizeDelta.Y * sizeFactor);

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

    public void SetWorldRect(Vector2 min, Vector2 max)
    {
        var p = ParentRect;
        var r = AnchorReference(p);
        float w = MathF.Max(MinSizeWorld, max.X - min.X);
        float h = MathF.Max(MinSizeWorld, max.Y - min.Y);
        SizeDelta = new Vector2((w - p.Width * (AnchorMax.X - AnchorMin.X)) * Renderer.PixelsPerUnit,
                                (h - p.Height * (AnchorMax.Y - AnchorMin.Y)) * Renderer.PixelsPerUnit);
        Position = new Vector2(min.X - r.X + Pivot.X * w, min.Y - r.Y + Pivot.Y * h);
    }

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new FloatField { Label = "Pos X", Get = () => Position.X * Renderer.PixelsPerUnit, Set = v => Position = new Vector2(v / Renderer.PixelsPerUnit, Position.Y) };
        yield return new FloatField { Label = "Pos Y", Get = () => Position.Y * Renderer.PixelsPerUnit, Set = v => Position = new Vector2(Position.X, v / Renderer.PixelsPerUnit) };
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