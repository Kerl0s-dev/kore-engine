using System.Reflection;

namespace KoreEngine;

/// <summary>
/// Clip actuellement édité, partagé par l'Inspector de clip (ClipInspector) et la Timeline :
/// une seule copie en mémoire, un seul état « modifié ».
/// </summary>
public static class ClipEditSession
{
    static string savedText = "";

    public static string? Path { get; private set; }
    public static AnimationClip? Clip { get; private set; }
    public static List<string> Warnings { get; private set; } = new();

    /// <summary>Charge le clip si ce n'est pas déjà le clip courant. Renvoie true s'il a été (re)chargé.</summary>
    public static bool EnsureLoaded(string fullPath)
    {
        if (Clip != null && Path == fullPath) return false;
        Load(fullPath);
        return true;
    }

    static void Load(string fullPath)
    {
        Warnings = new List<string>();
        Clip = AnimationClip.Load(fullPath, Warnings);
        Path = fullPath;
        savedText = Clip.ToText();
    }

    /// <summary>Recharge le clip depuis le disque (abandonne les modifications).</summary>
    public static void Reload()
    {
        if (Path != null) Load(Path);
    }

    public static bool IsDirty => Clip != null && Clip.ToText() != savedText;

    public static void Save()
    {
        if (Clip == null || Path == null) return;
        foreach (var t in Clip.Tracks) t.SortKeys();
        Clip.Save(Path);
        savedText = Clip.ToText();
        AnimationClipCache.Clear();   // les Animators relisent le fichier à leur prochaine entrée dans l'état
    }
}

/// <summary>Référence à une keyframe : une piste et un index dans sa liste de clés.</summary>
public readonly record struct KeyRef(AnimationTrack Track, int Index);

/// <summary>Sélection de keyframes dans le dopesheet, avec déplacement, suppression et changement d'interpolation.</summary>
public sealed class KeySelection
{
    HashSet<KeyRef> items = new();
    readonly List<(AnimationTrack Track, int Index, float StartTime)> dragStart = new();
    float dragReference;

    public int Count => items.Count;
    public IEnumerable<KeyRef> Items => items;
    public bool Contains(AnimationTrack track, int index) => items.Contains(new KeyRef(track, index));
    public void Clear() => items.Clear();

    public void Select(AnimationTrack track, int index, bool additive)
    {
        if (!additive) items.Clear();
        items.Add(new KeyRef(track, index));
    }

    public void Toggle(AnimationTrack track, int index)
    {
        var r = new KeyRef(track, index);
        if (!items.Remove(r)) items.Add(r);
    }

    /// <summary>Sélectionne les clés des pistes [firstTrack, lastTrack] dont le temps est dans [t0, t1].</summary>
    public void SelectRect(AnimationClip clip, int firstTrack, int lastTrack, float t0, float t1, bool additive)
    {
        if (!additive) items.Clear();
        if (t0 > t1) (t0, t1) = (t1, t0);
        if (firstTrack > lastTrack) (firstTrack, lastTrack) = (lastTrack, firstTrack);

        for (int ti = Math.Max(0, firstTrack); ti <= lastTrack && ti < clip.Tracks.Count; ti++)
        {
            var track = clip.Tracks[ti];
            for (int i = 0; i < track.Keys.Count; i++)
                if (track.Keys[i].Time >= t0 && track.Keys[i].Time <= t1)
                    items.Add(new KeyRef(track, i));
        }
    }

    /// <summary>Retire les références devenues invalides (piste supprimée, index hors limites).</summary>
    public void Prune(AnimationClip clip)
    {
        items.RemoveWhere(r => !clip.Tracks.Contains(r.Track) || r.Index < 0 || r.Index >= r.Track.Keys.Count);
    }

    public static float Snap(float time, float step) => step <= 0f ? time : MathF.Round(time / step) * step;

    // ---------------- Déplacement ----------------

    /// <summary>Mémorise les temps de départ. <paramref name="grabbed"/> est la clé saisie à la souris (référence du magnétisme).</summary>
    public void BeginDrag(KeyRef grabbed)
    {
        dragStart.Clear();
        foreach (var r in items)
            dragStart.Add((r.Track, r.Index, r.Track.Keys[r.Index].Time));
        dragReference = grabbed.Track.Keys[grabbed.Index].Time;
    }

    /// <summary>Déplace toutes les clés sélectionnées de <paramref name="delta"/> secondes depuis leur départ (magnétisme sur la clé saisie, jamais avant 0).</summary>
    public void DragBy(float delta, float snapStep)
    {
        if (dragStart.Count == 0) return;

        delta = Snap(dragReference + delta, snapStep) - dragReference;
        float minStart = float.MaxValue;
        foreach (var d in dragStart) minStart = MathF.Min(minStart, d.StartTime);
        if (minStart + delta < 0f) delta = -minStart;

        foreach (var (track, index, start) in dragStart)
        {
            var k = track.Keys[index];
            k.Time = start + delta;
            track.Keys[index] = k;
        }
    }

