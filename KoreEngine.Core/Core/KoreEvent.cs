using System.Globalization;
using System.Reflection;

namespace KoreEngine;

public class PersistentListener
{
    public string TargetObject = "";
    public string ComponentType = "";
    public string MethodName = "";
    public string Argument = "";   // encodé en string, parsé selon le type du paramètre

    public static IEnumerable<MethodInfo> GetCallableMethods(Type t) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
         .Where(m => !m.IsSpecialName
                  && m.DeclaringType != typeof(object)
                  && m.ReturnType == typeof(void)
                  && IsSupportedParams(m.GetParameters()))
         .DistinctBy(m => m.Name);

    static bool IsSupportedParams(ParameterInfo[] ps) =>
        ps.Length == 0 ||
        (ps.Length == 1 && (ps[0].ParameterType == typeof(string)
                         || ps[0].ParameterType == typeof(int)
                         || ps[0].ParameterType == typeof(float)
                         || ps[0].ParameterType == typeof(bool)));

    public GameObject? ResolveObject() =>
        SceneManager.Current?.AllObjects.FirstOrDefault(o => o.Name == TargetObject);

    public string ToLine() =>
        $"{Uri.EscapeDataString(TargetObject)}|{Uri.EscapeDataString(ComponentType)}|" +
        $"{Uri.EscapeDataString(MethodName)}|{Uri.EscapeDataString(Argument)}";

    public static PersistentListener? FromLine(string s)
    {
        var p = s.Split('|');
        if (p.Length < 4) return null;
        return new PersistentListener
        {
            TargetObject = Uri.UnescapeDataString(p[0]),
            ComponentType = Uri.UnescapeDataString(p[1]),
            MethodName = Uri.UnescapeDataString(p[2]),
            Argument = Uri.UnescapeDataString(p[3]),
        };
    }

    public void Invoke()
    {
        var comp = ResolveObject()?.Components
            .FirstOrDefault(c => c.GetType().Name == ComponentType);
        if (comp == null) return;

        var m = GetCallableMethods(comp.GetType()).FirstOrDefault(x => x.Name == MethodName);
        if (m == null) return;

        var ps = m.GetParameters();
        object?[] args = ps.Length == 0
            ? Array.Empty<object>()
            : new[] { Parse(ps[0].ParameterType, Argument) };
        m.Invoke(comp, args);
    }

    public static object? Parse(Type t, string s)
    {
        if (t == typeof(string)) return s;
        if (t == typeof(int)) return int.TryParse(s, out var i) ? i : 0;
        if (t == typeof(float)) return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
        if (t == typeof(bool)) return bool.TryParse(s, out var b) && b;
        return null;
    }
}

public class KoreEvent
{
    public List<PersistentListener> Listeners = new();

    // Abonnés par code, non sérialisés (n'est pas un champ → ignoré par la réflexion)
    event Action? runtime;
    public void AddListener(Action a) => runtime += a;
    public void RemoveListener(Action a) => runtime -= a;

    public void Invoke()
    {
        foreach (var l in Listeners) l.Invoke();
        runtime?.Invoke();
    }

    const string KeySuffix = ".Listener";

    public IEnumerable<string> Serialize(string fieldName)
    {
        foreach (var l in Listeners)
            yield return $"{fieldName}{KeySuffix}: {l.ToLine()}";
    }

    public void Deserialize(string fieldName, IEnumerable<string> lines)
    {
        Listeners.Clear();
        string key = fieldName + KeySuffix;
        foreach (var line in lines)
        {
            int colon = line.IndexOf(':');
            if (colon < 0 || line[..colon].Trim() != key) continue;
            var l = PersistentListener.FromLine(line[(colon + 1)..].Trim());
            if (l != null) Listeners.Add(l);
        }
    }
}