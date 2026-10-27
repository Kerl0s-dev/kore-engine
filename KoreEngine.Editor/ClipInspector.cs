using ImGuiNET;

namespace KoreEngine;

/// <summary>
/// Inspector d'un clip .kanim (sélectionné dans le Project Panel) : propriétés du clip, pistes et keyframes,
/// avec Save / Revert. Appelé par AssetInspector.Draw pour l'extension .kanim.
/// </summary>
public static class ClipInspector
{
    static string? loadedPath;
    static AnimationClip? clip;
    static string savedText = "";
    static List<string> warnings = new();

    // Réglages du générateur de piste de frames
    static int genFirstFrame;
    static int genFrameCount = 4;
    static float genFrameDuration = 0.1f;

    static readonly string[] TrackPresets =
    {
        "(custom)",
        "Transform.Position.X", "Transform.Position.Y",
        "Transform.Rotation",
        "Transform.Scale.X", "Transform.Scale.Y",
        "SpriteRenderer.Frame",
        "SpriteRenderer.Color.R", "SpriteRenderer.Color.G", "SpriteRenderer.Color.B", "SpriteRenderer.Color.A",
        "SpriteRenderer.FlipX", "SpriteRenderer.FlipY",
        "SpriteRenderer.Size.X", "SpriteRenderer.Size.Y",
    };

    static readonly string[] InterpolationNames = Enum.GetNames<Interpolation>();

    public static void Draw(string path, bool readOnly)
    {
        if (loadedPath != path || clip == null)
        {
            warnings = new List<string>();
            clip = AnimationClip.Load(path, warnings);
            savedText = clip.ToText();
            loadedPath = path;
        }
        var c = clip;

        ImGui.TextDisabled("Animation Clip");
        foreach (var w in warnings)
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), w);

        ImGui.BeginDisabled(readOnly);
        foreach (var f in ClipFields(c))
            InspectorPanel.DrawFieldDescriptor(c, f);
        ImGui.EndDisabled();

        ImGui.Separator();

        bool dirty = c.ToText() != savedText;
        ImGui.BeginDisabled(readOnly || !dirty);
        if (ImGui.Button("Save"))
        {
            foreach (var t in c.Tracks) t.SortKeys();
            c.Save(path);
            savedText = c.ToText();
            AnimationClipCache.Clear();   // les Animators en cours de jeu rechargent le fichier
        }
        ImGui.SameLine();
        if (ImGui.Button("Revert")) loadedPath = null;   // rechargé depuis le disque à la prochaine frame
        ImGui.EndDisabled();

        if (dirty)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("modifié, non enregistré");
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Chemins : Composant.Membre[.Sous-membre] sur l'objet portant l'Animator (ex. Transform.Rotation).");
        ImGui.TextDisabled("Frames : « Step » pour passer d'une frame à l'autre sans interpolation.");
    }

    static IEnumerable<InspectorField> ClipFields(AnimationClip c)
    {
        yield return new StringField { Label = "Name", Get = () => c.Name, Set = v => c.Name = v };
        yield return new FloatField
        {
            Label = "Duration (s)",
            Get = () => c.Duration,
            Set = v => c.Duration = v,
            Value = 0.05f,
            Min = 0.01f,
            Max = 600f
        };
        yield return new BoolField { Label = "Loop", Get = () => c.Loop, Set = v => c.Loop = v };

        yield return new ListField
        {
            Label = "Tracks",
            Count = () => c.Tracks.Count,
            ItemHeader = i => string.IsNullOrEmpty(c.Tracks[i].Path)
                ? $"Track {i}" : $"{c.Tracks[i].Path} ({c.Tracks[i].Keys.Count})",
            ItemFields = i => TrackFields(c, i),
            AddItem = () => c.Tracks.Add(new AnimationTrack
            {
                Path = "Transform.Rotation",
                Keys = { new Keyframe { Time = 0f, Value = 0f } }
            }),
            RemoveItem = i => c.Tracks.RemoveAt(i)
        };

        // Piste de frames de spritesheet : numérotation ligne par ligne dans la grille FrameSize du SpriteRenderer
        yield return new IntField { Label = "First Frame", Get = () => genFirstFrame, Set = v => genFirstFrame = v, Min = 0, Max = 4096 };
        yield return new IntField { Label = "Frame Count", Get = () => genFrameCount, Set = v => genFrameCount = v, Min = 1, Max = 256 };
        yield return new FloatField
        {
            Label = "Frame Duration (s)",
            Get = () => genFrameDuration,
            Set = v => genFrameDuration = v,
            Value = 0.01f,
            Min = 0.01f,
            Max = 5f
        };
        yield return new ButtonField
        {
            Label = "Add Sprite Frames Track",
            Action = () =>
            {
                var g = AnimationClip.FromFrames("", genFirstFrame, genFrameCount, genFrameDuration);
                c.Tracks.Add(g.Tracks[0]);
                c.Duration = MathF.Max(c.Duration, g.Duration);
            },
            Tooltip = "Ajoute une piste SpriteRenderer.Frame en escalier (frames consécutives)."
        };
    }

    static IEnumerable<InspectorField> TrackFields(AnimationClip c, int i)
    {
        var t = c.Tracks[i];
        yield return new EnumField
        {
            Label = "Preset",
            Names = TrackPresets,
            Get = () => Math.Max(0, Array.IndexOf(TrackPresets, t.Path)),
            Set = idx => { if (idx > 0) t.Path = TrackPresets[idx]; }
        };
        yield return new StringField { Label = "Path", Get = () => t.Path, Set = v => t.Path = v, MaxLength = 128 };

        yield return new ListField
        {
            Label = $"Keys##trk{i}",
            Count = () => t.Keys.Count,
            ItemHeader = k => $"{t.Keys[k].Time:0.###} s  →  {t.Keys[k].Value:0.###}",
            ItemFields = k => KeyFields(t, k),
            AddItem = () =>
            {
                float time = t.Keys.Count > 0 ? t.Keys[^1].Time + 0.1f : 0f;
                float value = t.Keys.Count > 0 ? t.Keys[^1].Value : 0f;
                t.Keys.Add(new Keyframe { Time = time, Value = value });
            },
            RemoveItem = k => t.Keys.RemoveAt(k)
        };

        yield return new ButtonField { Label = $"Sort Keys##srt{i}", Action = t.SortKeys, Tooltip = "Trie les keyframes par temps." };
    }

    static IEnumerable<InspectorField> KeyFields(AnimationTrack t, int k)
    {
        yield return new FloatField
        {
            Label = "Time",
            Get = () => t.Keys[k].Time,
            Set = v => { var key = t.Keys[k]; key.Time = v; t.Keys[k] = key; },
            Value = 0.01f,
            Min = 0f,
            Max = 600f
        };
        yield return new FloatField
        {
            Label = "Value",
            Get = () => t.Keys[k].Value,
            Set = v => { var key = t.Keys[k]; key.Value = v; t.Keys[k] = key; },
            Value = 0.1f
        };
        yield return new EnumField
        {
            Label = "Interpolation",
            Names = InterpolationNames,
            Get = () => (int)t.Keys[k].Interpolation,
            Set = idx => { var key = t.Keys[k]; key.Interpolation = (Interpolation)idx; t.Keys[k] = key; }
        };
    }
}