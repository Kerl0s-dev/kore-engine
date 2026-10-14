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
        { ".png", ".bmp", ".jpg", ".jpeg" };

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
            cache[path] = tex;
            Console.WriteLine($"Loaded texture \'{tex} ({path})\'");
            return tex;
        }
        catch
        {
            Console.WriteLine($"No texture at path \'{path}\'");
            cache[path] = IntPtr.Zero;
            return IntPtr.Zero;
        }
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

    static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string norm = path.Replace('\\', '/');
        int i = norm.LastIndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
        string? rel = i >= 0 ? norm[(i + "/Assets/".Length)..]
                    : Path.IsPathRooted(path) ? null : norm;

        if (rel != null)
        {
            string candidate = Path.Combine(SceneManager.AssetsDirectory, rel);
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