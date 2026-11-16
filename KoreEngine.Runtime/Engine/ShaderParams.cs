using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KoreEngine;

public enum ShaderParamType { Float, Float2, Float3, Float4, Color }

/// <summary>Paramètre de shader déclaré par un commentaire "// @param" dans le .hlsl.</summary>
public sealed class ShaderParam
{
    public string Name = "";
    public ShaderParamType Type;

    /// <summary>Index du float4 occupé dans le cbuffer généré.</summary>
    public int Slot;

    /// <summary>Valeur par défaut (couleur : 0-255, comme Color). Toujours 4 éléments.</summary>
    public float[] Default = new float[4];

    public bool HasRange;
    public float Min, Max;

    public int Components => Type switch
    {
        ShaderParamType.Float => 1,
        ShaderParamType.Float2 => 2,
        ShaderParamType.Float3 => 3,
        _ => 4
    };
}

/// <summary>
/// Déclaration, lecture et mise en forme des paramètres de shader.
/// </summary>
public static class ShaderParams
{
    public const int MaxParams = 32;

    static readonly Regex ParamLine = new(
        @"^[ \t]*//[ \t]*@param[ \t]+(?<type>\w+)[ \t]+(?<name>[A-Za-z_]\w*)[ \t]*(?:=[ \t]*(?<def>[^\[\r\n]*?))?[ \t]*(?:\[[ \t]*(?<min>[-+0-9.eE]+)[ \t]*,[ \t]*(?<max>[-+0-9.eE]+)[ \t]*\])?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    static readonly char[] Separators = { ' ', '\t', ',', '/' };

    public static List<ShaderParam> Parse(string source, string shaderName)
    {
        var list = new List<ShaderParam>();

        foreach (Match m in ParamLine.Matches(source))
        {
            string typeName = m.Groups["type"].Value.ToLowerInvariant();
            string name = m.Groups["name"].Value;

            ShaderParamType type;
            switch (typeName)
            {
                case "float": type = ShaderParamType.Float; break;
                case "float2": type = ShaderParamType.Float2; break;
                case "float3": type = ShaderParamType.Float3; break;
                case "float4": type = ShaderParamType.Float4; break;
                case "color": type = ShaderParamType.Color; break;
                default:
                    Logger.Warning($"[Shader] '{shaderName}' : type de paramètre '{typeName}' non supporté pour '{name}' (float, float2, float3, float4, color).");
                    continue;
            }

            if (list.Count >= MaxParams)
            {
                Logger.Warning($"[Shader] '{shaderName}' : {MaxParams} paramètres maximum, '{name}' ignoré.");
                continue;
            }

            if (list.Any(p => p.Name == name))
            {
                Logger.Warning($"[Shader] '{shaderName}' : paramètre '{name}' déclaré deux fois, le second est ignoré.");
                continue;
            }

            var param = new ShaderParam { Name = name, Type = type, Slot = list.Count };

            if (type == ShaderParamType.Color)
                param.Default = new[] { 255f, 255f, 255f, 255f };

            if (m.Groups["def"].Success && m.Groups["def"].Value.Trim().Length > 0)
                FillValue(param.Default, m.Groups["def"].Value, param.Components);

            if (m.Groups["min"].Success
                && float.TryParse(m.Groups["min"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float min)
                && float.TryParse(m.Groups["max"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float max))
            {
                param.HasRange = true;
                param.Min = min;
                param.Max = max;
            }

            list.Add(param);
        }

        return list;
    }

    /// <summary>Code HLSL à placer AVANT le source du shader (cbuffer + #define), puis "#line 1" pour garder les numéros de ligne.</summary>
    public static string BuildPrelude(IReadOnlyList<ShaderParam> parameters)
    {
        var sb = new StringBuilder();
        sb.Append("cbuffer KoreParams : register(b0, space3) { float4 _kp[").Append(parameters.Count).Append("]; };\n");

        foreach (var p in parameters)
        {
            string swizzle = p.Type switch
            {
                ShaderParamType.Float => ".x",
                ShaderParamType.Float2 => ".xy",
                ShaderParamType.Float3 => ".xyz",
                _ => ""
            };
            sb.Append("#define ").Append(p.Name).Append(" _kp[").Append(p.Slot).Append(']').Append(swizzle).Append('\n');
        }

        sb.Append("#line 1\n");
        return sb.ToString();
    }

    /// <summary>Remplit "target" avec les nombres de "text" (séparés par espaces, virgules ou '/'), jusqu'à "count" valeurs.</summary>
    public static void FillValue(float[] target, string text, int count)
    {
        var parts = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < System.Math.Min(count, parts.Length); i++)
            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                target[i] = v;
    }

    /// <summary>Valeurs prêtes pour le GPU : un float4 par paramètre (les couleurs passent de 0-255 à 0-1).</summary>
    public static float[] Pack(IReadOnlyList<ShaderParam> parameters, Func<ShaderParam, float[]> valueOf)
    {
        var data = new float[parameters.Count * 4];
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var v = valueOf(p);
            for (int k = 0; k < System.Math.Min(4, v.Length); k++)
                data[p.Slot * 4 + k] = p.Type == ShaderParamType.Color ? v[k] / 255f : v[k];
        }
        return data;
    }

    // ---- Surcharges par objet : "Name=v1/v2|Name2=v" (aucun espace, ni ':' en tête, ni ';') -------------

    public static Dictionary<string, float[]> ParseOverrides(string text)
    {
        var result = new Dictionary<string, float[]>();
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var entry in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = entry.IndexOf('=');
            if (eq <= 0) continue;

            var values = new float[4];
            FillValue(values, entry[(eq + 1)..], 4);
            result[entry[..eq]] = values;
        }
        return result;
    }

    public static string FormatOverrides(Dictionary<string, float[]> overrides)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join("|", overrides.Select(kv =>
            kv.Key + "=" + string.Join("/", kv.Value.Select(v => v.ToString("0.#####", inv)))));
    }
}