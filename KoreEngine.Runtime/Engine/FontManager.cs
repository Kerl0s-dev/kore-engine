// Engine/FontManager.cs
using SDL3;

namespace KoreEngine;

public static class FontManager
{
    static IntPtr rendererHandle;
    static Dictionary<string, IntPtr> fonts = new();
    static bool initialized = false;

    public static void Init(Renderer renderer)
    {
        if (initialized) return;
        TTF.Init();
        rendererHandle = renderer.Handle;
        initialized = true;
    }

    public static IntPtr GetFont(string path, float size)
    {
        if (!initialized)
        {
            Logger.Error("[FontManager] Init() n'a pas été appelé avant d'ouvrir une police.");
            return IntPtr.Zero;
        }

        string key = $"{path}:{size}";
        if (!fonts.TryGetValue(key, out var font))
        {
            font = TTF.OpenFont(path, size);
            if (font == IntPtr.Zero)
                Logger.Error($"[FontManager] Impossible d'ouvrir la police '{path}' : {SDL.GetError()}");
            fonts[key] = font;   // l'échec est mis en cache : un seul message
        }
        return font;
    }

    public static IntPtr CreateText(string fontPath, float size, string content = "")
    {
        return GetFont(fontPath, size);
    }

    // Cache des textes déjà rendus : (police, texte, couleur) → texture
    const int MaxCachedTexts = 256;
    static readonly Dictionary<(IntPtr Font, string Text, uint Color), (IntPtr Texture, int W, int H)> textCache = new();

    public static void DrawText(IntPtr fontHandle, string content, float x, float y,
        byte r, byte g, byte b, byte a = 255, float scale = 1f)
    {
        if (!TryGetTextTexture(fontHandle, content, r, g, b, a, out var cached)) return;

        var dest = new SDL.FRect { X = x, Y = y, W = cached.W * scale, H = cached.H * scale };
        SDL.RenderTexture(rendererHandle, cached.Texture, IntPtr.Zero, dest);
    }

    static bool TryGetTextTexture(IntPtr fontHandle, string content, byte r, byte g, byte b, byte a, out (IntPtr Texture, int W, int H) cached)
    {
        cached = default;
        if (fontHandle == IntPtr.Zero || string.IsNullOrEmpty(content)) return false;

        uint color = ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        var key = (fontHandle, content, color);

        if (!textCache.TryGetValue(key, out cached))
        {
            // Borne simple : au-delà, on vide tout (évite l'accumulation de textes qui changent à chaque frame)
            if (textCache.Count >= MaxCachedTexts) ClearTextCache();

            cached = default;
            IntPtr surface = TTF.RenderTextBlended(fontHandle, content, 0,
                new SDL.Color { R = r, G = g, B = b, A = a });
            if (surface != IntPtr.Zero)
            {
                IntPtr texture = SDL.CreateTextureFromSurface(rendererHandle, surface);
                SDL.DestroySurface(surface);

                if (texture != IntPtr.Zero)
                {
                    SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
                    SDL.SetTextureScaleMode(texture, SDL.ScaleMode.Linear);
                    SDL.GetTextureSize(texture, out float fw, out float fh);
                    cached = (texture, (int)fw, (int)fh);
                }
            }

            textCache[key] = cached;   // un échec est mémorisé aussi (Texture = Zero) : pas de nouvelle tentative à chaque frame
        }

        return cached.Texture != IntPtr.Zero;
    }

    public static void ClearTextCache()
    {
        foreach (var entry in textCache.Values)
            if (entry.Texture != IntPtr.Zero) SDL.DestroyTexture(entry.Texture);
        textCache.Clear();
    }

    public static void GetTextSize(IntPtr fontHandle, string content, out int w, out int h)
    {
        if (fontHandle == IntPtr.Zero || string.IsNullOrEmpty(content)) { w = 0; h = 0; return; }
        TTF.GetStringSize(fontHandle, content, 0, out w, out h);
    }
}