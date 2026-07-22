using KoreEngine.Engine;

namespace KoreEngine.Editor;

/// <summary>
/// Surveille les .cs dans le dossier actuel.
/// Quand un fichier change, attend un court délai (debounce), puis
/// lance ScriptCompiler. Si la compilation réussit, sauvegarde la
/// scène courante et la recharge depuis le .kscene.
/// </summary>
public class ScriptWatcher : IDisposable
{
    readonly List<FileSystemWatcher> watchers = new();

    bool buildPending = false;
    float timeSinceChange = 0f;
    const float BuildDelay = 1.5f; // secondes après le dernier changement

    string lastError = "";

    // Répertoires à surveiller
    readonly string[] watchDirs;

    volatile bool pendingFinalizeUnload = false;

    public bool IsBuilding => ScriptCompiler.IsCompiling;
    public string BuildStatus => lastError.Length > 0 ? lastError : ScriptCompiler.Status;

    public ScriptWatcher(string projectRoot)
    {
        watchDirs =
        [
            Path.Combine(projectRoot, "Assets")
        ];

        foreach (var dir in watchDirs.Where(Directory.Exists))
        {
            var w = new FileSystemWatcher(dir)
            {
                Filter = "*.cs",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            w.Changed += OnChanged;
            w.Created += OnChanged;
            w.Deleted += OnChanged;
            w.Renamed += (s, e) => OnChanged(s, e);
            watchers.Add(w);
            Console.WriteLine($"[ScriptWatcher] Surveillance : {dir}");
        }

        // Abonnement aux events du compilateur
        ScriptCompiler.OnCompileSuccess += OnCompileSuccess;
        ScriptCompiler.OnCompileError += OnCompileError;
    }
    void OnChanged(object sender, FileSystemEventArgs e)
    {
        buildPending = true;
        timeSinceChange = 0f;
        lastError = "";
        Console.WriteLine($"[ScriptWatcher] Changement : {Path.GetFileName(e.FullPath)}");
    }

    void OnCompileSuccess()
    {
        lastError = "";

        // Sauvegarde la scène courante avant de la recharger
        SceneManager.SaveCurrentScene();

        // Prépare le rechargement — le swap réel (current = next) n'arrive
        // qu'au prochain ApplyPendingScene() sur le thread principal.
        string? scenePath = SceneManager.CurrentScenePath;
        if (scenePath != null && File.Exists(scenePath))
        {
            SceneManager.LoadSceneFromFile(scenePath);
            Console.WriteLine($"[ScriptWatcher] Scène en attente de rechargement : {Path.GetFileNameWithoutExtension(scenePath)}");
        }

        // Ne PAS appeler FinalizeUnload() ici — on est sur le thread de
        // compilation en arrière-plan, et SceneManager.current pointe encore
        // vers l'ancienne scène (le swap n'a pas eu lieu). On se contente de
        // marquer une demande, consommée après le swap réel sur le thread UI.
        pendingFinalizeUnload = true;
    }

    void OnCompileError(string error)
    {
        lastError = $"Erreur : {error.Split('\n').FirstOrDefault() ?? error}";
    }

    /// <summary>
    /// À appeler juste après SceneManager.ApplyPendingScene() dans la boucle
    /// principale — garantit que l'ancienne scène a bien été remplacée avant
    /// de tenter de décharger l'assembly qui la référençait.
    /// </summary>
    public void FinalizeUnloadIfPending()
    {
        if (!pendingFinalizeUnload) return;
        pendingFinalizeUnload = false;
        ScriptCompiler.FinalizeUnload();
    }

    public void Update(float dt)
    {
        if (!buildPending || ScriptCompiler.IsCompiling) return;

        timeSinceChange += dt;
        if (timeSinceChange < BuildDelay) return;

        buildPending = false;

        var csFiles = watchDirs
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Distinct()
            .ToList();

        _ = ScriptCompiler.CompileAsync(csFiles);
    }

    public void Dispose()
    {
        ScriptCompiler.OnCompileSuccess -= OnCompileSuccess;
        ScriptCompiler.OnCompileError -= OnCompileError;

        foreach (var w in watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
    }
}