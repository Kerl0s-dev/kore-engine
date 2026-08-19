using System.IO;
using System.Text.Json;
using KoreEngine.Hub.Models;

namespace KoreEngine.Hub.Services;

public static class RecentProjectsStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KoreEngine", "recent_projects.json");

    public static List<RecentProjectEntry> Load()
    {
        if (!File.Exists(FilePath)) return new();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<RecentProjectEntry>>(json) ?? new();
        }
        catch
        {
            // Fichier corrompu : on repart d'une liste vide plutôt que de crasher le Hub.
            return new();
        }
    }

    public static void AddOrUpdate(RecentProjectEntry entry)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, entry);
        Persist(list);
    }

    public static void Remove(string path)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        Persist(list);
    }

    static void Persist(List<RecentProjectEntry> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
