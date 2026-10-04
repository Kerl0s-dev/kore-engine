namespace KoreEngine.Hub.Models;

/// <summary>
/// Un projet affiché dans la liste. Size et EditorVersion sont calculés en tâche
/// de fond (lecture disque) ; l'UI SDL relit simplement les valeurs à chaque frame,
/// donc plus besoin d'INotifyPropertyChanged.
/// </summary>
public class ProjectListItem
{
    public RecentProjectEntry Entry { get; }

    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public DateTime LastOpened => Entry.LastOpened;

    /// <summary>Mis en cache : Entry.Exists touche le disque, trop cher à appeler à chaque frame.</summary>
    public bool Exists { get; set; }

    public string Size { get; set; } = "…";
    public string EditorVersion { get; set; } = "…";

    public ProjectListItem(RecentProjectEntry entry)
    {
        Entry = entry;
        Exists = entry.Exists;
    }
}