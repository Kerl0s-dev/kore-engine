using SDL3;

namespace KoreEngine.Hub;

/// <summary>Texture SDL + sa taille en pixels (utile pour le layout).</summary>
public readonly record struct ImageInfo(IntPtr Texture, float Width, float Height)
{
    public bool IsValid => Texture != IntPtr.Zero;
}

/// <summary>
/// Cache d'images du Hub, indexé par chemin de fichier. Autonome : ne dépend
/// pas de KoreEngine.Runtime. À créer après le renderer et à détruire AVANT lui.
/// Rendu sur le thread principal uniquement.
/// </summary>
public sealed class ImageCache : IDisposable
{
    // Un échec n'est pas définitif : on réessaie après ce délai (ex. icône de
    // projet créée pendant que le Hub tourne), sans retoucher le disque à chaque frame.
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    readonly IntPtr renderer;
    readonly Dictionary<string, ImageInfo> images = new();
    readonly Dictionary<string, DateTime> failures = new();

    public ImageCache(IntPtr renderer) => this.renderer = renderer;

    /// <summary>Retourne l'image (chargée au premier appel). IsValid == false si indisponible.</summary>
    public ImageInfo Get(string path)
    {
        string key = Path.GetFullPath(path);

        if (images.TryGetValue(key, out var info)) return info;

        if (failures.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < RetryDelay)
            return default;

        info = Load(key, logErrors: !failures.ContainsKey(key));
        if (info.IsValid)
        {
            failures.Remove(key);
            images[key] = info;
        }
        else
        {
            failures[key] = DateTime.UtcNow;
        }
        return info;
    }

    ImageInfo Load(string path, bool logErrors)
    {
        if (!File.Exists(path))
        {
            if (logErrors) Console.WriteLine($"[ImageCache] Fichier introuvable : {path}");
            return default;
        }

        IntPtr surface = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => SDL.LoadPNG(path),
            ".bmp" => SDL.LoadBMP(path),
            // JPG & co : nécessitent SDL3_image (à brancher ici plus tard).
            _ => IntPtr.Zero
        };

        if (surface == IntPtr.Zero)
        {
            if (logErrors) Console.WriteLine($"[ImageCache] Format non supporté ou lecture impossible : {path} ({SDL.GetError()})");
            return default;
        }

        IntPtr texture = SDL.CreateTextureFromSurface(renderer, surface);
        SDL.DestroySurface(surface);

        if (texture == IntPtr.Zero)
        {
            if (logErrors) Console.WriteLine($"[ImageCache] Création de texture impossible : {path} ({SDL.GetError()})");
            return default;
        }

        SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
        SDL.SetTextureScaleMode(texture, SDL.ScaleMode.Linear); // miniatures lisses (nom d'enum à vérifier)
        SDL.GetTextureSize(texture, out float w, out float h);

        return new ImageInfo(texture, w, h);
    }

    /// <summary>Oublie (et détruit) une image pour forcer un rechargement au prochain Get.</summary>
    public void Invalidate(string path)
    {
        string key = Path.GetFullPath(path);
        failures.Remove(key);
        if (images.Remove(key, out var info))
            SDL.DestroyTexture(info.Texture);
    }

    /// <summary>Dessine l'image étirée dans le rectangle donné.</summary>
    public void Draw(ImageInfo image, float x, float y, float w, float h)
    {
        if (!image.IsValid) return;
        var dest = new SDL.FRect { X = x, Y = y, W = w, H = h };
        SDL.RenderTexture(renderer, image.Texture, IntPtr.Zero, dest);
    }

    /// <summary>Dessine l'image centrée dans le rectangle, en gardant ses proportions.</summary>
    public void DrawFit(ImageInfo image, float x, float y, float w, float h)
    {
        if (!image.IsValid || image.Width <= 0 || image.Height <= 0) return;

        float scale = Math.Min(w / image.Width, h / image.Height);
        float dw = image.Width * scale;
        float dh = image.Height * scale;
        Draw(image, x + (w - dw) / 2f, y + (h - dh) / 2f, dw, dh);
    }

    public void Dispose()
    {
        foreach (var info in images.Values)
            SDL.DestroyTexture(info.Texture);
        images.Clear();
        failures.Clear();
    }
}