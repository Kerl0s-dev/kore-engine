using KoreEngine.Engine;

namespace KoreEngine.Components.UI;

public class UIImage : UIElement
{
    public IntPtr Texture;
    public string TexturePath = "";

    public override void Draw(Renderer renderer)
    {
        if (Texture == IntPtr.Zero) return;
        renderer.DrawTexture(Texture, (int)ScreenX, (int)ScreenY, (int)ScreenWidth, (int)ScreenHeight);
    }
}