using System.Collections.Concurrent;
using KoreEngine;

/// <summary>
/// Rechargement à chaud des shaders : surveille les fichiers .shader et .kmat de Assets/ et Packages/,
/// et les recompile dès qu'ils sont enregistrés (même modèle que ScriptWatcher).
/// </summary>
public class ShaderWatcher : IDisposable
{
    readonly List<FileSystemWatcher> watchers = new();

    // Les événements arrivent sur un autre thread : on ne fait que noter les chemins, le rechargement
    // se fait dans Update(), sur le thread de rendu.
    readonly ConcurrentDictionary<string, byte> pending = new();
    volatile bool changed;
    float timeSinceChange;

    // Les éditeurs enregistrent souvent en plusieurs étapes : on attend que ça se calme
    const float Delay = 0.3f;

    public ShaderWatcher(string projectRoot)
    {
        foreach (var folder in new[] { "Assets", "Packages" })
        {
            string dir = Path.Combine(projectRoot, folder);
            if (!Directory.Exists(dir)) continue;

            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Filters.Add("*.shader");
            watcher.Filters.Add("*.kmat");

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += (s, e) => OnChanged(s, e);
            watchers.Add(watcher);
        }
    }

    void OnChanged(object sender, FileSystemEventArgs e)
    {
        pending[e.FullPath] = 0;
        timeSinceChange = 0f;
        changed = true;
    }

    /// <summary>À appeler une fois par frame, avant le rendu.</summary>
    public void Update()
    {
        if (!changed) return;

        timeSinceChange += Time.deltaTime;
        if (timeSinceChange < Delay) return;

        changed = false;
        var paths = pending.Keys.ToList();
        pending.Clear();

        bool materialChanged = false;

        foreach (var path in paths)
        {
            try
            {
                if (path.EndsWith(".kmat", StringComparison.OrdinalIgnoreCase))
                    materialChanged = true;
                else
                    ShaderCache.Reload(path);
            }
            catch (IOException)
            {
                // Fichier encore verrouillé par l'éditeur de texte : on réessaie au prochain tour
                pending[path] = 0;
                timeSinceChange = 0f;
                changed = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[Shader] Rechargement de '{path}' : {ex}");
            }
        }

        if (materialChanged)
        {
            try
            {
                MaterialCache.Clear();   // les matériaux sont relus au prochain usage
                Logger.Log("[Material] Matériaux rechargés");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Material] Rechargement : {ex}");
            }
        }
    }

    public void Dispose()
    {
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
    }
}