using System.ComponentModel;

namespace KoreEngine;

public class Collider : Component
{
    public int Width, Height;
    public int OffsetX, OffsetY;
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

    public Rectangle Bounds
    {
        get
        {
            var pos = gameObject.transform.WorldPosition;
            var scale = gameObject.transform.WorldScale;
            int w = (int)(Width * scale.X);
            int h = (int)(Height * scale.Y);
            int x = (int)(pos.X - w / 2f) + OffsetX;
            int y = (int)(pos.Y - h / 2f) + OffsetY;
            return new Rectangle(x, y, w, h);
        }
    }
}