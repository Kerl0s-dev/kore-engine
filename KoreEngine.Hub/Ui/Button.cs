namespace KoreEngine.Hub.Ui;

public sealed class Button : Widget
{
    public enum Style
    {
        Primary,
        Secondary
    }

    public string Text = "";
    public float FontSize = 16;
    public event Action? Click;
    public Style BtnStyle { get; }

    public bool Hovered { get; private set; }
    bool pressing; // le clic a commencé sur ce bouton

    public bool IsPressed => pressing && Hovered;

    public Button(string text, float width, float height, Style style, float fontSize = 16)
    {
        Text = text;
        Width = width;
        Height = height;
        FontSize = fontSize;
        BtnStyle = style;
    }

    public override void Update(UiContext ui)
    {
        var input = ui.Input;
        Hovered = Visible && Enabled && Contains(input.MouseX, input.MouseY);

        if (Hovered && input.Pressed)
            pressing = true;

        // Un clic = appui ET relâchement sur le bouton (comme un vrai bouton).
        if (input.Released)
        {
            if (pressing && Hovered)
                Click?.Invoke();
            pressing = false;
        }

        if (!input.Down)
            pressing = false;
    }

    public override void Draw(UiContext ui)
    {
        Rgba background = BtnStyle switch
        {
            Style.Primary => !Enabled ? Rgba.PrimaryButtonDisabled
                : IsPressed ? Rgba.PrimaryButtonPressed
                : Hovered ? Rgba.PrimaryButtonHover
                : Rgba.PrimaryButton,
            Style.Secondary => !Enabled ? Rgba.SecondaryButtonDisabled
                : IsPressed ? Rgba.SecondaryButtonPressed
                : Hovered ? Rgba.SecondaryButtonHover
                : Rgba.SecondaryButton,
            _ => throw new ArgumentOutOfRangeException()
        };

        ui.FillRect(X, Y, Width, Height, background);
        ui.OutlineRect(X, Y, Width, Height, Hovered ? Rgba.Accent : BtnStyle == Button.Style.Primary ? Rgba.PrimaryBorder : Rgba.SecondaryBorder);

        var (tw, th) = ui.Text.Measure(Text, FontSize);
        var color = Enabled ? Rgba.Text : Rgba.TextDim;
        ui.Text.Draw(Text, FontSize, X + (Width - tw) / 2f, Y + (Height - th) / 2f,
            color.R, color.G, color.B, color.A);
    }
}