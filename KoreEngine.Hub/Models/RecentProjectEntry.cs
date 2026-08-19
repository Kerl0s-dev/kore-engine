using System.IO;

namespace KoreEngine.Hub.Models;

/// <summary>
/// Un projet KoreEngine connu du Hub. "Path" pointe vers le dossier racine
/// du projet (celui qui contient {Name}.sln, {Name}.csproj, Assets/, etc.).
/// </summary>
public record RecentProjectEntry(string Name, string Path, DateTime LastOpened)
{
    /// <summary>Le dossier et la solution existent-ils toujours sur le disque ?</summary>
    public bool Exists =>
        Directory.Exists(Path) &&
        File.Exists(System.IO.Path.Combine(Path, $"{Name}.sln"));
}
