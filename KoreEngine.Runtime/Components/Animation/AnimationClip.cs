using System.Globalization;
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