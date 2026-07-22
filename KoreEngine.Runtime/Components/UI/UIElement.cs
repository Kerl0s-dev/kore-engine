using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Components.UI;

public abstract class UIElement : Component
{
    public float X, Y;
    public float Width, Height;
    public bool Visible = true;

    public UICanvas? Canvas;

    public float ScreenX => X * (Canvas?.ScaleX ?? 1f);
    public float ScreenY => Y * (Canvas?.ScaleY ?? 1f);
    public float ScreenWidth => Width * (Canvas?.ScaleX ?? 1f);
    public float ScreenHeight => Height * (Canvas?.ScaleY ?? 1f);

    public override void Render(Renderer renderer, Camera? camera)
    {
        if (!Visible) return;
        Draw(renderer);
    }

    public abstract void Draw(Renderer renderer);
}