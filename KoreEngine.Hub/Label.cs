namespace KoreEngine.Hub.Ui;

public sealed class Label : Widget
{
    public string Text = "";
    public float FontSize = 16;
    public Rgba Color = Rgba.Text;

    public Label(string text, float fontSize = 16)
    {
        Text = text;
        FontSize = fontSize;
    }

    public override void Draw(UiContext ui)
    {
        // Width/Height reflètent la taille réelle du texte (utile pour aligner).
        var (w, h) = ui.Text.Measure(Text, FontSize);
        Width = w;
        Height = h;
        ui.Text.Draw(Text, FontSize, X, Y, Color.R, Color.G, Color.B, Color.A);
    }
}