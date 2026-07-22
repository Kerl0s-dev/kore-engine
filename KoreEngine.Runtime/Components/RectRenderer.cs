using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Components;

public class RectRenderer : Component
{
    public Vector2 Size;
    public Color Color = Color.White;

    public override void Render(Renderer renderer, Camera? camera)
    {
        var scale = Owner.WorldScale;
        int w = (int)(Size.X * scale.X);
        int h = (int)(Size.Y * scale.Y);

        var pos = Owner.WorldPosition;
        int x = (int)(pos.X - w / 2f);
        int y = (int)(pos.Y + h / 2f); // + et non -, car Y-haut : le "haut" visuel = Y max

        if (camera != null)
            renderer.DrawRect(x, y, w, h, Color, 255, camera);
        else
            renderer.DrawRect(x, y, w, h, Color, 255);
    }
}