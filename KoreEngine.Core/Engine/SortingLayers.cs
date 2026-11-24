namespace KoreEngine;

/// <summary>
/// Liste ordonnée des Sorting Layers du projet, lue dans Assets/SortingLayers.txt (un nom par ligne,
/// du plus loin au plus proche). "Default" existe toujours : s'il n'est pas dans le fichier, il passe en premier.
/// Un nom inconnu est traité comme "Default".
/// </summary>
public static class SortingLayers
{
    static readonly List<string> names = new() { "Default" };
    static readonly Dictionary<string, int> indices = new() { ["Default"] = 0 };
    static string? loadedFrom;

    public static IReadOnlyList<string> Names
    {
        get { EnsureLoaded(); return names; }
    }

    public static int IndexOf(string? name)
    {
        EnsureLoaded();
        return name != null && indices.TryGetValue(name, out int i) ? i : indices["Default"];
    }

    /// <summary>Force la relecture du fichier au prochain accès.</summary>
    public static void Reload() => loadedFrom = null;

    static void EnsureLoaded()
    {
        string file = Path.Combine(SceneManager.AssetsDirectory ?? "", "SortingLayers.txt");
        if (loadedFrom == file) return;
        loadedFrom = file;

        names.Clear();
        indices.Clear();

        if (File.Exists(file))
        {
            foreach (var raw in File.ReadAllLines(file))
            {
                string name = raw.Trim();
                if (name.Length > 0 && !names.Contains(name))
                    names.Add(name);
            }
        }

        if (!names.Contains("Default"))
            names.Insert(0, "Default");

        for (int i = 0; i < names.Count; i++)
            indices[names[i]] = i;
    }
}