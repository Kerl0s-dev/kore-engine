using KoreEngine.Core;

namespace KoreEngine.Engine;

public static class SceneManager
{
    public static int ViewportWidth { get; set; }
    public static int ViewportHeight { get; set; }
    public static float ViewportMouseX { get; set; }
    public static float ViewportMouseY { get; set; }
    public static bool ViewportMouseInBounds { get; set; }

    // registre : nom de scène -> chemin absolu du fichier .kscene
    static Dictionary<string, string> registry = new();
    static Scene? current;
    static Scene? next;
    static string? currentSceneName;
    static string? currentScenePath; // chemin réel du fichier chargé, pour un save à la bonne place

    /// <summary>
    /// Déclenché juste avant qu'une scène soit chargée ou déchargée. Permet
    /// à l'Editor de réagir (ex: vider la sélection courante) sans que
    /// SceneManager ait besoin de connaître l'existence d'EditorSelection.
    /// </summary>
    public static event Action? OnSceneChanging;

    /// <summary>
    /// Racine du dossier Assets. Par défaut, à côté de l'exécutable — c'est
    /// ce qui convient à un jeu buildé. L'Editor écrase cette valeur au
    /// démarrage avec le vrai chemin du projet (voir EditorWindow).
    /// </summary>
    public static string AssetsDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Assets");

    public static Scene? Current => current;
    public static string? CurrentSceneName => currentSceneName;
    public static string? CurrentScenePath => currentScenePath;

    public static void ScanAndRegisterScenes()
    {
        registry.Clear();

        if (!Directory.Exists(AssetsDirectory)) return;

        foreach (var file in Directory
            .GetFiles(AssetsDirectory, "*.kscene", SearchOption.AllDirectories)
            .OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(file);

            if (registry.ContainsKey(name))
            {
                Logger.Warning(
                    $"[SceneManager] Deux scènes portent le nom '{name}' " +
                    $"— '{registry[name]}' sera écrasée par '{file}' dans le registre. " +
                    "Renomme l'une des deux pour éviter toute ambiguïté.");
            }

            registry[name] = file;
        }
    }

    public static void LoadScene(string name)
    {
        if (!registry.TryGetValue(name, out var path))
            throw new Exception($"Scene '{name}' not registered.");

        LoadSceneFromFile(path);
    }

    public static void LoadSceneFromFile(string path)
    {
        // Sauvegarde la scène courante avant de charger la nouvelle, si elle existe
        if (current != null)
            SaveCurrentScene();

        next = SceneSerializer.LoadScene(path);
        currentSceneName = Path.GetFileNameWithoutExtension(path);
        currentScenePath = path;
        OnSceneChanging?.Invoke();
    }

    public static void SaveCurrentScene()
    {
        if (current == null) return;

        // Sauvegarde à l'emplacement d'origine si connu (scène chargée
        // depuis un sous-dossier), sinon fallback à la racine Assets
        // pour une toute nouvelle scène jamais sauvegardée.
        string path = currentScenePath
            ?? Path.Combine(AssetsDirectory, $"{currentSceneName ?? current.Name}.kscene");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SceneSerializer.SaveScene(current, path);
        currentScenePath = path;
    }

    /// <summary>
    /// À appeler après tout renommage de fichier/dossier dans Assets/, pour que
    /// la scène chargée (ou son dossier parent) suive si elle est concernée.
    /// </summary>
    public static void NotifyPathRenamed(string oldPath, string newPath)
    {
        if (currentScenePath == null) return;

        if (string.Equals(currentScenePath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            currentScenePath = newPath;
            currentSceneName = Path.GetFileNameWithoutExtension(newPath);
        }
        else if (currentScenePath.StartsWith(oldPath + Path.DirectorySeparatorChar,
                     StringComparison.OrdinalIgnoreCase))
        {
            // Le dossier renommé est un parent de la scène courante
            currentScenePath = newPath + currentScenePath.Substring(oldPath.Length);
            currentSceneName = Path.GetFileNameWithoutExtension(currentScenePath);
        }
    }

    public static IEnumerable<string> SceneFiles()
    {
        if (!Directory.Exists(AssetsDirectory)) return Enumerable.Empty<string>();
        return Directory
            .GetFiles(AssetsDirectory, "*.kscene", SearchOption.AllDirectories)
            .OrderBy(f => f);
    }

    public static void ApplyPendingScene()
    {
        if (next == null) return;
        current = next;
        next = null;
    }

    public static void Update(float dt)
        => current?.Update(dt);

    public static void NotifyStart()
        => current?.Start();

    public static void UnloadScene()
    {
        current?.DestroyAll();
        current = null;
        currentSceneName = null;
        currentScenePath = null;
        OnSceneChanging?.Invoke();
    }

    public static void Render(Renderer renderer)
        => current?.Render(renderer);
}