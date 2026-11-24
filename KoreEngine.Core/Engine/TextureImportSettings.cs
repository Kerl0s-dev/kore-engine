namespace KoreEngine;

public enum TextureFilterMode { Nearest, Linear }   // l'ordre = l'index du combo

public class TextureImportSettings
{
    // Linear = le comportement historique (défaut SDL) : les textures existantes ne changent pas d'aspect.
    public const TextureFilterMode Default = TextureFilterMode.Linear;
    public TextureFilterMode FilterMode = Default;

    public static TextureImportSettings Load(string assetPath)
    {
        var s = new TextureImportSettings();
        var values = AssetMeta.Read(TextureCache.Resolve(assetPath));
        if (values.TryGetValue("FilterMode", out var v) && Enum.TryParse<TextureFilterMode>(v, true, out var mode))
            s.FilterMode = mode;
        return s;
    }

    public void Save(string assetPath)
    {
        string resolved = TextureCache.Resolve(assetPath);
        // Pas de .kmeta créé tant que tout est au défaut : évite d'en semer un par texture.
        if (FilterMode == Default && !File.Exists(AssetMeta.PathFor(resolved))) return;
        AssetMeta.Write(resolved, new[] { new KeyValuePair<string, string>("FilterMode", FilterMode.ToString()) });
    }

    public IEnumerable<InspectorField> GetInspectorFields(string assetPath)
    {
        yield return new EnumField
        {
            Label = "Filter Mode",
            Names = Enum.GetNames<TextureFilterMode>(),
            Get = () => (int)FilterMode,
            Set = v =>
            {
                FilterMode = (TextureFilterMode)v;
                Save(assetPath);
                TextureCache.SetFilter(assetPath, FilterMode);   // effet immédiat, sans recharger
            }
        };
    }
}