using SDL3;

namespace KoreEngine.Editor;

/// <summary>
/// Cache des icônes de l'éditeur chargées depuis Assets/Editor/Icons/.
/// Convention : folder.png, cs.png, kscene.png, wav.png, png.png, default.png
/// Si une icône est manquante, retourne IntPtr.Zero (fallback vers le placeholder).
/// </summary>
public static class EditorIcons
{
    static Renderer? renderer;
    static readonly Dictionary<string, IntPtr> icons = new();

    static readonly string[] IconNames =
    {
        "default", "folder", //Assets de base
        "cs", // Scripts
        "kscene", // Scènes
        ".png", ".jpg", ".jpeg", ".bmp", ".tga", // Images
        ".ttf", ".otf", // Polices
        ".wav", ".mp3", ".ogg", ".flac", // Audio
        "play", "stop", "pause", "step", // Boutons de l'éditeur
        "camera" // Gizmos
    };

    /// <summary>Retourne l'icône exacte, ou IntPtr.Zero — sans repli vers "default".</summary>
    public static IntPtr GetExact(string name) =>
        icons.TryGetValue(name.ToLowerInvariant(), out var t) ? t : IntPtr.Zero;

    public static void Init(Renderer r)
    {
        renderer = r;
        LoadAll();
    }

    static void LoadAll()
    {
        if (renderer == null) return;

        string root = Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Icons");
        if (!Directory.Exists(root))
        {
            Console.WriteLine($"[EditorIcons] Dossier d'icônes introuvable : {root}");
            return;
        }

        foreach (var name in IconNames)
        {
            string path = Path.Combine(root, $"{name}.png");
            if (!File.Exists(path)) continue;

            try
            {
                var tex = renderer.LoadTexture(path);
                if (tex != IntPtr.Zero)
                {
                    SDL.SetTextureScaleMode(tex, SDL.ScaleMode.Linear);
                    icons[name] = tex;
                }
            }
            catch { /* icône manquante ou invalide, on ignore */ }
        }
    }

    /// <summary>
    /// Retourne l'icône pour une extension donnée (sans le point).
    /// Retourne l'icône "default" si l'extension n'a pas d'icône dédiée.
    /// Retourne IntPtr.Zero si aucune icône n'est disponible.
    /// </summary>
    public static IntPtr Get(string extOrName)
    {
        string key = extOrName.ToLowerInvariant().TrimStart('.');
        if (icons.TryGetValue(key, out var tex)) return tex;
        if (icons.TryGetValue("default", out var def)) return def;
        return IntPtr.Zero;
    }

    public static IntPtr Folder =>
        icons.TryGetValue("folder", out var t) ? t : IntPtr.Zero;
}