Shader "Post/CRT"

Properties
{
    Scanlines ("Scanline intensity", Range(0, 1)) = 0.35
    LineSize ("Line size in pixels", Range(1, 8)) = 3
    Curvature ("Curvature", Range(0, 0.3)) = 0.08
    Resolution ("Resolution", Vector2) = (0, 0)
}

HLSLPROGRAM
// Resolution est rempli par le moteur (taille de la cible en pixels).
float4 Fragment(float2 uv, float4 color)
{
    // Courbure de l'écran (distorsion en barillet)
    float2 p = uv * 2.0 - 1.0;
    p *= 1.0 + dot(p, p) * Curvature;
    float2 cuv = p * 0.5 + 0.5;

    if (cuv.x < 0.0 || cuv.x > 1.0 || cuv.y < 0.0 || cuv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    float4 src = SampleMainTex(cuv) * color;

    float scan = 0.5 + 0.5 * sin(cuv.y * Resolution.y / LineSize * 6.2831853);
    src.rgb *= 1.0 - Scanlines * scan;
    return src;
}
ENDHLSL