    /// <summary>Termine le déplacement : retrie les pistes touchées et met la sélection à jour.</summary>
    public void EndDrag()
    {
        var affected = dragStart.Select(d => d.Track).Distinct().ToList();
        dragStart.Clear();

        var moved = new Dictionary<AnimationTrack, int[]>();
        foreach (var track in affected)
        {
            int n = track.Keys.Count;
            int[] order = Enumerable.Range(0, n).OrderBy(i => track.Keys[i].Time).ToArray();   // tri stable
            var oldToNew = new int[n];
            for (int newIdx = 0; newIdx < n; newIdx++) oldToNew[order[newIdx]] = newIdx;

            var sorted = order.Select(i => track.Keys[i]).ToList();
            track.Keys.Clear();
            track.Keys.AddRange(sorted);
            moved[track] = oldToNew;
        }

        var remapped = new HashSet<KeyRef>();
        foreach (var r in items)
            remapped.Add(moved.TryGetValue(r.Track, out var map) ? new KeyRef(r.Track, map[r.Index]) : r);
        items = remapped;
    }

    // ---------------- Édition ----------------

    /// <summary>Supprime les clés sélectionnées. Renvoie leur nombre.</summary>
    public int DeleteSelected()
    {
        int count = 0;
        foreach (var group in items.GroupBy(r => r.Track))
        {
            foreach (int idx in group.Select(r => r.Index).OrderByDescending(i => i))
            {
                group.Key.Keys.RemoveAt(idx);
                count++;
            }
        }
        items.Clear();
        return count;
    }

    public void SetInterpolation(Interpolation interpolation)
    {
        foreach (var r in items)
        {
            var k = r.Track.Keys[r.Index];
            k.Interpolation = interpolation;
            r.Track.Keys[r.Index] = k;
        }
    }
}

/// <summary>
/// Mode Record : compare les valeurs numériques d'un objet avant/après les modifications faites dans l'Inspector
/// et transforme chaque différence en keyframe au temps courant.
/// Cycle par frame : <see cref="Commit"/> (enregistre les différences) → application du clip → <see cref="Capture"/>.
/// </summary>
public sealed class RecordTracker
{
    const float Epsilon = 1e-4f;

    readonly Dictionary<string, float> baseline = new();
    readonly Dictionary<string, float> current = new();
    readonly Dictionary<string, Type> types = new();

    /// <summary>Mémorise les valeurs actuelles comme référence.</summary>
    public void Capture(GameObject root)
    {
        baseline.Clear();
        Enumerate(root, baseline, null);
    }

    /// <summary>
    /// Enregistre dans <paramref name="clip"/> les valeurs modifiées depuis <see cref="Capture"/>, au temps <paramref name="time"/>.
    /// Une nouvelle piste reçoit aussi une clé à 0 avec l'ancienne valeur (si le temps n'est pas 0), pour que le début du clip reste inchangé.
    /// Renvoie le nombre de propriétés modifiées.
    /// </summary>
    public int Commit(GameObject root, AnimationClip clip, float time)
    {
        current.Clear();
        types.Clear();
        Enumerate(root, current, types);

        time = MathF.Round(Math.Max(0f, time) * 1000f) / 1000f;
        int changes = 0;

        foreach (var (path, value) in current)
        {
            if (!baseline.TryGetValue(path, out float old)) continue;
            if (MathF.Abs(value - old) < Epsilon) continue;

            // float/double interpolés ; int, byte et bool en escalier (frames, indicateurs)
            var type = types[path];
            var interpolation = type == typeof(float) || type == typeof(double) ? Interpolation.Linear : Interpolation.Step;

            var track = clip.Tracks.Find(t => t.Path == path);
            if (track == null)
            {
                track = new AnimationTrack { Path = path };
                clip.Tracks.Add(track);
                if (time > 0.0005f) track.SetKey(0f, old, interpolation);
            }
            track.SetKey(time, value, interpolation);
            changes++;
        }
        return changes;
    }

    /// <summary>Lit la valeur actuelle d'une propriété animable (ex. "Transform.Rotation") sur un objet.</summary>
    public static bool TryRead(GameObject root, string path, out float value)
    {
        var values = new Dictionary<string, float>();
        Enumerate(root, values, null);
        return values.TryGetValue(path, out value);
    }

    static bool TryNumber(object? v, Type t, out float x)
    {
        x = 0f;
        if (v == null) return false;
        if (t == typeof(float)) x = (float)v;
        else if (t == typeof(double)) x = (float)(double)v;
        else if (t == typeof(int)) x = (int)v;
        else if (t == typeof(byte)) x = (byte)v;
        else if (t == typeof(bool)) x = (bool)v ? 1f : 0f;
        else return false;
        return true;
    }

    /// <summary>Liste les propriétés numériques animables : champs publics des composants, et sous-champs de Vector2 / Color.</summary>
    static void Enumerate(GameObject go, Dictionary<string, float> values, Dictionary<string, Type>? types)
    {
        var components = new List<Component> { go.transform };
        foreach (var c in go.Components)
            if (!ReferenceEquals(c, go.transform)) components.Add(c);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var comp in components)
        {
            if (comp is Animator) continue;
            string cname = comp.GetType().Name;

            foreach (var f in comp.GetType().GetFields(flags))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                object? v = f.GetValue(comp);

                if (TryNumber(v, f.FieldType, out float x))
                {
                    string path = $"{cname}.{f.Name}";
                    values[path] = x;
                    if (types != null) types[path] = f.FieldType;
                }
                else if (v != null && (f.FieldType == typeof(Vector2) || f.FieldType == typeof(Color)))
                {
                    foreach (var sub in f.FieldType.GetFields(flags))
                    {
                        if (sub.IsInitOnly || sub.IsLiteral) continue;
                        if (!TryNumber(sub.GetValue(v), sub.FieldType, out float sx)) continue;
                        string path = $"{cname}.{f.Name}.{sub.Name}";
                        values[path] = sx;
                        if (types != null) types[path] = sub.FieldType;
                    }
                }
            }
        }
    }
}