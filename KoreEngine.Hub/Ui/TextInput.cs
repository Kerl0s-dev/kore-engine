namespace KoreEngine.Hub.Ui;

/// <summary>
/// Champ de texte une ligne : curseur, Retour arrière/Suppr, flèches, Début/Fin,
/// Ctrl+A (tout sélectionner), Ctrl+V (coller). Pas de sélection partielle.
/// Suppose une police MONOSPACE (RobotoMono) pour placer le curseur au clic.
/// </summary>
public sealed class TextInput : Widget
{
    const float Padding = 10;

    string text = "";
    int caret;
    bool selectAll;
    float scrollOffset;

    public string Placeholder = "";
    public int MaxLength = 256;
    public float FontSize = 16;

    public event Action? Changed;

    public string Text
    {
        get => text;
        set
        {
            text = value ?? "";
            caret = text.Length;
            selectAll = false;
        }
    }

    public TextInput(float width, float height = 36)
    {
        Width = width;
        Height = height;
    }

    public override void Update(UiContext ui)
    {
        var input = ui.Input;

        if (input.Pressed)
        {
            if (Enabled && Contains(input.MouseX, input.MouseY))
            {
                ui.Focus = this;
                selectAll = false;
                float charWidth = ui.Text.Measure("M", FontSize).Width;
                if (charWidth > 0)
                    caret = Math.Clamp((int)MathF.Round((input.MouseX - (X + Padding) + scrollOffset) / charWidth), 0, text.Length);
            }
            else if (ReferenceEquals(ui.Focus, this))
            {
                ui.Focus = null;
            }
        }

        if (!Enabled || !ReferenceEquals(ui.Focus, this)) return;

        string before = text;

        // Texte tapé (les caractères de contrôle sont ignorés)
        if (input.TypedText.Length > 0)
        {
            var clean = new string(input.TypedText.Where(c => !char.IsControl(c)).ToArray());
            Insert(clean);
        }

        foreach (var key in input.Keys)
        {
            switch (key.Key)
            {
                case UiKeys.Backspace:
                    if (selectAll) Clear();
                    else if (caret > 0) { text = text.Remove(caret - 1, 1); caret--; }
                    break;

                case UiKeys.Delete:
                    if (selectAll) Clear();
                    else if (caret < text.Length) text = text.Remove(caret, 1);
                    break;

                case UiKeys.Left: selectAll = false; caret = Math.Max(0, caret - 1); break;
                case UiKeys.Right: selectAll = false; caret = Math.Min(text.Length, caret + 1); break;
                case UiKeys.Home: selectAll = false; caret = 0; break;
                case UiKeys.End: selectAll = false; caret = text.Length; break;

                case UiKeys.A when key.Ctrl:
                    selectAll = text.Length > 0;
                    break;

                case UiKeys.V when key.Ctrl:
                    Insert(ReadClipboard());
                    break;
            }
        }

        if (text != before) Changed?.Invoke();
    }

    void Clear()
    {
        text = "";
        caret = 0;
        selectAll = false;
    }

    void Insert(string value)
    {
        if (value.Length == 0) return;
        if (selectAll) Clear();

        int room = MaxLength - text.Length;
        if (room <= 0) return;
        if (value.Length > room) value = value[..room];

        text = text.Insert(caret, value);
        caret += value.Length;
    }

    static string ReadClipboard()
    {
        try
        {
            string clip = SDL3.SDL.GetClipboardText() ?? "";
            return clip.Replace("\r", "").Replace("\n", " ").Trim();
        }
        catch
        {
            return "";
        }
    }

    public override void Draw(UiContext ui)
    {
        bool focused = ReferenceEquals(ui.Focus, this);

        ui.FillRect(X, Y, Width, Height, new Rgba(26, 26, 26));
        ui.OutlineRect(X, Y, Width, Height, focused ? Rgba.Accent : Rgba.SecondaryBorder);

        var (charWidth, lineHeight) = ui.Text.Measure("M", FontSize);
        if (charWidth <= 0) return;

        // Défilement horizontal pour garder le curseur visible.
        float available = Width - Padding * 2;
        float caretPx = caret * charWidth;
        scrollOffset = Math.Max(0, caretPx - available + 2);

        float textX = X + Padding - scrollOffset;
        float textY = Y + (Height - lineHeight) / 2f;

        ui.PushClip(X + 1, Y + 1, Width - 2, Height - 2);

        if (text.Length == 0)
        {
            if (Placeholder.Length > 0)
                ui.Text.Draw(Placeholder, FontSize, X + Padding, textY, Rgba.TextDim.R, Rgba.TextDim.G, Rgba.TextDim.B);
        }
        else
        {
            if (selectAll)
                ui.FillRect(textX, textY, text.Length * charWidth, lineHeight, new Rgba(52, 120, 246, 110));

            var color = Enabled ? Rgba.Text : Rgba.TextDim;
            ui.Text.Draw(text, FontSize, textX, textY, color.R, color.G, color.B);
        }

        // Curseur clignotant
        if (focused && Enabled && Environment.TickCount64 / 500 % 2 == 0)
            ui.FillRect(textX + caretPx, textY, 2, lineHeight, Rgba.Text);

        ui.PopClip();
    }
}