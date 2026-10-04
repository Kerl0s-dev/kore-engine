using SDL3;

namespace KoreEngine.Hub;

/// <summary>
/// Rendu de texte TTF avec cache de textures. Chaque (texte, taille) n'est rendu
/// qu'une fois : le texte est généré en blanc puis teinté au dessin (ColorMod),
/// donc la couleur ne multiplie pas les entrées du cache.
/// Autonome (ne dépend pas de FontManager du Runtime). Thread principal uniquement.
/// À détruire AVANT le renderer.
/// </summary>
public sealed class TextCache : IDisposable
{
    const int MaxEntries = 512;

    readonly IntPtr renderer;
    readonly string fontPath;
    readonly Dictionary<float, IntPtr> fonts = new();
    readonly Dictionary<(string Text, float Size), Entry> entries = new();
    long frame;

    sealed class Entry
    {
        public IntPtr Texture;
        public float Width, Height;
        public long LastUsed;
    }

    public TextCache(IntPtr renderer, string fontPath)
    {
        if (!File.Exists(fontPath))
            throw new FileNotFoundException($"Police introuvable : {fontPath}");

        this.renderer = renderer;
        this.fontPath = fontPath;
        TTF.Init();
    }

    /// <summary>À appeler une fois par frame (sert à purger les textes inutilisés).</summary>
    public void BeginFrame() => frame++;

    /// <summary>Taille en pixels du texte, pour les layouts.</summary>
    public (float Width, float Height) Measure(string text, float size)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        var e = GetEntry(text, size);
        return e == null ? (0, 0) : (e.Width, e.Height);
    }

    public void Draw(string text, float size, float x, float y, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        if (string.IsNullOrEmpty(text)) return;

        var e = GetEntry(text, size);
        if (e == null) return;

        SDL.SetTextureColorMod(e.Texture, r, g, b);
        SDL.SetTextureAlphaMod(e.Texture, a);

        var dest = new SDL.FRect { X = x, Y = y, W = e.Width, H = e.Height };
        SDL.RenderTexture(renderer, e.Texture, IntPtr.Zero, dest);
    }

    Entry? GetEntry(string text, float size)
    {
        var key = (text, size);
        if (entries.TryGetValue(key, out var entry))
        {
            entry.LastUsed = frame;
            return entry;
        }

        IntPtr font = GetFont(size);
        if (font == IntPtr.Zero) return null;

        IntPtr surface = TTF.RenderTextBlended(font, text, 0,
            new SDL.Color { R = 255, G = 255, B = 255, A = 255 });
        if (surface == IntPtr.Zero) return null;

        IntPtr texture = SDL.CreateTextureFromSurface(renderer, surface);
        SDL.DestroySurface(surface);
        if (texture == IntPtr.Zero) return null;

        SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
        SDL.GetTextureSize(texture, out float w, out float h);

        if (entries.Count >= MaxEntries) Evict();

        entry = new Entry { Texture = texture, Width = w, Height = h, LastUsed = frame };
        entries[key] = entry;
        return entry;
    }

    IntPtr GetFont(float size)
    {
        if (fonts.TryGetValue(size, out var font)) return font;

        font = TTF.OpenFont(fontPath, size);
        if (font == IntPtr.Zero)
            Console.WriteLine($"[TextCache] OpenFont a échoué ({fontPath}, {size}) : {SDL.GetError()}");

        fonts[size] = font;
        return font;
    }

    /// <summary>Supprime les ~25 % d'entrées les moins récemment utilisées.</summary>
    void Evict()
    {
        var oldest = entries.OrderBy(kv => kv.Value.LastUsed).Take(MaxEntries / 4).ToList();
        foreach (var kv in oldest)
        {
            SDL.DestroyTexture(kv.Value.Texture);
            entries.Remove(kv.Key);
        }
    }

    public void Dispose()
    {
        foreach (var e in entries.Values) SDL.DestroyTexture(e.Texture);
        entries.Clear();

        foreach (var f in fonts.Values)
            if (f != IntPtr.Zero) TTF.CloseFont(f); // nom à vérifier dans ton binding
        fonts.Clear();
    }
}