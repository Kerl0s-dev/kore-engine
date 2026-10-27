namespace KoreEngine;

/// <summary>Cache des clips .kanim, rechargés automatiquement quand le fichier change sur le disque.</summary>
public static class AnimationClipCache
{
    sealed class Entry
    {
        public AnimationClip Clip = null!;
        public DateTime WriteTimeUtc;
    }

    static readonly Dictionary<string, Entry> cache = new();

    /// <param name="path">Chemin de projet portable ("Assets/Anim/walk.kanim").</param>
    public static AnimationClip? Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string full = Path.GetFullPath(TextureCache.Resolve(path));
        if (!File.Exists(full))
        {
            Logger.Warning($"Clip d'animation introuvable : {path}");
            return null;
        }

        DateTime stamp = File.GetLastWriteTimeUtc(full);
        if (cache.TryGetValue(full, out var e) && e.WriteTimeUtc == stamp) return e.Clip;

        var warnings = new List<string>();
        var clip = AnimationClip.Load(full, warnings);
        foreach (var w in warnings) Logger.Warning($"{path} : {w}");

        cache[full] = new Entry { Clip = clip, WriteTimeUtc = stamp };
        return clip;
    }

    public static void Clear() => cache.Clear();
}