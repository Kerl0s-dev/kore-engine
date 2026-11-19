Shader "Post/ChromaticAberration"

Properties
{
    Strength ("Strength", Range(0, 0.05)) = 0.006
    Radial ("Radial amount", Range(0, 1)) = 1
    Angle ("Angle", Range(0, 360)) = 0
}

HLSLPROGRAM
// Décale les canaux rouge et bleu en sens opposés.
// Radial = 1 : le décalage grandit vers les bords (effet d'objectif) ; Radial = 0 : décalage uniforme dans la direction Angle.
float4 Fragment(float2 uv, float4 color)
{
    float a = radians(Angle);
    float2 uniformDir = float2(cos(a), sin(a));
    float2 radialDir = (uv - 0.5) * 2.0;
    float2 offset = lerp(uniformDir, radialDir, Radial) * Strength;

    float4 center = SampleMainTex(uv);
    float r = SampleMainTex(uv + offset).r;
    float b = SampleMainTex(uv - offset).b;
    return float4(r, center.g, b, center.a) * color;
}
ENDHLSL
