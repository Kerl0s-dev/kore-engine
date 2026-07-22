// Engine/FontManager.cs
using SDL3;

namespace KoreEngine.Engine;

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
        string key = $"{path}:{size}";
        if (!fonts.ContainsKey(key))
            fonts[key] = TTF.OpenFont(path, size);
        return fonts[key];
    }

    public static IntPtr CreateText(string fontPath, float size, string content = "")
    {
        return GetFont(fontPath, size);
    }

    public static void DrawText(IntPtr fontHandle, string content, float x, float y, byte r, byte g, byte b, byte a = 255)
    {
        if (fontHandle == IntPtr.Zero || string.IsNullOrEmpty(content)) return;

        IntPtr surface = TTF.RenderTextBlended(fontHandle, content, 0,
            new SDL.Color { R = r, G = g, B = b, A = a });
        if (surface == IntPtr.Zero) return;

        IntPtr texture = SDL.CreateTextureFromSurface(rendererHandle, surface);
        SDL.DestroySurface(surface);
        if (texture == IntPtr.Zero) return;

        SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
        SDL.GetTextureSize(texture, out float w, out float h);
        var dest = new SDL.FRect { X = x, Y = y, W = w, H = h };
        SDL.RenderTexture(rendererHandle, texture, IntPtr.Zero, dest);
        SDL.DestroyTexture(texture);
    }

    public static void GetTextSize(IntPtr fontHandle, string content, out int w, out int h)
    {
        if (fontHandle == IntPtr.Zero || string.IsNullOrEmpty(content)) { w = 0; h = 0; return; }
        TTF.GetStringSize(fontHandle, content, 0, out w, out h);
    }
}