namespace KoreEngine;

/// <summary>Noms des 32 layers d'objets (GameObject.Layer), utilisés par le Culling Mask des caméras.</summary>
public static class LayerNames
{
    public static readonly string[] All = Enumerable.Range(0, 32)
        .Select(i => i == 0 ? "Default" : $"Layer {i}")
        .ToArray();
}