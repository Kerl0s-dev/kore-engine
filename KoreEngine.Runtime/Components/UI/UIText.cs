namespace KoreEngine.UI;

public class UIText : UIElement
{
    public string Text = "Text";

    [AssetPath(".ttf", ".otf")]
    public string FontPath = "";

    [Range(1, 256)]
    public int Size = 24;

    public Color Color = new Color(255, 255, 255);

    // Police résolue mémorisée : TextureCache.Resolve() touche le disque, on ne le refait
    // que si le chemin ou la taille changent (privé → ni sérialisé, ni affiché).
    IntPtr font;
    (string, int) fontKey;
    bool hasFont;

    public override void Draw(Renderer renderer)
    {
        if (string.IsNullOrEmpty(Text) || string.IsNullOrEmpty(FontPath)) return;

        IntPtr f = GetFont();
        var color = Color ?? new Color(255, 255, 255, 255);

        // Échelle uniforme avec le Canvas : le texte suit la résolution
        float scale = Canvas != null ? MathF.Min(Canvas.ScaleX, Canvas.ScaleY) : 1f;

        FontManager.DrawText(f, Text, ScreenX, ScreenY,
            (byte)color.R, (byte)color.G, (byte)color.B, (byte)color.A, scale);
    }

    IntPtr GetFont()
    {
        var key = (FontPath, Math.Max(1, Size));
        if (hasFont && key == fontKey) return font;

        fontKey = key;
        hasFont = true;
        font = FontManager.GetFont(TextureCache.Resolve(FontPath), key.Item2);
        return font;
    }
}