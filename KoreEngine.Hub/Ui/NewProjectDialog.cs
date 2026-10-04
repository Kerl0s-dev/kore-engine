using System.Text.RegularExpressions;

namespace KoreEngine.Hub.Ui;

/// <summary>
/// Fenêtre modale « Nouveau projet » : nom + dossier parent. Ne crée rien elle-même :
/// elle valide la saisie puis déclenche Submit(nom, dossier) ; HubWindow fait le travail.
/// </summary>
public sealed class NewProjectDialog : Widget
{
    const float PanelWidth = 620;
    const float PanelHeight = 340;
    const float Margin = 24;

    // Le nom sert de namespace / nom d'assembly C# dans les projets générés.
    static readonly Regex ValidName = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    readonly Label titleLabel = new("Nouveau projet", 22);
    readonly Label nameCaption = new("Nom du projet", 14) { Color = Rgba.TextDim };
    readonly Label folderCaption = new("Dossier parent", 14) { Color = Rgba.TextDim };
    readonly TextInput nameInput = new(0) { Placeholder = "MonJeu" };
    readonly TextInput folderInput = new(0) { Placeholder = "Dossier où créer le projet", FontSize = 14 };
    readonly Button browseButton = new("Parcourir...", 130, 36, Button.Style.Secondary, 14);
    readonly Button createButton = new("Créer", 130, 40, Button.Style.Primary);
    readonly Button cancelButton = new("Annuler", 130, 40, Button.Style.Secondary);

    string? error;
    bool busy;
    bool focusNameNextFrame;

    public event Action<string, string>? Submit; // (nom, dossier parent)
    public event Action? Cancel;
    public event Action? Browse;

    public NewProjectDialog()
    {
        Visible = false;

        browseButton.Click += () => { if (!busy) Browse?.Invoke(); };
        createButton.Click += TrySubmit;
        cancelButton.Click += () => { if (!busy) Cancel?.Invoke(); };
        nameInput.Changed += () => error = null;
        folderInput.Changed += () => error = null;
    }

    public void Show(string defaultFolder)
    {
        nameInput.Text = "";
        folderInput.Text = defaultFolder;
        error = null;
        SetBusy(false);
        Visible = true;
        focusNameNextFrame = true;
    }

    public void Close() => Visible = false;
    public void SetFolder(string folder) { folderInput.Text = folder; error = null; }
    public void SetError(string? message) => error = message;

    public void SetBusy(bool value)
    {
        busy = value;
        nameInput.Enabled = folderInput.Enabled = !value;
        browseButton.Enabled = createButton.Enabled = cancelButton.Enabled = !value;
    }

    void TrySubmit()
    {
        if (busy) return;

        string name = nameInput.Text.Trim();
        string folder = folderInput.Text.Trim();

        if (name.Length == 0) { error = "Donne un nom au projet."; return; }
        if (!ValidName.IsMatch(name))
        {
            error = "Nom invalide : lettres, chiffres et _ uniquement, sans commencer par un chiffre.";
            return;
        }
        if (folder.Length == 0) { error = "Choisis un dossier."; return; }

        error = null;
        Submit?.Invoke(name, folder);
    }

    void Layout(UiContext ui)
    {
        float px = (ui.WindowWidth - PanelWidth) / 2f;
        float py = (ui.WindowHeight - PanelHeight) / 2f;
        X = px; Y = py; Width = PanelWidth; Height = PanelHeight;

        float inner = PanelWidth - Margin * 2;

        titleLabel.X = px + Margin; titleLabel.Y = py + 20;

        nameCaption.X = px + Margin; nameCaption.Y = py + 68;
        nameInput.X = px + Margin; nameInput.Y = py + 90; nameInput.Width = inner;

        folderCaption.X = px + Margin; folderCaption.Y = py + 140;
        browseButton.X = px + PanelWidth - Margin - browseButton.Width;
        browseButton.Y = py + 162;
        folderInput.X = px + Margin; folderInput.Y = py + 162;
        folderInput.Width = inner - browseButton.Width - 8;

        createButton.X = px + PanelWidth - Margin - createButton.Width;
        createButton.Y = py + PanelHeight - Margin - createButton.Height;
        cancelButton.X = createButton.X - cancelButton.Width - 12;
        cancelButton.Y = createButton.Y;
    }

    public override void Update(UiContext ui)
    {
        Layout(ui);

        if (focusNameNextFrame)
        {
            ui.Focus = nameInput;
            focusNameNextFrame = false;
        }

        nameInput.Update(ui);
        folderInput.Update(ui);
        browseButton.Update(ui);
        createButton.Update(ui);
        cancelButton.Update(ui);

        if (busy) return;

        foreach (var key in ui.Input.Keys)
        {
            switch (key.Key)
            {
                case UiKeys.Escape:
                    Cancel?.Invoke();
                    return;
                case UiKeys.Enter:
                    TrySubmit();
                    return;
                case UiKeys.Tab:
                    ui.Focus = ReferenceEquals(ui.Focus, nameInput) ? folderInput : nameInput;
                    break;
            }
        }
    }

    public override void Draw(UiContext ui)
    {
        Layout(ui);

        // Fond assombri : l'UI en dessous reste visible mais inactive.
        ui.FillRect(0, 0, ui.WindowWidth, ui.WindowHeight, new Rgba(0, 0, 0, 150));

        ui.FillRect(X, Y, Width, Height, new Rgba(38, 38, 38));
        ui.OutlineRect(X, Y, Width, Height, Rgba.SecondaryBorder);

        titleLabel.Draw(ui);
        nameCaption.Draw(ui);
        nameInput.Draw(ui);
        folderCaption.Draw(ui);
        folderInput.Draw(ui);
        browseButton.Draw(ui);

        // Aperçu du chemin final
        string name = nameInput.Text.Trim();
        string preview = name.Length > 0
            ? Path.Combine(folderInput.Text.Trim(), name)
            : folderInput.Text.Trim();
        float textWidth = Width - Margin * 2;
        ui.Text.Draw(UiText.Fit(ui, "Chemin : " + preview, 12, textWidth, keepTail: true), 12,
            X + Margin, Y + 214, Rgba.TextDim.R, Rgba.TextDim.G, Rgba.TextDim.B);

        // Erreur ou état « création en cours »
        if (busy)
            ui.Text.Draw("Création du projet en cours...", 14, X + Margin, Y + 244, Rgba.Text.R, Rgba.Text.G, Rgba.Text.B);
        else if (error != null)
            ui.Text.Draw(UiText.Fit(ui, error, 14, textWidth, keepTail: false), 14,
                X + Margin, Y + 244, 230, 90, 90);

        cancelButton.Draw(ui);
        createButton.Draw(ui);
    }
}