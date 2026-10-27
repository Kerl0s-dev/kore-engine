using System.Globalization;

namespace KoreEngine;

/// <summary>
/// Joue des clips .kanim (keyframes sur des propriétés) selon une machine à états pilotée par des paramètres.
/// Les scripts modifient les paramètres : SetBool / SetFloat / SetTrigger.
/// </summary>
public class Animator : Component
{
    public AnimatorController Controller = new();

    // Animations de l'ancien format (Def:/Trans:), conservées tant qu'elles ne sont pas converties en .kanim
    sealed class LegacyDefinition
    {
        public string Name = "";
        public int SizeX, SizeY, Row, StartCol, FrameCount;
        public float FrameDuration;
        public bool Looping;
    }
    readonly List<LegacyDefinition> legacyDefs = new();
    readonly List<string> legacyLines = new();
    readonly Dictionary<AnimationClip, PropertyBinding?[]> bindingCache = new();
    readonly Dictionary<string, AnimationClip> legacyClips = new();

    // ------------------------------------------------------------------
    // API pour les scripts
    // ------------------------------------------------------------------

    public void SetBool(string name, bool value) => Controller.SetBool(name, value);
    public void SetFloat(string name, float value) => Controller.SetFloat(name, value);
    public void SetTrigger(string name) => Controller.SetTrigger(name);
    public void ResetTrigger(string name) => Controller.ResetTrigger(name);
    public bool GetBool(string name) => Controller.GetBool(name);
    public float GetFloat(string name) => Controller.GetFloat(name);

    /// <summary>Passe immédiatement dans un état. Renvoie false si l'état n'existe pas.</summary>
    public bool Play(string stateName)
    {
        bool ok = Controller.Play(stateName);
        if (ok) ApplyPose();
        return ok;
    }

    public string CurrentState => Controller.CurrentState?.Name ?? "";

    /// <summary>Vrai quand le clip courant (sans boucle) est terminé.</summary>
    public bool IsFinished => Controller.IsFinished;

    // ------------------------------------------------------------------
    // Cycle de vie
    // ------------------------------------------------------------------

    public override void Start()
    {
        Controller.ClipProvider = ResolveClip;
        bindingCache.Clear();
        legacyClips.Clear();
        Controller.Start();

        if (legacyLines.Count > 0)
            Logger.Warning($"Animator « {gameObject.Name} » : {legacyLines.Count} transition(s) de l'ancien format ignorée(s). Recréez-les avec des paramètres.");

        ApplyPose();
    }

    public override void Update(float dt)
    {
        Controller.Update(dt);
        ApplyPose();
    }

    void ApplyPose()
    {
        var clip = Controller.CurrentClip;
        if (clip == null) return;

        var bindings = GetBindings(clip);
        float t = Controller.Time;
        for (int i = 0; i < bindings.Length; i++)
            bindings[i]?.TrySet(clip.Tracks[i].Evaluate(t));
    }

    // ------------------------------------------------------------------
    // Résolution des clips et des propriétés
    // ------------------------------------------------------------------

    AnimationClip? ResolveClip(AnimatorState state)
    {
        if (!string.IsNullOrEmpty(state.ClipPath))
            return AnimationClipCache.Get(state.ClipPath);

        // État issu de l'ancien format : clip généré en mémoire
        var def = legacyDefs.Find(d => d.Name == state.Name);
        if (def == null) return null;
        if (legacyClips.TryGetValue(def.Name, out var cached)) return cached;

        var sprite = gameObject.GetComponent<SpriteRenderer>();
        int columns = sprite?.Columns ?? 1;
        var clip = AnimationClip.FromFrames(def.Name, def.Row * columns + def.StartCol, def.FrameCount, def.FrameDuration, def.Looping);
        legacyClips[def.Name] = clip;
        return clip;
    }

