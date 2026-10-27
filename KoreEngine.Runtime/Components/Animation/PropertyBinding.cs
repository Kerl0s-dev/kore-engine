using System.Reflection;

namespace KoreEngine;

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
        if (leafType == typeof(byte)) return (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
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