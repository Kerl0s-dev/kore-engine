namespace KoreEngine.Hub.Ui;

/// <summary>
/// Petite fenêtre de chargement (façon Unity) affichée pendant l'ouverture d'un projet :
/// nom du projet, étape en cours, barre de progression indéterminée (dotnet ne donne
/// pas de pourcentage), temps écoulé et derniers messages du build.
/// Alimentée par HubWindow via AddLog / SetStage / Succeed / Fail (thread de rendu).
/// </summary>
public sealed class LoadingDialog : Widget
{
    enum State { Running, Succeeded, Failed }

    const float PanelWidth = 560;
    const float PanelHeight = 300;
    const float Margin = 24;
    const int MaxStoredLines = 200;
    const int DisplayedLines = 5;
    const float LogFontSize = 11;
    const long AutoCloseDelayMs = 1500;
    const long BarPeriodMs = 1400;

    static readonly Rgba Green = new(80, 200, 120);
    static readonly Rgba Red = new(230, 90, 90);

    readonly Button closeButton = new("Fermer", 130, 36, Button.Style.Secondary, 14);
    readonly List<string> lines = new();

    State state;
    string projectName = "";
    string stage = "";
    long startTicks;
    long closeAtTicks;

    public LoadingDialog()
    {
        Visible = false;
        closeButton.Click += Close;
    }

    public void Show(string name)
    {
        projectName = name;
        stage = "Préparation...";
        lines.Clear();
        state = State.Running;
        startTicks = Environment.TickCount64;
        closeAtTicks = 0;
        Visible = true;
    }

    public void Close() => Visible = false;

    public void SetStage(string text) => stage = text;

    public void AddLog(string line)
    {
        line = line.Replace('\t', ' ').Trim();
        if (line.Length == 0) return;

        lines.Add(line);
        if (lines.Count > MaxStoredLines)
            lines.RemoveRange(0, lines.Count - MaxStoredLines);
    }

    public void Succeed()
    {
        state = State.Succeeded;
        stage = "Éditeur lancé.";
        closeAtTicks = Environment.TickCount64 + AutoCloseDelayMs;
    }

    public void Fail()
    {
        state = State.Failed;
        stage = "L'ouverture a échoué. Derniers messages :";
    }

    void Layout(UiContext ui)
    {
        X = (ui.WindowWidth - PanelWidth) / 2f;
        Y = (ui.WindowHeight - PanelHeight) / 2f;
        Width = PanelWidth;
        Height = PanelHeight;

        closeButton.X = X + PanelWidth - Margin - closeButton.Width;
        closeButton.Y = Y + PanelHeight - Margin - closeButton.Height;
    }

    public override void Update(UiContext ui)
    {
        Layout(ui);

        if (state == State.Succeeded && Environment.TickCount64 >= closeAtTicks)
        {
            Close();
            return;
        }

        // Pendant le chargement, rien ne peut être annulé (dotnet n'est pas interruptible
        // ici) : seule l'erreur donne accès au bouton Fermer.
        if (state != State.Failed) return;

        closeButton.Update(ui);
        foreach (var key in ui.Input.Keys)
        {
            if (key.Key == UiKeys.Escape || key.Key == UiKeys.Enter)
            {
                Close();
                return;
            }
        }
    }

