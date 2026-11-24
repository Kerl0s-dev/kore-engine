using System.Globalization;
using System.Reflection;
using System.Text;

namespace KoreEngine;

/// <summary>Interpolation entre une clé et la suivante.</summary>
public enum Interpolation
{
    Linear,
    Step,     // valeur de la clé jusqu'à la clé suivante (frames de spritesheet, booléens)
    Smooth    // smoothstep (ease in/out)
}

public struct Keyframe
{
    public float Time;                       // secondes
    public float Value;
    public Interpolation Interpolation;      // s'applique au segment qui COMMENCE à cette clé
}

/// <summary>Courbe animant une propriété, désignée par un chemin "Composant.Membre[.Sous-membre]".</summary>
public class AnimationTrack
{
    /// <summary>Ex. "Transform.Rotation", "Transform.Position.X", "SpriteRenderer.Color.A", "SpriteRenderer.Frame".</summary>
    public string Path = "";
    public List<Keyframe> Keys = new();

    public void SortKeys() => Keys.Sort((a, b) => a.Time.CompareTo(b.Time));

    /// <summary>Insère ou met à jour la clé au temps donné (tolérance 0,5 ms). Renvoie son index.</summary>
    public int SetKey(float time, float value, Interpolation? interpolation = null)
    {
        for (int i = 0; i < Keys.Count; i++)
        {
            if (MathF.Abs(Keys[i].Time - time) >= 0.0005f) continue;
            var k = Keys[i];
            k.Value = value;
            if (interpolation.HasValue) k.Interpolation = interpolation.Value;
            Keys[i] = k;
            return i;
        }

        var nk = new Keyframe { Time = time, Value = value, Interpolation = interpolation ?? Interpolation.Linear };
        int at = Keys.FindIndex(k => k.Time > time);
        if (at < 0) { Keys.Add(nk); return Keys.Count - 1; }
        Keys.Insert(at, nk);
        return at;
    }

    public float Evaluate(float time)
    {
        int n = Keys.Count;
        if (n == 0) return 0f;
        if (n == 1 || time <= Keys[0].Time) return Keys[0].Value;
        if (time >= Keys[n - 1].Time) return Keys[n - 1].Value;

        // Recherche du segment [i, i+1] contenant 'time' (les pistes sont courtes : recherche linéaire)
        int i = 0;
        while (i < n - 2 && time >= Keys[i + 1].Time) i++;

        Keyframe a = Keys[i], b = Keys[i + 1];
        float span = b.Time - a.Time;
        if (span <= 1e-6f) return b.Value;

        float u = (time - a.Time) / span;
        switch (a.Interpolation)
        {
            case Interpolation.Step: return a.Value;
            case Interpolation.Smooth: u = u * u * (3f - 2f * u); break;
        }
        return a.Value + (b.Value - a.Value) * u;
    }
}

/// <summary>
/// Un clip d'animation (fichier .kanim) : une durée, un mode de boucle et des pistes de keyframes.
/// Format texte :
///
///   # commentaire
///   Name: Walk
///   Duration: 0.8
///   Loop: true
///   Track: Transform.Rotation
///     0    0   linear
///     0.4  15  smooth
///     0.8  0
///
/// Chaque ligne de clé est "temps valeur [linear|step|smooth]" (linear par défaut).
/// </summary>
public class AnimationClip
{
    public const string Extension = ".kanim";

    public string Name = "New Clip";
    public float Duration = 1f;
    public bool Loop = true;
    public List<AnimationTrack> Tracks = new();

    /// <summary>Durée effective : au moins celle de la dernière clé, jamais nulle.</summary>
    public float EffectiveDuration
    {
        get
        {
            float d = Duration;
            foreach (var t in Tracks)
                if (t.Keys.Count > 0) d = MathF.Max(d, t.Keys[^1].Time);
            return MathF.Max(d, 1e-3f);
        }
    }

    // ------------------------------------------------------------------
    // Texte
    // ------------------------------------------------------------------

    public static AnimationClip Parse(string text, List<string>? warnings = null)
    {
        var inv = CultureInfo.InvariantCulture;
        var clip = new AnimationClip();
        AnimationTrack? track = null;
        int lineNo = 0;

        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line[0] == '#') continue;

