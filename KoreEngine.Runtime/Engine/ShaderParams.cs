using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KoreEngine;

public enum ShaderParamType { Float, Float2, Float3, Float4, Color }

public sealed class ShaderParam
{
    public string Name = "";
    public string DisplayName = "";
    public ShaderParamType Type;
    public int Slot;
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

public sealed class ShaderTextureParam
{
    public string Name = "";
    public string DisplayName = "";
    public int Slot;
    public string DefaultTexture = "white";
}

public sealed class ParsedShader
{
    public string Name = "";
    public List<ShaderParam> Params { get; init; } = new();
    public List<ShaderTextureParam> Textures { get; init; } = new();
    public string FinalHlslSource = "";
}

public static class ShaderTemplate
{
    public const int MaxParams = 32;

    static readonly Regex HeaderRegex = new(
        @"Shader\s+""(?<name>[^""]+)""",
        RegexOptions.Compiled);

    static readonly Regex PropLineRegex = new(
        @"^[ \t]*(?<name>[A-Za-z_]\w*)[ \t]*(?:\([ \t]*""(?<label>[^""]*)""[ \t]*,[ \t]*(?<type>(?:[^()]+|\([^()]*\))+)\)|:[ \t]*(?<typeAlt>[^\r\n=]+))[ \t]*(?:=[ \t]*(?<def>[^\r\n]*?))?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    static readonly Regex RangeRegex = new(
        @"^Range\s*\(\s*(?<min>[-+0-9.eE]+)\s*,\s*(?<max>[-+0-9.eE]+)\s*\)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedShader Compile(string source, string fallbackName)
    {
        var shader = new ParsedShader { Name = fallbackName };

        var headerMatch = HeaderRegex.Match(source);
        if (headerMatch.Success)
            shader.Name = headerMatch.Groups["name"].Value;

        string propsBlock = ExtractBlock(source, "Properties");
        (string hlslCode, int hlslStartLine) = ExtractHlslBlock(source);

        ParseProperties(propsBlock, shader);
        shader.FinalHlslSource = BuildFragmentHlsl(hlslCode, hlslStartLine, shader);
        return shader;
    }

    private static void ParseProperties(string block, ParsedShader shader)
    {
        int texSlot = 1; // t0 / s0 réservés à u_texture / u_sampler de SDL_Renderer

        foreach (Match m in PropLineRegex.Matches(block))
        {
            string name = m.Groups["name"].Value;
            string label = m.Groups["label"].Success ? m.Groups["label"].Value : name;
            string rawType = (m.Groups["type"].Success ? m.Groups["type"].Value : m.Groups["typeAlt"].Value).Trim();
            string defVal = m.Groups["def"].Success ? m.Groups["def"].Value.Trim() : "";

            if (rawType.Equals("2D", StringComparison.OrdinalIgnoreCase) ||
                rawType.Equals("Texture2D", StringComparison.OrdinalIgnoreCase))
            {
                shader.Textures.Add(new ShaderTextureParam
                {
                    Name = name,
                    DisplayName = label,
                    Slot = texSlot++,
                    DefaultTexture = string.IsNullOrEmpty(defVal) ? "white" : defVal.Trim('"')
                });
                continue;
            }

            if (shader.Params.Count >= MaxParams)
            {
                Logger.Warning($"[Shader] '{shader.Name}' : {MaxParams} paramètres maximum, '{name}' ignoré.");
                continue;
            }

            if (shader.Params.Any(p => p.Name == name))
            {
                Logger.Warning($"[Shader] '{shader.Name}' : paramètre '{name}' déclaré deux fois.");
                continue;
            }

            var param = new ShaderParam
            {
                Name = name,
                DisplayName = label,
                Slot = shader.Params.Count
            };

            var rangeMatch = RangeRegex.Match(rawType);
            if (rangeMatch.Success)
            {
                param.Type = ShaderParamType.Float;
                param.HasRange = true;
                param.Min = float.Parse(rangeMatch.Groups["min"].Value, CultureInfo.InvariantCulture);
                param.Max = float.Parse(rangeMatch.Groups["max"].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                switch (rawType.ToLowerInvariant())
                {
                    case "float": param.Type = ShaderParamType.Float; break;
                    case "float2":
                    case "vector2": param.Type = ShaderParamType.Float2; break;
                    case "float3":
                    case "vector3": param.Type = ShaderParamType.Float3; break;
                    case "float4":
                    case "vector": param.Type = ShaderParamType.Float4; break;
                    case "color":
                        param.Type = ShaderParamType.Color;
                        param.Default = new[] { 255f, 255f, 255f, 255f };
                        break;
                    default:
                        Logger.Warning($"[Shader] '{shader.Name}' : type '{rawType}' non supporté pour '{name}'.");
                        continue;
                }
            }

            if (!string.IsNullOrEmpty(defVal))
                ShaderParams.FillValue(param.Default, defVal, param.Components);

            shader.Params.Add(param);
        }
    }

    private static string BuildFragmentHlsl(string userHlsl, int startLine, ParsedShader shader)
    {
        var sb = new StringBuilder();

        // Convention SDL_Renderer
        sb.Append("""
            Texture2D u_texture : register(t0, space2);
            SamplerState u_sampler : register(s0, space2);

            float4 SampleMainTex(float2 uv)
            {
                return u_texture.Sample(u_sampler, uv);
            }

            """);

        foreach (var tex in shader.Textures)
        {
            sb.Append($"Texture2D {tex.Name} : register(t{tex.Slot}, space2);\n");
            sb.Append($"SamplerState {tex.Name}_Sampler : register(s{tex.Slot}, space2);\n");
        }
        if (shader.Textures.Count > 0)
            sb.Append("#define SampleTex(tex, uv) tex.Sample(tex##_Sampler, uv)\n\n");

        if (shader.Params.Count > 0)
        {
            sb.Append($"cbuffer KoreParams : register(b0, space3) {{ float4 _kp[{shader.Params.Count}]; }};\n");
            foreach (var p in shader.Params)
            {
                string swizzle = p.Type switch
                {
                    ShaderParamType.Float => ".x",
                    ShaderParamType.Float2 => ".xy",
                    ShaderParamType.Float3 => ".xyz",
                    _ => ""
                };
                sb.Append($"#define {p.Name} _kp[{p.Slot}]{swizzle}\n");
            }
            sb.Append('\n');
        }

        sb.Append($"#line {startLine}\n");
        sb.Append(userHlsl);
        sb.Append("\n\n");

        // Point d'entrée "main" exigé par SDL_GPURenderState
        if (!Regex.IsMatch(userHlsl, @"\bfloat4\s+main\s*\("))
        {
            sb.Append("""
                float4 main(float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
                {
                    return Fragment(uv, color);
                }
                """);
        }

        return sb.ToString();
    }

    private static string ExtractBlock(string source, string keyword)
    {
        var match = Regex.Match(source, $@"\b{keyword}\s*\{{", RegexOptions.IgnoreCase);
        if (!match.Success) return "";

        int depth = 1;
        int i = match.Index + match.Length;
        int start = i;

        while (i < source.Length && depth > 0)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            i++;
        }

        return depth == 0 ? source[start..(i - 1)] : "";
    }

    private static (string Code, int StartLine) ExtractHlslBlock(string source)
    {
        var startMatch = Regex.Match(source, @"\bHLSLPROGRAM\b");
        var endMatch = Regex.Match(source, @"\bENDHLSL\b");

        if (!startMatch.Success || !endMatch.Success || endMatch.Index <= startMatch.Index)
            return (source, 1);

        int codeStart = startMatch.Index + startMatch.Length;
        int lineNumber = source[..codeStart].Count(c => c == '\n') + 1;

        return (source[codeStart..endMatch.Index], lineNumber);
    }
}

public static class ShaderParams
{
    public const int MaxParams = ShaderTemplate.MaxParams;
    static readonly char[] Separators = { ' ', '\t', ',', '/', '(', ')' };

    public static List<ShaderParam> Parse(string source, string shaderName)
        => ShaderTemplate.Compile(source, shaderName).Params;

    public static void FillValue(float[] target, string text, int count)
    {
        var parts = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < System.Math.Min(count, parts.Length); i++)
            if (float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                target[i] = v;
    }

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