using KoreEngine.Core;

namespace KoreEngine.Components;

public class Camera : Component
{
    public float Zoom = 1f;
    public int ViewWidth, ViewHeight;

    // Position interne — utilisée uniquement quand Owner == null (EditorCamera).
    // Pour une Camera normale sur un GameObject, la position réelle est
    // Owner.WorldPosition ; le setter passe par Owner.LocalPosition.
    protected Vector2 _position;

    /// <summary>
    /// Position monde de la caméra.
    /// Get : Owner.WorldPosition si attachée à un objet, sinon _position.
    /// Set : Owner.LocalPosition si attachée à un objet, sinon _position.
    /// </summary>
    public virtual Vector2 Position
    {
        get => Owner != null ? Owner.WorldPosition : _position;
        set
        {
            if (Owner != null) Owner.LocalPosition = value;
            else _position = value;
        }
    }

    public Camera() : this(800, 600) { }
    public Camera(int viewWidth = 800, int viewHeight = 600)
    {
        ViewWidth = viewWidth;
        ViewHeight = viewHeight;
    }

    public Vector2 WorldToScreen(Vector2 worldPos) => new Vector2(
        (worldPos.X - Position.X) * Zoom + ViewWidth * 0.5f,
        (Position.Y - worldPos.Y) * Zoom + ViewHeight * 0.5f   // Y inversé : monde Y-haut → écran Y-bas
    );

    public Vector2 ScreenToWorld(Vector2 screenPos) => new Vector2(
        (screenPos.X - ViewWidth * 0.5f) / Zoom + Position.X,
        Position.Y - (screenPos.Y - ViewHeight * 0.5f) / Zoom   // symétrique, même inversion
    );

    public Rectangle Bounds => new Rectangle(
        (int)(Position.X - ViewWidth * 0.5f / Zoom),
        (int)(Position.Y - ViewHeight * 0.5f / Zoom), // Y du coin "bas" en Y-haut, à nommer/interpréter en conséquence
        (int)(ViewWidth / Zoom),
        (int)(ViewHeight / Zoom)
    );

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        // Position (lecture seule ici — on déplace la caméra en déplaçant son GameObject)
        yield return new TextField { Label = "Pos X", Get = () => $"{Position.X:F1}" };
        yield return new TextField { Label = "Pos Y", Get = () => $"{Position.Y:F1}" };

        yield return new FloatField
        {
            Label = "Zoom",
            Get = () => Zoom,
            Set = v => Zoom = v,
            Speed = 0.01f,
            Min = 0.05f,
            Max = 20f
        };

        yield return new TextField { Label = "View W", Get = () => $"{ViewWidth}" };
        yield return new TextField { Label = "View H", Get = () => $"{ViewHeight}" };
    }
}