            int colon = line.IndexOf(':');
            if (colon > 0 && !char.IsDigit(line[0]) && line[0] != '-' && line[0] != '.')
            {
                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                switch (key.ToLowerInvariant())
                {
                    case "name": clip.Name = value; break;
                    case "duration":
                        if (float.TryParse(value, NumberStyles.Float, inv, out float d)) clip.Duration = d;
                        else warnings?.Add($"ligne {lineNo} : durée invalide « {value} »");
                        break;
                    case "loop": clip.Loop = value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "track":
                        track = new AnimationTrack { Path = value };
                        clip.Tracks.Add(track);
                        break;
                    default: warnings?.Add($"ligne {lineNo} : clé inconnue « {key} »"); break;
                }
                continue;
            }

            // Ligne de keyframe
            if (track == null) { warnings?.Add($"ligne {lineNo} : keyframe hors d'une piste"); continue; }

            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2
                || !float.TryParse(p[0], NumberStyles.Float, inv, out float time)
                || !float.TryParse(p[1], NumberStyles.Float, inv, out float val))
            {
                warnings?.Add($"ligne {lineNo} : keyframe invalide « {line} »");
                continue;
            }

            var interp = Interpolation.Linear;
            if (p.Length >= 3 && !Enum.TryParse(p[2], true, out interp))
            {
                warnings?.Add($"ligne {lineNo} : interpolation inconnue « {p[2]} »");
                interp = Interpolation.Linear;
            }
            track.Keys.Add(new Keyframe { Time = time, Value = val, Interpolation = interp });
        }

        foreach (var t in clip.Tracks) t.SortKeys();
        return clip;
    }

    public string ToText()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("# KoreEngine animation clip\n");
        sb.Append($"Name: {Name}\n");
        sb.Append($"Duration: {Duration.ToString("0.###", inv)}\n");
        sb.Append($"Loop: {(Loop ? "true" : "false")}\n");
        foreach (var t in Tracks)
        {
            sb.Append($"Track: {t.Path}\n");
            foreach (var k in t.Keys)
            {
                sb.Append($"  {k.Time.ToString("0.###", inv)} {k.Value.ToString("0.####", inv)}");
                if (k.Interpolation != Interpolation.Linear) sb.Append($" {k.Interpolation.ToString().ToLowerInvariant()}");
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    public static AnimationClip Load(string fullPath, List<string>? warnings = null)
    {
        var clip = Parse(File.ReadAllText(fullPath), warnings);
        if (clip.Name == "New Clip") clip.Name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
        return clip;
    }

    public void Save(string fullPath) => File.WriteAllText(fullPath, ToText(), new UTF8Encoding(false));

    // ------------------------------------------------------------------
    // Fabriques
    // ------------------------------------------------------------------

    /// <summary>
    /// Clip de spritesheet : une piste "SpriteRenderer.Frame" en escalier. Les frames sont numérotées
    /// ligne par ligne dans la grille de la texture ; <paramref name="firstFrame"/> est l'index de départ.
    /// </summary>
    public static AnimationClip FromFrames(string name, int firstFrame, int frameCount, float frameDuration, bool loop = true)
    {
        var track = new AnimationTrack { Path = "SpriteRenderer.Frame" };
        for (int i = 0; i < frameCount; i++)
            track.Keys.Add(new Keyframe { Time = i * frameDuration, Value = firstFrame + i, Interpolation = Interpolation.Step });

        return new AnimationClip
        {
            Name = name,
            Duration = frameCount * frameDuration,
            Loop = loop,
            Tracks = { track }
        };
    }
}

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

/// <summary>
/// Écrit une valeur flottante dans un membre (champ ou propriété) d'un objet, désigné par un chemin
/// de membres : ["Position", "X"], ["Color", "A"]... Les structs intermédiaires (Vector2) sont
/// réécrits après modification. Les feuilles supportées : float, double, int, byte, bool.
/// </summary>
public sealed class PropertyBinding
{
    readonly object root;
    readonly MemberInfo[] chain;
    readonly Type leafType;

    PropertyBinding(object root, MemberInfo[] chain, Type leafType)
    {
        this.root = root;
        this.chain = chain;
        this.leafType = leafType;
    }

    public static PropertyBinding? Create(object root, IReadOnlyList<string> memberNames, out string error)
    {
        error = "";
        if (memberNames.Count == 0) { error = "aucun membre"; return null; }

        var chain = new MemberInfo[memberNames.Count];
        Type current = root.GetType();

        for (int i = 0; i < memberNames.Count; i++)
        {
            MemberInfo? m = FindMember(current, memberNames[i]);
            if (m == null)
            {
                error = $"« {memberNames[i]} » introuvable dans {current.Name}";
                return null;
            }
            chain[i] = m;
            current = TypeOf(m);
        }

        if (!IsSupportedLeaf(current))
        {
            error = IsStructOrClass(current)
                ? $"« {memberNames[^1]} » est de type {current.Name} : précisez un sous-membre (par exemple .X)"
                : $"le type {current.Name} n'est pas animable";
            return null;
        }
        if (!CanWrite(chain[^1])) { error = $"« {memberNames[^1]} » est en lecture seule"; return null; }

        return new PropertyBinding(root, chain, current);
    }

    static MemberInfo? FindMember(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        return (MemberInfo?)type.GetField(name, flags) ?? type.GetProperty(name, flags);
    }

    static Type TypeOf(MemberInfo m) => m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;

    static bool CanWrite(MemberInfo m) => m is FieldInfo f ? !f.IsInitOnly : ((PropertyInfo)m).CanWrite;

    static bool IsSupportedLeaf(Type t) =>
        t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(byte) || t == typeof(bool);

    static bool IsStructOrClass(Type t) => !t.IsPrimitive && !t.IsEnum && t != typeof(string);

    static object? Get(MemberInfo m, object target) =>
        m is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)m).GetValue(target);

    static void Set(MemberInfo m, object target, object? value)
    {
        if (m is FieldInfo f) f.SetValue(target, value);
        else ((PropertyInfo)m).SetValue(target, value);
    }

    object ConvertLeaf(float v)
    {
        if (leafType == typeof(float)) return v;
        if (leafType == typeof(double)) return (double)v;
        if (leafType == typeof(int)) return (int)MathF.Round(v);
        if (leafType == typeof(byte)) return (byte)System.Math.Clamp((int)MathF.Round(v), 0, 255);
        return v >= 0.5f; // bool
    }

    /// <summary>Écrit la valeur ; renvoie false si un objet intermédiaire est nul.</summary>
    public bool TrySet(float value) => SetRec(root, 0, ConvertLeaf(value));

    bool SetRec(object target, int i, object leafValue)
    {
        MemberInfo m = chain[i];
        if (i == chain.Length - 1)
        {
            Set(m, target, leafValue);
            return true;
        }

        object? child = Get(m, target);
        if (child == null) return false;
        if (!SetRec(child, i + 1, leafValue)) return false;

        // child est une copie encadrée (boxed) d'un struct : on la réécrit dans son parent
        if (child.GetType().IsValueType) Set(m, target, child);
        return true;
    }
}

/// <summary>Lie les pistes d'un clip aux propriétés d'un GameObject et les applique (Animator, aperçu de la timeline).</summary>
public static class AnimationBinder
{
    /// <summary>Une liaison par piste (null si la piste est invalide : l'erreur est envoyée à <paramref name="warn"/>).</summary>
    public static PropertyBinding?[] Bind(GameObject go, AnimationClip clip, Action<string>? warn = null)
    {
        var result = new PropertyBinding?[clip.Tracks.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = BindTrack(go, clip, clip.Tracks[i], warn);
        return result;
    }

    static PropertyBinding? BindTrack(GameObject go, AnimationClip clip, AnimationTrack track, Action<string>? warn)
    {
        string[] parts = track.Path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            warn?.Invoke($"Clip « {clip.Name} » : chemin invalide « {track.Path} » (attendu Composant.Membre).");
            return null;
        }

        Component? comp = parts[0].Equals("Transform", StringComparison.OrdinalIgnoreCase)
            ? go.transform
            : go.Components.Find(c => c.GetType().Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));

        if (comp == null)
        {
            warn?.Invoke($"Clip « {clip.Name} » : « {go.Name} » n'a pas de composant {parts[0]} ({track.Path}).");
            return null;
        }

        var binding = PropertyBinding.Create(comp, parts[1..], out string error);
        if (binding == null)
            warn?.Invoke($"Clip « {clip.Name} » : piste « {track.Path} » ignorée : {error}.");
        return binding;
    }

    /// <summary>Écrit sur l'objet la valeur de chaque piste au temps donné.</summary>
    public static void Apply(PropertyBinding?[] bindings, AnimationClip clip, float time)
    {
        for (int i = 0; i < bindings.Length && i < clip.Tracks.Count; i++)
            bindings[i]?.TrySet(clip.Tracks[i].Evaluate(time));
    }

    /// <summary>Identifie l'ensemble des chemins d'un clip : change quand une piste est ajoutée, retirée ou renommée.</summary>
    public static string Signature(AnimationClip clip) => string.Join('|', clip.Tracks.Select(t => t.Path));
}