namespace KoreEngine.UI;

public abstract class UIElement : Component
{
    public Rectangle Rect;
    public bool Visible = true;

    public UICanvas? Canvas;

    public float ScreenX => Rect.X * (Canvas?.ScaleX ?? 1f);
    public float ScreenY => Rect.Y * (Canvas?.ScaleY ?? 1f);
    public float ScreenWidth => Rect.Width * (Canvas?.ScaleX ?? 1f);
    public float ScreenHeight => Rect.Height * (Canvas?.ScaleY ?? 1f);

    public override void Render(Renderer renderer, Camera? camera)
    {
        if (!Visible) return;
        Draw(renderer);
    }

    public abstract void Draw(Renderer renderer);
}