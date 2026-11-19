Shader "Post/Pixelate"

Properties
{
    PixelSize ("Pixel size", Range(1, 32)) = 4
    Resolution ("Resolution", Vector2) = (0, 0)
}

HLSLPROGRAM
// Resolution est rempli par le moteur (taille de la cible en pixels).
float4 Fragment(float2 uv, float4 color)
{
    float2 cells = Resolution / PixelSize;
    float2 p = (floor(uv * cells) + 0.5) / cells;
    return SampleMainTex(p) * color;
}
ENDHLSL
