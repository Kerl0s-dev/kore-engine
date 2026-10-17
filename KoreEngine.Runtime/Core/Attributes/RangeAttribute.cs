namespace KoreEngine;

/// <summary>
/// Limite un champ numérique (float, int, byte) à [Min, Max] et l'affiche
/// sous forme de slider dans l'inspector. Fonctionne avec l'auto-draw par
/// réflexion, pas avec un GetInspectorFields() manuel (voir plus bas).
///
/// Utilisation :
///   [Range(0, 1)]
///   public float Volume = 1f;
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RangeAttribute : Attribute
{
    public float Min { get; }
    public float Max { get; }

    public RangeAttribute(float min, float max)
    {
        Min = min;
        Max = max;
    }
}