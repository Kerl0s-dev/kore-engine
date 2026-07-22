using KoreEngine.Core;
using KoreEngine.Physics;

namespace KoreEngine.Components;

public class Collider : Component
{
    public int Width, Height;
    public int OffsetX, OffsetY;
    public string Tag = ""; // "player", "enemy", "trigger"...
    public bool IsTrigger = false; // trigger = détecte sans résoudre

    public Action<Collider, CollisionInfo>? OnCollision;
    public Action<Collider, CollisionInfo>? OnTriggerEnter;

    public Rectangle Bounds
    {
        get
        {
            var pos = Owner.WorldPosition;
            var scale = Owner.WorldScale;
            int w = (int)(Width * scale.X);
            int h = (int)(Height * scale.Y);
            int x = (int)(pos.X - w / 2f) + OffsetX;
            int y = (int)(pos.Y - h / 2f) + OffsetY;
            return new Rectangle(x, y, w, h);
        }
    }
}