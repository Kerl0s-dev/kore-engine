using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KoreEngine.Hub.Models;

/// <summary>
/// Enveloppe un RecentProjectEntry pour l'affichage : Size et EditorVersion
/// sont calculés de façon asynchrone (lecture disque potentiellement lente),
/// d'où ce type mutable qui notifie l'UI — contrairement au record
/// RecentProjectEntry immuable, qui ne sert qu'à la persistance JSON.
/// </summary>
public class ProjectListItem : INotifyPropertyChanged
{
    public RecentProjectEntry Entry { get; }

    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public DateTime LastOpened => Entry.LastOpened;
    public bool Exists => Entry.Exists;

    string _size = "Calcul...";
    public string Size
    {
        get => _size;
        set { _size = value; OnPropertyChanged(); }
    }

    string _editorVersion = "Calcul...";
    public string EditorVersion
    {
        get => _editorVersion;
        set { _editorVersion = value; OnPropertyChanged(); }
    }

    public ProjectListItem(RecentProjectEntry entry)
    {
        Entry = entry;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
