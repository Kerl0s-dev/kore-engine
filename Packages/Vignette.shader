Shader "Post/Vignette"

Properties
{
    Intensity ("Intensity", Range(0, 1)) = 0.6
    Radius ("Radius", Range(0, 1.5)) = 0.8
    Softness ("Softness", Range(0.05, 1)) = 0.5
}

HLSLPROGRAM
float4 Fragment(float2 uv, float4 color)
{
    float4 src = SampleMainTex(uv) * color;
    float dist = length(uv - 0.5) * 1.4142;
    float v = smoothstep(Radius, Radius - Softness, dist);
    src.rgb *= lerp(1.0, v, Intensity);
    return src;
}
ENDHLSL
