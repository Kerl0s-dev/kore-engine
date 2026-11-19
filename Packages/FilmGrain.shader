Shader "Post/FilmGrain"

Properties
{
    Amount ("Amount", Range(0, 0.5)) = 0.08
    Speed ("Speed", Range(0, 30)) = 12
    Resolution ("Resolution", Vector2) = (0, 0)
    Time ("Time", Float) = 0
}

HLSLPROGRAM
// Resolution et Time sont remplis par le moteur.
float4 Fragment(float2 uv, float4 color)
{
    float4 src = SampleMainTex(uv) * color;
    float2 seed = uv * Resolution + floor(Time * Speed);
    float n = frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
    src.rgb += (n - 0.5) * Amount;
    return src;
}
ENDHLSL
