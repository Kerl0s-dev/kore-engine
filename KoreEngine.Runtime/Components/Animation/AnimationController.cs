using System.Globalization;

namespace KoreEngine;

public enum ParameterType { Bool, Float, Trigger }

public class AnimatorParameter
{
    public string Name = "param";
    public ParameterType Type = ParameterType.Bool;
    public float Default;                 // Bool : 0/1
}

public class AnimatorState
{
    public string Name = "State";
    public string ClipPath = "";          // "Assets/Anim/walk.kanim"
    public float Speed = 1f;
}

public enum ConditionOp { IsTrue, IsFalse, Greater, Less, Triggered }

public class AnimatorCondition
{
    public string Parameter = "";
    public ConditionOp Op = ConditionOp.IsTrue;
    public float Value;                   // seuil pour Greater / Less
}

public class AnimatorTransition
{
    public string From = "";              // "" = depuis n'importe quel état
    public string To = "";
    public bool HasExitTime;              // attendre une fraction du clip avant de partir
    public float ExitTime = 1f;           // temps normalisé depuis l'entrée dans l'état (1 = clip terminé)
    public List<AnimatorCondition> Conditions = new();
}

/// <summary>
/// Machine à états de l'Animator : paramètres (bool, float, trigger), états, transitions et temps de lecture.
/// Classe pure (aucune dépendance au moteur) : les clips sont fournis par <see cref="ClipProvider"/>.
/// </summary>
public sealed class AnimatorController
{
    public List<AnimatorParameter> Parameters = new();
    public List<AnimatorState> States = new();
    public List<AnimatorTransition> Transitions = new();
    public string DefaultState = "";

    /// <summary>Renvoie le clip d'un état (ou null s'il est introuvable).</summary>
    public Func<AnimatorState, AnimationClip?> ClipProvider = _ => null;

    readonly Dictionary<string, float> values = new();
    readonly HashSet<string> triggers = new();

    AnimatorState? current;
    AnimationClip? currentClip;
    float elapsed;

    public AnimatorState? CurrentState => current;
    public AnimationClip? CurrentClip => currentClip;

    /// <summary>Temps de lecture dans le clip courant, en secondes (bouclé si le clip boucle).</summary>
    public float Time { get; private set; }

    /// <summary>Vrai quand un clip sans boucle est arrivé à sa fin.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Temps normalisé écoulé depuis l'entrée dans l'état (non bouclé).</summary>
    public float NormalizedTime => currentClip == null ? 0f : elapsed / currentClip.EffectiveDuration;

    // ------------------------------------------------------------------
    // Paramètres
    // ------------------------------------------------------------------

    public void ResetParameters()
    {
        values.Clear();
        triggers.Clear();
        foreach (var p in Parameters)
        {
            if (p.Type == ParameterType.Trigger) { if (p.Default >= 0.5f) triggers.Add(p.Name); }
            else values[p.Name] = p.Default;
        }
    }

    public void SetBool(string name, bool v) => values[name] = v ? 1f : 0f;
    public void SetFloat(string name, float v) => values[name] = v;
    public void SetTrigger(string name) => triggers.Add(name);
    public void ResetTrigger(string name) => triggers.Remove(name);
    public bool GetBool(string name) => values.TryGetValue(name, out float v) && v >= 0.5f;
    public float GetFloat(string name) => values.TryGetValue(name, out float v) ? v : 0f;
    public bool IsTriggerSet(string name) => triggers.Contains(name);

    // ------------------------------------------------------------------
    // Lecture
    // ------------------------------------------------------------------

    public AnimatorState? FindState(string name) => States.Find(s => s.Name == name);

    /// <summary>Passe immédiatement dans un état (temps remis à zéro). Renvoie false si l'état n'existe pas.</summary>
    public bool Play(string stateName)
    {
        var s = FindState(stateName);
        if (s == null) return false;
        Enter(s);
        return true;
    }

    void Enter(AnimatorState s)
    {
        current = s;
        currentClip = ClipProvider(s);
        elapsed = 0f;
        Time = 0f;
        IsFinished = false;
    }

    /// <summary>Démarre dans l'état par défaut (ou le premier état). Les paramètres sont réinitialisés.</summary>
    public void Start()
    {
        ResetParameters();
        current = null;
        currentClip = null;
        var s = FindState(DefaultState) ?? (States.Count > 0 ? States[0] : null);
        if (s != null) Enter(s);
    }

    /// <summary>Évalue les transitions puis avance le temps. Après l'appel, <see cref="Time"/> est à appliquer sur <see cref="CurrentClip"/>.</summary>
    public void Update(float dt)
    {
        if (current == null) return;

        // 1. Transitions (la première satisfaite l'emporte)
        foreach (var t in Transitions)
        {
            if (t.From != "" && t.From != current.Name) continue;
            if (t.To == current.Name) continue;
            var target = FindState(t.To);
            if (target == null) continue;
            if (!IsSatisfied(t)) continue;

            ConsumeTriggers(t);
            Enter(target);
            break;
        }

        // 2. Avance du temps
        if (currentClip == null) return;
        float duration = currentClip.EffectiveDuration;
        elapsed += dt * current.Speed;

        if (currentClip.Loop)
        {
            Time = elapsed % duration;
            IsFinished = false;
        }
        else
        {
            Time = MathF.Min(elapsed, duration);
            IsFinished = elapsed >= duration;
        }
    }

