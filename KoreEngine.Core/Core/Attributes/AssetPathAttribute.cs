namespace KoreEngine;

/// <summary>
/// Sur un champ string : l'Inspector affiche un sélecteur de fichiers du projet
/// (Assets + Packages) limité à ces extensions. Auto-draw uniquement.
///   [AssetPath(".ttf", ".otf")] public string FontPath = "";
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AssetPathAttribute : Attribute
{
    public string[] Extensions { get; }

    public AssetPathAttribute(params string[] extensions)
    {
        Extensions = extensions;
    }

    public override string ToString() => $"{string.Join(", ", Extensions)}";
}