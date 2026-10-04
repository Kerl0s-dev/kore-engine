using KoreEngine.Hub.Models;

namespace KoreEngine.Hub.Ui;

/// <summary>
/// Liste verticale de projets, une ligne par projet, avec défilement à la molette,
/// sélection au clic et ouverture au double-clic.
/// </summary>
public sealed class ProjectList : Widget
{
    const float RowHeight = 64;
    const float RowGap = 4;
    const float ScrollStep = 56;
    const long DoubleClickMs = 400;

    // Colonnes de droite (largeurs fixes, texte aligné à droite).
    const float DateColumn = 110;
    const float SizeColumn = 90;
    const float VersionColumn = 90;

    static readonly Rgba RowNormal = new(44, 44, 44);
    static readonly Rgba RowHover = new(58, 58, 58);
    static readonly Rgba RowSelected = new(34, 66, 120);
    static readonly Rgba Missing = new(230, 90, 90);

    public List<ProjectListItem> Items = new();
    public int SelectedIndex { get; private set; } = -1;

    public ProjectListItem? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public event Action<ProjectListItem?>? SelectionChanged;
    public event Action<ProjectListItem>? Open; // double-clic

    float scroll;
    int hoverIndex = -1;
    int lastClickIndex = -1;
    long lastClickTime;

    float ContentHeight => Items.Count * (RowHeight + RowGap);
    float MaxScroll => Math.Max(0, ContentHeight - Height);

    public override void Update(UiContext ui)
    {
        var input = ui.Input;
        bool inside = Contains(input.MouseX, input.MouseY);

        // Molette
        if (inside && input.WheelY != 0)
            scroll -= input.WheelY * ScrollStep;
        scroll = Math.Clamp(scroll, 0, MaxScroll);

        // Survol
        hoverIndex = inside ? IndexAt(input.MouseY) : -1;

        // Clic / double-clic
        if (inside && input.Pressed && hoverIndex >= 0)
        {
            long now = Environment.TickCount64;
            bool isDouble = hoverIndex == lastClickIndex && now - lastClickTime <= DoubleClickMs;

            Select(hoverIndex);

            if (isDouble)
            {
                lastClickIndex = -1;
                Open?.Invoke(Items[hoverIndex]);
            }
            else
            {
                lastClickIndex = hoverIndex;
                lastClickTime = now;
            }
        }
    }

    int IndexAt(float mouseY)
    {
        float y = mouseY - Y + scroll;
        if (y < 0) return -1;

        int index = (int)(y / (RowHeight + RowGap));
        bool onRow = y - index * (RowHeight + RowGap) < RowHeight;
        return onRow && index < Items.Count ? index : -1;
    }

    public void Select(int index)
    {
        if (index == SelectedIndex) return;
        SelectedIndex = index;
        SelectionChanged?.Invoke(Selected);
    }

    public override void Draw(UiContext ui)
    {
        ui.PushClip(X, Y, Width, Height);

        float rowWidth = Width - 12; // laisse de la place à la barre de défilement

        for (int i = 0; i < Items.Count; i++)
        {
            float y = Y - scroll + i * (RowHeight + RowGap);
            if (y + RowHeight < Y || y > Y + Height) continue; // hors de la zone visible

            DrawRow(ui, Items[i], X, y, rowWidth, i);
        }

        DrawScrollbar(ui);
        ui.PopClip();
    }

    void DrawRow(UiContext ui, ProjectListItem item, float x, float y, float w, int index)
    {
        Rgba bg = index == SelectedIndex ? RowSelected
                : index == hoverIndex ? RowHover
                : RowNormal;

        ui.FillRect(x, y, w, RowHeight, bg);
        if (index == SelectedIndex)
            ui.OutlineRect(x, y, w, RowHeight, Rgba.Accent);

        // Vignette : carré coloré avec l'initiale (en attendant de vraies icônes).
        var iconColor = item.Exists ? Rgba.Accent : new Rgba(90, 90, 90);
        ui.FillRect(x + 12, y + 12, 40, 40, iconColor);
        string initial = item.Name.Length > 0 ? char.ToUpperInvariant(item.Name[0]).ToString() : "?";
        var (iw, ih) = ui.Text.Measure(initial, 22);
        ui.Text.Draw(initial, 22, x + 12 + (40 - iw) / 2f, y + 12 + (40 - ih) / 2f);

        // Colonnes de droite
        float right = x + w - 16;
        float textAreaRight = right - (DateColumn + SizeColumn + VersionColumn) - 16;
        float textLeft = x + 64;
        float textWidth = Math.Max(40, textAreaRight - textLeft);

        // Nom + chemin
        var nameColor = item.Exists ? Rgba.Text : Rgba.TextDim;
        ui.Text.Draw(UiText.Fit(ui, item.Name, 18, textWidth, keepTail: false), 18, textLeft, y + 10,
            nameColor.R, nameColor.G, nameColor.B);

        if (item.Exists)
            ui.Text.Draw(UiText.Fit(ui, item.Path, 12, textWidth, keepTail: true), 12, textLeft, y + 38,
                Rgba.TextDim.R, Rgba.TextDim.G, Rgba.TextDim.B);
        else
            ui.Text.Draw("Introuvable sur le disque", 12, textLeft, y + 38, Missing.R, Missing.G, Missing.B);

        // Date | Taille | Version du moteur
        DrawRightAligned(ui, item.LastOpened.ToString("dd/MM/yyyy"), 14, right, y);
        DrawRightAligned(ui, item.Exists ? item.Size : "—", 14, right - DateColumn, y);
        DrawRightAligned(ui, item.Exists ? item.EditorVersion : "—", 14, right - DateColumn - SizeColumn, y);
    }

    static void DrawRightAligned(UiContext ui, string text, float size, float rightEdge, float rowY)
    {
        var (tw, th) = ui.Text.Measure(text, size);
        ui.Text.Draw(text, size, rightEdge - tw, rowY + (RowHeight - th) / 2f,
            Rgba.TextDim.R, Rgba.TextDim.G, Rgba.TextDim.B);
    }

    void DrawScrollbar(UiContext ui)
    {
        if (MaxScroll <= 0) return;

        float trackX = X + Width - 8;
        float thumbHeight = Math.Max(24, Height * (Height / ContentHeight));
        float thumbY = Y + (Height - thumbHeight) * (scroll / MaxScroll);

        ui.FillRect(trackX, Y, 6, Height, new Rgba(40, 40, 40));
        ui.FillRect(trackX, thumbY, 6, thumbHeight, new Rgba(110, 110, 110));
    }
}