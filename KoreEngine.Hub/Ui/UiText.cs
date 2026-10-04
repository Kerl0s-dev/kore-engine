namespace KoreEngine.Hub.Ui;

public static class UiText
{
    /// <summary>
    /// Tronque un texte à la largeur donnée avec « … ». S'appuie sur la police
    /// MONOSPACE du Hub (RobotoMono) : toutes les lettres ont la même largeur, donc
    /// pas besoin de mesurer chaque candidat (ce qui remplirait le cache de textes).
    /// </summary>
    public static string Fit(UiContext ui, string text, float size, float maxWidth, bool keepTail)
    {
        float charWidth = ui.Text.Measure("M", size).Width;
        if (charWidth <= 0) return text;

        int maxChars = Math.Max(1, (int)(maxWidth / charWidth));
        if (text.Length <= maxChars) return text;

        return keepTail
            ? "…" + text[^(maxChars - 1)..]
            : text[..(maxChars - 1)] + "…";
    }
}