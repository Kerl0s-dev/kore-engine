namespace KoreEngine.Core;

/// <summary>
/// Empêche un champ public d'apparaître dans l'inspector de l'éditeur.
/// Fonctionne uniquement avec l'auto-draw par réflexion — n'a pas d'effet
/// si le composant surcharge DrawInspector() manuellement.
///
/// Utilisation :
///   [HideInInspector]
///   public float internalValue;
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HideInInspectorAttribute : Attribute { }