    public override void Draw(UiContext ui)
    {
        Layout(ui);

        ui.FillRect(0, 0, ui.WindowWidth, ui.WindowHeight, new Rgba(0, 0, 0, 170));
        ui.FillRect(X, Y, Width, Height, new Rgba(38, 38, 38));
        ui.OutlineRect(X, Y, Width, Height, Rgba.SecondaryBorder);

        float inner = PanelWidth - Margin * 2;
        float left = X + Margin;

        string title = state == State.Failed ? "Échec de l'ouverture" : "Ouverture du projet";
        ui.Text.Draw(title, 20, left, Y + 22);

        ui.Text.Draw(UiText.Fit(ui, projectName, 16, inner, keepTail: false), 16, left, Y + 54,
            Rgba.Accent.R, Rgba.Accent.G, Rgba.Accent.B);

        // Étape en cours + temps écoulé
        long elapsedSec = (Environment.TickCount64 - startTicks) / 1000;
        string elapsed = $"{elapsedSec} s";
        var (ew, _) = ui.Text.Measure(elapsed, 14);
        ui.Text.Draw(elapsed, 14, left + inner - ew, Y + 94, Rgba.TextDim.R, Rgba.TextDim.G, Rgba.TextDim.B);
        ui.Text.Draw(UiText.Fit(ui, stage, 14, inner - ew - 16, keepTail: false), 14, left, Y + 94);

        DrawBar(ui, left, Y + 122, inner);
        DrawLogs(ui, left, Y + 146, inner);

        if (state == State.Failed)
            closeButton.Draw(ui);
    }

    void DrawBar(UiContext ui, float x, float y, float width)
    {
        const float height = 6;
        ui.FillRect(x, y, width, height, new Rgba(26, 26, 26));

        switch (state)
        {
            case State.Succeeded:
                ui.FillRect(x, y, width, height, Green);
                break;

            case State.Failed:
                ui.FillRect(x, y, width, height, Red);
                break;

            default:
                // Segment qui fait des allers-retours : progression « indéterminée ».
                float segment = width * 0.3f;
                float t = (Environment.TickCount64 % BarPeriodMs) / (float)BarPeriodMs;
                float phase = t < 0.5f ? t * 2f : (1f - t) * 2f;
                ui.FillRect(x + phase * (width - segment), y, segment, height, Rgba.Accent);
                break;
        }
    }

    void DrawLogs(UiContext ui, float x, float y, float width)
    {
        const float lineHeight = 17;

        var shown = GetDisplayedLines(ui, width);
        var color = state == State.Failed ? Red : Rgba.TextDim;

        for (int i = 0; i < shown.Count; i++)
            ui.Text.Draw(shown[i], LogFontSize, x, y + i * lineHeight, color.R, color.G, color.B);
    }

    /// <summary>
    /// En cours : les dernières lignes. En échec :
    ///  - si l'éditeur a planté au démarrage : le DÉBUT de sa sortie (la 1re ligne d'une exception
    ///    nomme le fichier manquant), coupé sur plusieurs lignes pour tout lire ;
    ///  - sinon les dernières erreurs de build (sans doublons, sans « 0 Error(s) »).
    /// </summary>
    List<string> GetDisplayedLines(UiContext ui, float width)
    {
        float charWidth = ui.Text.Measure("M", LogFontSize).Width; // police monospace
        int maxChars = charWidth > 0 ? Math.Max(10, (int)(width / charWidth)) : 80;

        if (state == State.Failed)
        {
            var editorLines = lines
                .Where(l => l.StartsWith("[Editor]", StringComparison.Ordinal))
                .Select(l => l["[Editor]".Length..].Trim())
                .Distinct()
                .ToList();

            if (editorLines.Count > 0)
                return Wrap(editorLines, maxChars);
        }

        IEnumerable<string> source = lines;
        if (state == State.Failed)
        {
            var errors = lines
                .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                         && !l.Contains("0 Error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (errors.Count > 0) source = errors;
        }

        var result = new List<string>();
        foreach (var line in source.Reverse())
        {
            if (result.Contains(line)) continue;
            result.Add(line);
            if (result.Count == DisplayedLines) break;
        }

        result.Reverse();
        return result.Select(l => UiText.Fit(ui, l, LogFontSize, width, keepTail: false)).ToList();
    }

    static List<string> Wrap(List<string> input, int maxChars)
    {
        var result = new List<string>();
        foreach (var line in input)
        {
            for (int i = 0; i < line.Length && result.Count < DisplayedLines; i += maxChars)
                result.Add(line.Substring(i, Math.Min(maxChars, line.Length - i)));

            if (result.Count >= DisplayedLines) break;
        }
        return result;
    }
}