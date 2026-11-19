Shader "Post/ColorAdjust"

Properties
{
    Brightness ("Brightness", Range(-1, 1)) = 0
    Contrast ("Contrast", Range(0, 2)) = 1
    Saturation ("Saturation", Range(0, 2)) = 1
    Tint ("Tint", Color) = (255, 255, 255, 255)
}

HLSLPROGRAM
float4 Fragment(float2 uv, float4 color)
{
    float4 src = SampleMainTex(uv) * color;
    float3 rgb = src.rgb + Brightness;
    rgb = (rgb - 0.5) * Contrast + 0.5;
    float gray = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    rgb = lerp(float3(gray, gray, gray), rgb, Saturation);
    rgb *= Tint.rgb;
    return float4(saturate(rgb), src.a);
}
ENDHLSL
