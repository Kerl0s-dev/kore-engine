namespace KoreEngine;

/// <summary>
/// Asset ".kmat" : un shader + (à venir) les valeurs de ses paramètres.
/// Même format "Clé: Valeur" que les scènes et les .kmeta, par exemple :
///   Shader: Grayscale.hlsl      (cherché n'importe où sous Assets/)
/// </summary>
public sealed class Material
{
    public string AssetPath { get; }
    public string ShaderPath { get; private set; } = "";

    /// <summary>Valeurs par défaut écrites dans le .kmat ("NomDuParamètre: valeurs"), avant lecture du shader.</summary>
    readonly Dictionary<string, string> rawDefaults = new();

    /// <summary>Shader compilé, ou null si le chemin est vide, le shader en erreur ou le driver non "gpu".</summary>
    public Shader? Shader => ShaderCache.Get(ShaderPath);

    Material(string assetPath) => AssetPath = assetPath;

    /// <summary>Paramètres du shader (vide si le shader est absent ou en erreur).</summary>
    public IReadOnlyList<ShaderParam> Params => Shader?.Params ?? Array.Empty<ShaderParam>();

    /// <summary>Valeur par défaut d'un paramètre : celle du .kmat si elle y est écrite, sinon celle du shader (copie).</summary>
    public float[] GetDefault(ShaderParam param)
    {
        var value = (float[])param.Default.Clone();
        if (rawDefaults.TryGetValue(param.Name, out var text))
            ShaderParams.FillValue(value, text, param.Components);
        return value;
    }

    public static Material? Load(string assetPath)
    {
        string file = TextureCache.Resolve(assetPath);
        if (!File.Exists(file))
        {
            Logger.Error($"[Material] Fichier introuvable : {assetPath}");
            return null;
        }

        var mat = new Material(assetPath);
        foreach (var raw in File.ReadAllLines(file))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0) continue;

            string key = raw[..colon].Trim();
            string value = raw[(colon + 1)..].Trim();

            if (key == "Shader") mat.ShaderPath = FindShader(value) ?? value;
            else mat.rawDefaults[key] = value;
        }
        return mat;
    }

    /// <summary>
    /// Retrouve le .hlsl : d'abord au chemin donné ("Assets/Shaders/x.hlsl" ou "x.hlsl"), sinon par
    /// son nom de fichier n'importe où sous Assets/ puis Packages/. Le .hlsl peut donc être rangé
    /// dans le dossier de son choix sans modifier le .kmat.
    /// </summary>
    static string? FindShader(string shaderPath)
    {
        if (string.IsNullOrEmpty(shaderPath)) return null;
        if (File.Exists(TextureCache.Resolve(shaderPath))) return shaderPath;

        string name = Path.GetFileName(shaderPath.Replace('\\', '/'));
        string root = Path.GetDirectoryName(
            Path.TrimEndingDirectorySeparator(SceneManager.AssetsDirectory)) ?? "";

        foreach (var folder in new[] { "Assets", "Packages" })
        {
            string dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;

            var matches = Directory.GetFiles(dir, name, SearchOption.AllDirectories);
            if (matches.Length == 0) continue;

            if (matches.Length > 1)
                Logger.Warning($"[Material] Plusieurs fichiers '{name}' sous {folder}/ : '{AssetPaths.ToProjectPath(matches[0])}' utilisé.");
            return AssetPaths.ToProjectPath(matches[0]);
        }
        return null;
    }
}

/// <summary>Cache de matériaux indexé par chemin d'asset (".kmat").</summary>
public static class MaterialCache
{
    static readonly Dictionary<string, Material?> cache = new();

    public static Material? Get(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (cache.TryGetValue(path, out var mat)) return mat;

        mat = Material.Load(path);
        cache[path] = mat;
        return mat;
    }

    public static void Clear() => cache.Clear();
}