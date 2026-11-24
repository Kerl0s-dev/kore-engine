using System.Diagnostics;

namespace KoreEngine;

/// <summary>
/// Valeurs de paramètres de shader propres à un usage d'un matériau (surcharges des défauts du .kmat),
/// encodées en texte "Nom=v1/v2|Nom2=v" pour être sérialisées dans un simple champ string.
/// Sert aux effets de post-processing ; fournit les champs d'Inspector et les valeurs prêtes pour le GPU.
///
/// Paramètres réservés, remplis par le moteur et masqués dans l'Inspector :
///   Time        (float)  secondes écoulées
///   Resolution  (float2) taille en pixels de la cible de rendu
/// </summary>
public sealed class MaterialParamSet
{
    static readonly Stopwatch clock = Stopwatch.StartNew();

    string text = "";
    Dictionary<string, float[]> overrides = new();
    Material? packedFor;
    int packedGeneration = -1;
    float[]? packed;
    float[]? frame;
    bool hasBuiltIns;

    /// <summary>Appelé quand l'utilisateur modifie une valeur : le propriétaire y recopie le texte à sérialiser.</summary>
    public Action<string>? Changed;

    public string Text
    {
        get => text;
        set
        {
            value ??= "";
            if (value == text) return;
            text = value;
            overrides = ShaderParams.ParseOverrides(text);
            packed = null;
        }
    }

    public static bool IsBuiltIn(ShaderParam p) => p.Name is "Time" or "Resolution";

    float[] GetValue(Material material, ShaderParam param)
        => overrides.TryGetValue(param.Name, out var v) ? (float[])v.Clone() : material.GetDefault(param);

    void SetValue(ShaderParam param, float[] value)
    {
        overrides[param.Name] = value;
        text = ShaderParams.FormatOverrides(overrides);
        packed = null;
        Changed?.Invoke(text);
    }

    void Reset()
    {
        text = "";
        overrides = new();
        packed = null;
        Changed?.Invoke(text);
    }

    /// <summary>Valeurs GPU du matériau + surcharges (null si le shader n'a pas de paramètres). Le tableau retourné est réutilisé : à consommer tout de suite.</summary>
    public float[]? BuildUniforms(Material material, float width, float height)
    {
        var ps = material.Params;
        if (ps.Count == 0) return null;

        if (packed == null || packedFor != material || packedGeneration != ShaderCache.Generation)
        {
            packed = ShaderParams.Pack(ps, p => overrides.TryGetValue(p.Name, out var v) ? v : material.GetDefault(p));
            packedFor = material;
            packedGeneration = ShaderCache.Generation;
            hasBuiltIns = ps.Any(IsBuiltIn);
        }

        if (!hasBuiltIns) return packed;

        if (frame == null || frame.Length != packed.Length) frame = new float[packed.Length];
        Array.Copy(packed, frame, packed.Length);

        foreach (var p in ps)
        {
            if (p.Name == "Time")
                frame[p.Slot * 4] = (float)clock.Elapsed.TotalSeconds;
            else if (p.Name == "Resolution")
            {
                frame[p.Slot * 4] = width;
                frame[p.Slot * 4 + 1] = height;
            }
        }
        return frame;
    }

    public IEnumerable<InspectorField> CreateFields(Material material)
    {
        foreach (var param in material.Params)
        {
            if (IsBuiltIn(param)) continue;
            foreach (var field in FieldsFor(material, param))
                yield return field;
        }

        if (!string.IsNullOrEmpty(text))
            yield return new ButtonField { Label = "Reset params", Action = Reset, Tooltip = "Revient aux valeurs du matériau" };
    }

    IEnumerable<InspectorField> FieldsFor(Material material, ShaderParam param)
    {
        string label = string.IsNullOrEmpty(param.DisplayName) ? param.Name : param.DisplayName;

        switch (param.Type)
        {
            case ShaderParamType.Float:
                yield return new FloatField
                {
                    Label = label,
                    Get = () => GetValue(material, param)[0],
                    Set = v => { var a = GetValue(material, param); a[0] = v; SetValue(param, a); },
                    Min = param.HasRange ? param.Min : 0f,
                    Max = param.HasRange ? param.Max : 0f,
                    Slider = param.HasRange
                };
                break;

            case ShaderParamType.Float2:
                yield return new Vector2Field
                {
                    Label = label,
                    Get = () => { var a = GetValue(material, param); return new Vector2(a[0], a[1]); },
                    Set = v => SetValue(param, new[] { v.X, v.Y, 0f, 0f })
                };
                break;

            case ShaderParamType.Float3:
            case ShaderParamType.Float4:
                string[] axes = { "x", "y", "z", "w" };
                for (int i = 0; i < param.Components; i++)
                {
                    int index = i;
                    yield return new FloatField
                    {
                        Label = label + "." + axes[index],
                        Get = () => GetValue(material, param)[index],
                        Set = v => { var a = GetValue(material, param); a[index] = v; SetValue(param, a); }
                    };
                }
                break;

            case ShaderParamType.Color:
                yield return new ColorField
                {
                    Label = label,
                    Get = () => { var a = GetValue(material, param); return new Color((int)a[0], (int)a[1], (int)a[2], (int)a[3]); },
                    Set = c => SetValue(param, new float[] { c.R, c.G, c.B, c.A })
                };
                break;
        }
    }
}