using SDL3;

namespace KoreEngine;

/// <summary>
/// Cache global de textures SDL indexées par chemin de fichier.
/// Utilisé par le texture picker de l'inspector pour charger les
/// miniatures une seule fois et les réutiliser sans recharge.
/// </summary>
public static class TextureCache
{
    static Renderer? renderer;
    static readonly Dictionary<string, IntPtr> cache = new();

    public static readonly string[] SupportedExtensions =
        { ".png", ".bmp" };

    public static void Init(Renderer r) => renderer = r;

    /// <summary>
    /// Retourne la texture SDL pour ce chemin, en la chargeant si besoin.
    /// Retourne IntPtr.Zero si le chargement échoue.
    /// </summary>
    public static IntPtr Get(string path)
    {
        if (cache.TryGetValue(path, out var tex)) return tex;

        if (renderer == null)
        {
            Logger.Error($"[TextureCache] Init() n'a pas été appelé avant de charger '{path}'.");
            return IntPtr.Zero;
        }

        try
        {
            tex = renderer.LoadTexture(Resolve(path));
            SDL.SetTextureScaleMode(tex, ToSdl(GetFilter(path)));
            cache[path] = tex;
            return tex;
        }
        catch
        {
            Console.WriteLine($"No texture at path \'{path}\'");
            cache[path] = IntPtr.Zero;
            return IntPtr.Zero;
        }
    }

    // Filtre par fichier physique (clé = chemin résolu). Nécessaire car le cache est indexé par le chemin
    // *tel que demandé* : "Assets/a.png" (scène) et "C:/.../Assets/a.png" (picker, Project Panel)
    // donnent deux textures SDL différentes pour le même fichier.
    static readonly Dictionary<string, TextureFilterMode> filterByFile = new(StringComparer.OrdinalIgnoreCase);

    static string Normalize(string path) => Path.GetFullPath(Resolve(path)).Replace('\\', '/');

    static SDL.ScaleMode ToSdl(TextureFilterMode m) =>
        m == TextureFilterMode.Nearest ? SDL.ScaleMode.Nearest : SDL.ScaleMode.Linear;

    public static TextureFilterMode GetFilter(string path)
    {
        string key = Normalize(path);
        if (!filterByFile.TryGetValue(key, out var mode))
        {
            mode = TextureImportSettings.Load(path).FilterMode;
            filterByFile[key] = mode;
        }
        return mode;
    }

    /// Applique le filtre à TOUTES les textures en cache qui pointent vers ce fichier.
    public static void SetFilter(string path, TextureFilterMode mode)
    {
        string key = Normalize(path);
        filterByFile[key] = mode;

        foreach (var (k, tex) in cache)
            if (tex != IntPtr.Zero && Normalize(k) == key)
                SDL.SetTextureScaleMode(tex, ToSdl(mode));
    }

    /// <summary>
    /// Liste tous les fichiers image sous le dossier Assets/ (récursif).
    /// </summary>
    public static IEnumerable<string> ScanAssets(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();

        Console.WriteLine($"Scanned {root} folder...");

        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => SupportedExtensions.Contains(
                Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f);
    }

    static readonly string[] ContentFolders = { "Assets", "Packages" };

    internal static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string norm = path.Replace('\\', '/');

        // Assets/ et Packages/ sont voisins : dans l'éditeur, à la racine du projet ;
        // dans le jeu buildé, à côté de l'exécutable.
        string root = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(SceneManager.AssetsDirectory)) ?? "";

        foreach (var folder in ContentFolders)
        {
            string marker = $"/{folder}/";
            int i = norm.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;

            string candidate = Path.Combine(root, folder, norm[(i + marker.Length)..]);
            if (File.Exists(candidate)) return candidate;
        }

        // Chemin relatif sans marqueur : on le cherche sous Assets
        if (!Path.IsPathRooted(path))
        {
            string candidate = Path.Combine(SceneManager.AssetsDirectory, norm);
            if (File.Exists(candidate)) return candidate;
        }

        return path;   // repli : chemin d'origine
    }

    public static void Dispose()
    {
        // Les textures SDL sont détruites avec le renderer —
        // on vide juste le dictionnaire ici.
        cache.Clear();
    }
}