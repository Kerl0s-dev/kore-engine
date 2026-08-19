namespace KoreEngine.Engine;

/// <summary>
/// Réglages globaux du projet, volontairement minimalistes (même esprit texte
/// simple que le format .kscene) : pour l'instant juste la scène de démarrage.
///
/// Nécessaire parce qu'un jeu buildé (GameLoop) n'a personne pour choisir
/// une scène à la main comme dans l'éditeur — il faut bien qu'il sache
/// laquelle charger tout seul au lancement.
/// </summary>
public static class ProjectSettings
{
    public static string? StartupScene { get; set; }

    static string FilePath => Path.Combine(SceneManager.AssetsDirectory, "ProjectSettings.txt");

    public static void Load()
    {
        StartupScene = null;
        if (!File.Exists(FilePath)) return;

        foreach (var line in File.ReadAllLines(FilePath))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("StartupScene:"))
                StartupScene = trimmed["StartupScene:".Length..].Trim();
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(SceneManager.AssetsDirectory);
        File.WriteAllText(FilePath, $"StartupScene: {StartupScene}{Environment.NewLine}");
    }
}