    PropertyBinding?[] GetBindings(AnimationClip clip)
    {
        if (bindingCache.TryGetValue(clip, out var cached)) return cached;

        var result = new PropertyBinding?[clip.Tracks.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = CreateBinding(clip, clip.Tracks[i]);

        bindingCache[clip] = result;
        return result;
    }

    PropertyBinding? CreateBinding(AnimationClip clip, AnimationTrack track)
    {
        string[] parts = track.Path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            Logger.Warning($"Clip « {clip.Name} » : chemin invalide « {track.Path} » (attendu Composant.Membre).");
            return null;
        }

        Component? comp = parts[0].Equals("Transform", StringComparison.OrdinalIgnoreCase)
            ? gameObject.transform
            : gameObject.Components.Find(c => c.GetType().Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));

        if (comp == null)
        {
            Logger.Warning($"Clip « {clip.Name} » : « {gameObject.Name} » n'a pas de composant {parts[0]} ({track.Path}).");
            return null;
        }

        var binding = PropertyBinding.Create(comp, parts[1..], out string error);
        if (binding == null)
            Logger.Warning($"Clip « {clip.Name} » : piste « {track.Path} » ignorée : {error}.");
        return binding;
    }

    // ------------------------------------------------------------------
    // Sérialisation
    // ------------------------------------------------------------------

    public override IEnumerable<string> Serialize()
    {
        foreach (var line in Controller.Serialize()) yield return line;

        var inv = CultureInfo.InvariantCulture;
        foreach (var d in legacyDefs)
            yield return $"Def: {d.Name},{d.SizeX},{d.SizeY},{d.Row},{d.StartCol},{d.FrameCount},{d.FrameDuration.ToString(inv)},{(d.Looping ? "true" : "false")}";
        foreach (var l in legacyLines) yield return l;
    }

    public override void Deserialize(List<string> extraLines)
    {
        var inv = CultureInfo.InvariantCulture;
        legacyDefs.Clear();
        legacyLines.Clear();

        var unknown = Controller.Deserialize(extraLines);
        foreach (var line in unknown)
        {
            int colon = line.IndexOf(':');
            string key = colon < 0 ? "" : line[..colon].Trim();
            var p = (colon < 0 ? "" : line[(colon + 1)..]).Split(',');

            if (key == "Def" && p.Length >= 8)
            {
                var d = new LegacyDefinition
                {
                    Name = p[0].Trim(),
                    SizeX = int.Parse(p[1], inv),
                    SizeY = int.Parse(p[2], inv),
                    Row = int.Parse(p[3], inv),
                    StartCol = int.Parse(p[4], inv),
                    FrameCount = int.Parse(p[5], inv),
                    FrameDuration = float.Parse(p[6], inv),
                    Looping = p[7].Trim() == "true"
                };
                legacyDefs.Add(d);
                if (Controller.FindState(d.Name) == null)
                    Controller.States.Add(new AnimatorState { Name = d.Name, ClipPath = "" });
            }
            else if (key == "Trans")
                legacyLines.Add(line);
        }

        if (Controller.DefaultState == "" && Controller.States.Count > 0)
            Controller.DefaultState = Controller.States[0].Name;
    }

    // ------------------------------------------------------------------
    // Migration de l'ancien format
    // ------------------------------------------------------------------

    /// <summary>
    /// Écrit chaque animation de l'ancien format dans Assets/Animations/&lt;nom&gt;.kanim et les assigne aux états.
    /// Les anciennes transitions sont listées dans la Console puis supprimées.
    /// </summary>
    public void ConvertLegacy()
    {
        if (legacyDefs.Count == 0 && legacyLines.Count == 0) return;

        string dir = Path.Combine(SceneManager.AssetsDirectory, "Animations");
        Directory.CreateDirectory(dir);

        foreach (var d in legacyDefs)
        {
            var clip = ResolveClip(new AnimatorState { Name = d.Name });
            if (clip == null) continue;

            string file = Path.Combine(dir, d.Name + AnimationClip.Extension);
            clip.Save(file);

            var state = Controller.FindState(d.Name);
            if (state != null) state.ClipPath = AssetPaths.ToProjectPath(file);
        }

        foreach (var l in legacyLines) Logger.Warning($"Ancienne transition supprimée (à recréer avec des paramètres) : {l}");
        legacyDefs.Clear();
        legacyLines.Clear();
        legacyClips.Clear();
        bindingCache.Clear();
    }

    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------

    static readonly string[] ParameterTypeNames = Enum.GetNames<ParameterType>();
    static readonly string[] ConditionOpNames = Enum.GetNames<ConditionOp>();

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        var c = Controller;
        string[] stateNames = c.States.Select(s => s.Name).ToArray();
        string[] fromNames = new[] { "(Any)" }.Concat(stateNames).ToArray();
        string[] paramNames = c.Parameters.Select(p => p.Name).ToArray();

        yield return new EnumField
        {
            Label = "Default State",
            Names = stateNames.Length > 0 ? stateNames : new[] { "(none)" },
            Get = () => Math.Max(0, Array.IndexOf(stateNames, c.DefaultState)),
            Set = i => { if (i >= 0 && i < stateNames.Length) c.DefaultState = stateNames[i]; }
        };

        yield return new ListField
        {
            Label = "Parameters",
            Count = () => c.Parameters.Count,
            ItemHeader = i => $"{c.Parameters[i].Name} ({c.Parameters[i].Type})",
            ItemFields = ParameterFields,
            AddItem = () => c.Parameters.Add(new AnimatorParameter { Name = UniqueName("param", c.Parameters.Select(p => p.Name)) }),
            RemoveItem = i => c.Parameters.RemoveAt(i)
        };

        yield return new ListField
        {
            Label = "States",
            Count = () => c.States.Count,
            ItemHeader = i => string.IsNullOrEmpty(c.States[i].ClipPath)
                ? $"{c.States[i].Name}" : $"{c.States[i].Name}  ({Path.GetFileNameWithoutExtension(c.States[i].ClipPath)})",
            ItemFields = StateFields,
            AddItem = () => c.States.Add(new AnimatorState { Name = UniqueName("State", c.States.Select(s => s.Name)) }),
            RemoveItem = i => c.States.RemoveAt(i)
        };

        yield return new ListField
        {
            Label = "Transitions",
            Count = () => c.Transitions.Count,
            ItemHeader = i => $"{(c.Transitions[i].From == "" ? "(Any)" : c.Transitions[i].From)} → {c.Transitions[i].To}",
            ItemFields = i => TransitionFields(i, fromNames, stateNames, paramNames),
            AddItem = () => c.Transitions.Add(new AnimatorTransition { To = stateNames.Length > 0 ? stateNames[0] : "" }),
            RemoveItem = i => c.Transitions.RemoveAt(i)
        };

        foreach (var problem in c.Validate())
            yield return new TextField { Label = "⚠", Get = () => problem };

        if (legacyDefs.Count > 0 || legacyLines.Count > 0)
        {
            yield return new TextField
            {
                Label = "Legacy",
                Get = () => $"{legacyDefs.Count} animation(s) et {legacyLines.Count} transition(s) de l'ancien format"
            };
            yield return new ButtonField
            {
                Label = "Convert legacy animations to .kanim",
                Action = ConvertLegacy,
                Tooltip = "Écrit Assets/Animations/<nom>.kanim pour chaque animation. Les anciennes transitions sont supprimées (listées dans la Console)."
            };
        }
    }

    static string UniqueName(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing);
        if (!set.Contains(baseName)) return baseName;
        for (int i = 1; ; i++)
            if (!set.Contains($"{baseName}{i}")) return $"{baseName}{i}";
    }

    IEnumerable<InspectorField> ParameterFields(int i)
    {
        var p = Controller.Parameters[i];
        yield return new StringField { Label = "Name", Get = () => p.Name, Set = v => p.Name = v };
        yield return new EnumField
        {
            Label = "Type",
            Names = ParameterTypeNames,
            Get = () => (int)p.Type,
            Set = idx => p.Type = (ParameterType)idx
        };
        switch (p.Type)
        {
            case ParameterType.Bool:
                yield return new BoolField { Label = "Default", Get = () => p.Default >= 0.5f, Set = v => p.Default = v ? 1f : 0f };
                break;
            case ParameterType.Float:
                yield return new FloatField { Label = "Default", Get = () => p.Default, Set = v => p.Default = v };
                break;
        }
    }

    IEnumerable<InspectorField> StateFields(int i)
    {
        var s = Controller.States[i];
        yield return new StringField { Label = "Name", Get = () => s.Name, Set = v => s.Name = v };
        yield return new AssetPathField
        {
            Label = "Clip",
            Extensions = [AnimationClip.Extension],
            Get = () => s.ClipPath,
            Set = v => { s.ClipPath = v; bindingCache.Clear(); }
        };
        yield return new FloatField { Label = "Speed", Get = () => s.Speed, Set = v => s.Speed = v, Value = 0.05f, Min = 0f, Max = 10f };
    }

    IEnumerable<InspectorField> TransitionFields(int i, string[] fromNames, string[] stateNames, string[] paramNames)
    {
        var t = Controller.Transitions[i];

        yield return new EnumField
        {
            Label = "From",
            Names = fromNames,
            Get = () => t.From == "" ? 0 : Math.Max(0, Array.IndexOf(fromNames, t.From)),
            Set = idx => t.From = idx <= 0 ? "" : fromNames[idx]
        };
        yield return new EnumField
        {
            Label = "To",
            Names = stateNames.Length > 0 ? stateNames : new[] { "(none)" },
            Get = () => Math.Max(0, Array.IndexOf(stateNames, t.To)),
            Set = idx => { if (idx >= 0 && idx < stateNames.Length) t.To = stateNames[idx]; }
        };
        yield return new BoolField { Label = "Has Exit Time", Get = () => t.HasExitTime, Set = v => t.HasExitTime = v };
        if (t.HasExitTime)
            yield return new FloatField { Label = "Exit Time", Get = () => t.ExitTime, Set = v => t.ExitTime = v, Value = 0.05f, Min = 0f, Max = 10f };

        yield return new ListField
        {
            Label = $"Conditions##tr{i}",
            Count = () => t.Conditions.Count,
            ItemHeader = j => ConditionText(t.Conditions[j]),
            ItemFields = j => ConditionFields(t.Conditions[j], paramNames),
            AddItem = () => t.Conditions.Add(new AnimatorCondition { Parameter = paramNames.Length > 0 ? paramNames[0] : "" }),
            RemoveItem = j => t.Conditions.RemoveAt(j)
        };
    }

    static string ConditionText(AnimatorCondition c) => c.Op switch
    {
        ConditionOp.IsTrue => c.Parameter,
        ConditionOp.IsFalse => $"not {c.Parameter}",
        ConditionOp.Greater => $"{c.Parameter} > {c.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
        ConditionOp.Less => $"{c.Parameter} < {c.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
        _ => $"trigger {c.Parameter}"
    };

    static IEnumerable<InspectorField> ConditionFields(AnimatorCondition c, string[] paramNames)
    {
        yield return new EnumField
        {
            Label = "Parameter",
            Names = paramNames.Length > 0 ? paramNames : new[] { "(none)" },
            Get = () => Math.Max(0, Array.IndexOf(paramNames, c.Parameter)),
            Set = idx => { if (idx >= 0 && idx < paramNames.Length) c.Parameter = paramNames[idx]; }
        };
        yield return new EnumField
        {
            Label = "Operator",
            Names = ConditionOpNames,
            Get = () => (int)c.Op,
            Set = idx => c.Op = (ConditionOp)idx
        };
        if (c.Op is ConditionOp.Greater or ConditionOp.Less)
            yield return new FloatField { Label = "Value", Get = () => c.Value, Set = v => c.Value = v, Value = 0.05f };
    }
}