    bool IsSatisfied(AnimatorTransition t)
    {
        if (t.HasExitTime && NormalizedTime < t.ExitTime) return false;

        foreach (var c in t.Conditions)
        {
            switch (c.Op)
            {
                case ConditionOp.IsTrue: if (!GetBool(c.Parameter)) return false; break;
                case ConditionOp.IsFalse: if (GetBool(c.Parameter)) return false; break;
                case ConditionOp.Greater: if (!(GetFloat(c.Parameter) > c.Value)) return false; break;
                case ConditionOp.Less: if (!(GetFloat(c.Parameter) < c.Value)) return false; break;
                case ConditionOp.Triggered: if (!triggers.Contains(c.Parameter)) return false; break;
            }
        }
        return true;
    }

    void ConsumeTriggers(AnimatorTransition t)
    {
        foreach (var c in t.Conditions)
            if (c.Op == ConditionOp.Triggered) triggers.Remove(c.Parameter);
    }

    // ------------------------------------------------------------------
    // Vérification (affichée dans l'Inspector)
    // ------------------------------------------------------------------

    /// <summary>Liste les incohérences : états ou paramètres inconnus, doublons.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        var stateNames = new HashSet<string>();
        foreach (var s in States)
            if (!stateNames.Add(s.Name)) problems.Add($"état en double : {s.Name}");

        if (States.Count > 0 && DefaultState != "" && !stateNames.Contains(DefaultState))
            problems.Add($"état par défaut inconnu : {DefaultState}");

        var paramTypes = new Dictionary<string, ParameterType>();
        foreach (var p in Parameters)
            if (!paramTypes.TryAdd(p.Name, p.Type)) problems.Add($"paramètre en double : {p.Name}");

        for (int i = 0; i < Transitions.Count; i++)
        {
            var t = Transitions[i];
            if (t.From != "" && !stateNames.Contains(t.From)) problems.Add($"transition {i} : état source inconnu « {t.From} »");
            if (!stateNames.Contains(t.To)) problems.Add($"transition {i} : état cible inconnu « {t.To} »");
            foreach (var c in t.Conditions)
            {
                if (!paramTypes.TryGetValue(c.Parameter, out var type))
                {
                    problems.Add($"transition {i} : paramètre inconnu « {c.Parameter} »");
                    continue;
                }
                bool ok = c.Op switch
                {
                    ConditionOp.IsTrue or ConditionOp.IsFalse => type == ParameterType.Bool,
                    ConditionOp.Greater or ConditionOp.Less => type == ParameterType.Float,
                    ConditionOp.Triggered => type == ParameterType.Trigger,
                    _ => false
                };
                if (!ok) problems.Add($"transition {i} : « {c.Parameter} » ({type}) incompatible avec {c.Op}");
            }
        }
        return problems;
    }

    // ------------------------------------------------------------------
    // Sérialisation (lignes de scène)
    // ------------------------------------------------------------------

    /// <remarks>Les noms ne doivent contenir ni virgule, ni point-virgule, ni deux-points.</remarks>
    public IEnumerable<string> Serialize()
    {
        var inv = CultureInfo.InvariantCulture;
        yield return $"Default: {DefaultState}";
        foreach (var p in Parameters)
            yield return $"Param: {p.Name},{p.Type},{p.Default.ToString(inv)}";
        foreach (var s in States)
            yield return $"State: {s.Name},{s.ClipPath},{s.Speed.ToString(inv)}";
        foreach (var t in Transitions)
        {
            string conds = string.Join(';', t.Conditions.Select(c => $"{c.Parameter}:{c.Op}:{c.Value.ToString(inv)}"));
            yield return $"Transition: {t.From},{t.To},{(t.HasExitTime ? "true" : "false")},{t.ExitTime.ToString(inv)},{conds}";
        }
    }

    /// <summary>Renvoie les lignes non reconnues (compatibilité avec l'ancien format).</summary>
    public List<string> Deserialize(IEnumerable<string> lines)
    {
        var inv = CultureInfo.InvariantCulture;
        Parameters.Clear(); States.Clear(); Transitions.Clear(); DefaultState = "";
        var unknown = new List<string>();

        foreach (var line in lines)
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            var p = value.Split(',');

            try
            {
                switch (key)
                {
                    case "Default": DefaultState = value; break;

                    case "Param" when p.Length >= 3:
                        Parameters.Add(new AnimatorParameter
                        {
                            Name = p[0].Trim(),
                            Type = Enum.Parse<ParameterType>(p[1].Trim()),
                            Default = float.Parse(p[2], inv)
                        });
                        break;

                    case "State" when p.Length >= 3:
                        States.Add(new AnimatorState
                        {
                            Name = p[0].Trim(),
                            ClipPath = p[1].Trim(),
                            Speed = float.Parse(p[2], inv)
                        });
                        break;

                    case "Transition" when p.Length >= 4:
                        {
                            var t = new AnimatorTransition
                            {
                                From = p[0].Trim(),
                                To = p[1].Trim(),
                                HasExitTime = p[2].Trim() == "true",
                                ExitTime = float.Parse(p[3], inv)
                            };
                            if (p.Length >= 5 && p[4].Trim().Length > 0)
                            {
                                foreach (var cs in p[4].Split(';'))
                                {
                                    var cp = cs.Split(':');
                                    if (cp.Length < 3) continue;
                                    t.Conditions.Add(new AnimatorCondition
                                    {
                                        Parameter = cp[0].Trim(),
                                        Op = Enum.Parse<ConditionOp>(cp[1].Trim()),
                                        Value = float.Parse(cp[2], inv)
                                    });
                                }
                            }
                            Transitions.Add(t);
                            break;
                        }

                    default: unknown.Add(line); break;
                }
            }
            catch (FormatException) { unknown.Add(line); }
            catch (ArgumentException) { unknown.Add(line); }
        }
        return unknown;
    }
}