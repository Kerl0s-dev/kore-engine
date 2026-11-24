namespace KoreEngine;

/// <summary>
/// Chemins d'assets portables : "Assets/Sons/clip.wav" plutôt que "C:\Users\...\clip.wav".
/// Résolus à l'exécution par TextureCache.Resolve (éditeur : racine du projet, jeu buildé : à côté de l'exe).
/// </summary>
public static class AssetPaths
{
    static readonly string[] ContentFolders = { "Assets", "Packages" };

    /// <summary>
    /// Chemin absolu sous Assets/ ou Packages/ -> "Assets/Sub/x.png" (séparateurs '/').
    /// Un chemin déjà relatif ou situé hors du projet est renvoyé tel quel (séparateurs normalisés).
    /// </summary>
    public static string ToProjectPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string norm = path.Replace('\\', '/');
        if (!Path.IsPathRooted(path)) return norm;

        string root = (Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(SceneManager.AssetsDirectory)) ?? "").Replace('\\', '/');

        foreach (var folder in ContentFolders)
        {
            string prefix = $"{root}/{folder}/";
            if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return $"{folder}/{norm[prefix.Length..]}";
        }

        return norm;
    }
}