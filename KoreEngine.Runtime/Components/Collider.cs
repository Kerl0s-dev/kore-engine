using System.ComponentModel;

namespace KoreEngine;

public class Collider : Component
{
    public ColliderShapeType Shape = ColliderShapeType.Box;

    // Dimensions locales en MÈTRES (avant échelle du Transform).
    //   Box     : Width x Height
    //   Circle  : diamètre = min(Width, Height)
    //   Capsule : boîte englobante Width x Height ; verticale si Height >= Width, sinon horizontale
    public float Width = 1f, Height = 1f;

    // Décalage en MÈTRES dans le repère de l'objet : il suit la rotation du Transform.
    public float OffsetX = 0f, OffsetY = 0f;

    public string Tag = ""; // "player", "enemy", "trigger"...
    public bool IsTrigger = false; // trigger = détecte sans résoudre

    // Les événements renvoient désormais vers le GameObject
    // Masqué dans Visual Studio pour ne pas polluer l'autocomplétion de l'utilisateur
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void OnCollision(Collider other)
    {
        gameObject.DispatchCollision(other);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public override void OnTriggerEnter(Collider other)
    {
        gameObject.DispatchTrigger(other);
    }

    /// <summary>Forme réelle du collider dans le monde (position, rotation et échelle du Transform appliquées).</summary>
    public WorldShape GetWorldShape()
    {
        var t = gameObject.transform;
        var pos = t.WorldPosition;
        var scale = t.WorldScale;

        float w = MathF.Abs(Width * scale.X);
        float h = MathF.Abs(Height * scale.Y);

        float rad = t.WorldRotation * (MathF.PI / 180f);
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var ax = new Vector2(cos, sin);   // axe X local (anti-horaire, Y vers le haut)
        var ay = new Vector2(-sin, cos);  // axe Y local

        var center = pos + ax * OffsetX + ay * OffsetY;
        var s = new WorldShape { Type = Shape, Center = center, AxisX = ax, AxisY = ay };

        switch (Shape)
        {
            case ColliderShapeType.Circle:
                s.Radius = MathF.Min(w, h) * 0.5f;
                s.SegA = s.SegB = center;
                break;

            case ColliderShapeType.Capsule:
                if (h >= w)
                {
                    s.Radius = w * 0.5f;
                    float half = (h - w) * 0.5f;
                    s.SegA = center - ay * half;
                    s.SegB = center + ay * half;
                }
                else
                {
                    s.Radius = h * 0.5f;
                    float half = (w - h) * 0.5f;
                    s.SegA = center - ax * half;
                    s.SegB = center + ax * half;
                }
                break;

            default:
                s.HalfExtents = new Vector2(w * 0.5f, h * 0.5f);
                break;
        }
        return s;
    }

    /// <summary>Moment d'inertie (kg·m²) d'un solide homogène de cette masse, autour du centre du collider.</summary>
    public float ComputeInertia(float mass) => ShapeInertia.Compute(GetWorldShape(), mass);

    /// <summary>AABB englobant la forme (exact pour une Box alignée sur les axes).</summary>
    public Rectangle Bounds => GetWorldShape().Bounds;
}