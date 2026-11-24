namespace KoreEngine.UI;

public class UIImage : UIElement
{
    public IntPtr Texture;
    public string TexturePath = "";
    public Color color = Color.White;

    public override void Draw(Renderer renderer)
    {
        if (Texture == IntPtr.Zero) return;

        float centerX = ScreenX + ScreenWidth / 2f;
        float centerY = ScreenY + ScreenHeight / 2f;

        renderer.DrawTexture(Texture, centerX, centerY, ScreenWidth, ScreenHeight, color);
    }

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new TextureField
        {
            Label = "Texture",
            Get = () => (Texture, TexturePath),
            Set = (tex, path) => { Texture = tex; TexturePath = path; }
        };

        yield return new ColorField
        {
            Label = "Color",
            Get = () => color,
            Set = v => color = v
        };
    }
}