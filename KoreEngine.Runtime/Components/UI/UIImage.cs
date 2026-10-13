using SDL3;

namespace KoreEngine.UI;

public class UIImage : UIElement
{
    public IntPtr Texture;
    public string TexturePath = "";

    public FilteringMode Filtering = FilteringMode.Linear;

    static readonly string[] FilteringNames =
        Enum.GetNames<FilteringMode>();

    static SDL.ScaleMode ToSdlScaleMode(FilteringMode mode) => mode switch
    {
        FilteringMode.Nearest => SDL.ScaleMode.Nearest,
        FilteringMode.PixelArt => SDL.ScaleMode.PixelArt,
        _ => SDL.ScaleMode.Linear
    };

    public override void Draw(Renderer renderer)
    {
        if (Texture == IntPtr.Zero) return;

        SDL.SetTextureScaleMode(Texture, ToSdlScaleMode(Filtering));

        renderer.DrawTexture(Texture, (int)ScreenX, (int)ScreenY, (int)ScreenWidth, (int)ScreenHeight);
    }
}