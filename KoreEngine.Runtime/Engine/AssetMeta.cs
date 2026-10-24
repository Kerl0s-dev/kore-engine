namespace KoreEngine;

public static class AssetMeta
{
    public const string Extension = ".kmeta";

    public static string PathFor(string assetPath) => assetPath + Extension;
    public static bool IsMeta(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, string> Read(string assetPath)
    {
        var values = new Dictionary<string, string>();
        string meta = PathFor(assetPath);
        if (!File.Exists(meta)) return values;

        foreach (var raw in File.ReadAllLines(meta))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            values[raw[..colon].Trim()] = raw[(colon + 1)..].Trim();
        }
        return values;
    }

    public static void Write(string assetPath, IEnumerable<KeyValuePair<string, string>> values)
    {
        try { File.WriteAllLines(PathFor(assetPath), values.Select(kv => $"{kv.Key}: {kv.Value}")); }
        catch (Exception e) { Logger.Error($"[AssetMeta] Écriture de {PathFor(assetPath)} : {e.Message}"); }
    }

    /// À appeler quand l'asset est renommé : le .kmeta le suit.
    public static void Move(string oldAsset, string newAsset)
    {
        string oldMeta = PathFor(oldAsset);
        if (File.Exists(oldMeta))
            File.Move(oldMeta, PathFor(newAsset), overwrite: true);
    }

    public static void Delete(string assetPath)
    {
        string meta = PathFor(assetPath);
        if (File.Exists(meta)) File.Delete(meta);
    }
}