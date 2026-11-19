Shader "Post/Blur"

Properties
{
    Radius ("Radius in pixels", Range(0, 8)) = 2
    Resolution ("Resolution", Vector2) = (0, 0)
}

HLSLPROGRAM
// Flou 3x3 pondéré (1 2 1 / 2 4 2 / 1 2 1), étalé sur Radius pixels.
float4 Fragment(float2 uv, float4 color)
{
    float2 px = Radius / Resolution;

    float4 sum = SampleMainTex(uv) * 4.0;
    sum += SampleMainTex(uv + float2(px.x, 0.0)) * 2.0;
    sum += SampleMainTex(uv - float2(px.x, 0.0)) * 2.0;
    sum += SampleMainTex(uv + float2(0.0, px.y)) * 2.0;
    sum += SampleMainTex(uv - float2(0.0, px.y)) * 2.0;
    sum += SampleMainTex(uv + px);
    sum += SampleMainTex(uv - px);
    sum += SampleMainTex(uv + float2(px.x, -px.y));
    sum += SampleMainTex(uv + float2(-px.x, px.y));

    return (sum / 16.0) * color;
}
ENDHLSL
