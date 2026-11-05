using KoreEngine;

public class ScriptWatcher : IDisposable
{
    readonly List<FileSystemWatcher> watchers = new();

    volatile bool buildPending = false;
    float timeSinceChange = 0f;
    const float BuildDelay = 1.5f;

    string lastError = "";
    readonly string[] watchDirs;

    int unloadPendingFrames = -1;

    public bool IsBuilding => ScriptCompiler.IsCompiling;
    public string BuildStatus => lastError.Length > 0 ? lastError : ScriptCompiler.Status;

    public ScriptWatcher(string projectRoot)
    {
        watchDirs = [Path.Combine(projectRoot, "Assets")];

        foreach (var dir in watchDirs.Where(Directory.Exists))
        {
            var w = new FileSystemWatcher(dir)
            {
                Filter = "*.cs",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            w.Changed += OnChanged;
            w.Created += OnChanged;
            w.Deleted += OnChanged;
            w.Renamed += (s, e) => OnChanged(s, e);
            watchers.Add(w);
            Console.WriteLine($"[ScriptWatcher] Surveillance : {dir}");
        }

        ScriptCompiler.OnCompileSuccess += OnCompileSuccess;
        ScriptCompiler.OnCompileError += OnCompileError;

        ReloadAssemblies();
    }

    void OnChanged(object sender, FileSystemEventArgs e)
    {
        buildPending = true;
        timeSinceChange = 0f;
        lastError = "";
        Console.WriteLine($"[ScriptWatcher] Changement détecté : {Path.GetFileName(e.FullPath)}");
    }

    void OnCompileSuccess()
    {
        lastError = "";

        // 1. Vider la sélection éditeur
        EditorSelection.Selected = null;

        // 2. Sauvegarder la scène actuelle
        SceneManager.SaveCurrentScene();

        // 3. Demander le chargement de la nouvelle scène
        string? scenePath = SceneManager.CurrentScenePath;
        if (scenePath != null && File.Exists(scenePath))
        {
            SceneManager.LoadSceneFromFile(scenePath);
            Console.WriteLine($"[ScriptWatcher] Scène en attente de rechargement : {Path.GetFileNameWithoutExtension(scenePath)}");
        }

        // 4. Attendre 5 frames avant de forcer le déchargement pour s'assurer que le rechargement UI est terminé
        unloadPendingFrames = 5;
    }

    void OnCompileError()
    {
        lastError = $"Erreur : {ScriptCompiler.Status}";
    }

    public void FinalizeUnloadIfPending()
    {
        if (unloadPendingFrames < 0) return;

        if (unloadPendingFrames > 0)
        {
            unloadPendingFrames--;
            return;
        }

        unloadPendingFrames = -1;
        ScriptCompiler.FinalizeUnload();
    }

    public void Update()
    {
        // Vérifier si le compilateur a terminé et signalé un succès en arrière-plan
        if (ScriptCompiler.PendingSuccessNotification)
        {
            ScriptCompiler.PendingSuccessNotification = false;

            // 1. Force l'événement de succès (ce qui déclenchera componentTypes = null si l'inspecteur est abonné)
            ScriptCompiler.RaiseCompileSuccess();

            // 2. Optionnel : Appeler FinalizeUnload pour vérifier s'il y a des fuites d'ALC dans la console
            ScriptCompiler.FinalizeUnload();

            Console.WriteLine("[Editor] Hot Reload appliqué avec succès !");
        }

        if (ScriptCompiler.PendingErrorNotification)
        {
            ScriptCompiler.PendingErrorNotification = false;
            Console.WriteLine($"[Editor] Erreur interceptée : {ScriptCompiler.Status}");
            ScriptCompiler.RaiseCompileError();
            Console.WriteLine("[Editor] Erreur lors du Hot Reload.");
        }

        if (!buildPending || ScriptCompiler.IsCompiling) return;

        timeSinceChange += Time.deltaTime;
        if (timeSinceChange < BuildDelay) return;

        buildPending = false;
        ReloadAssemblies();
    }

    public void ReloadAssemblies() => ScriptCompiler.CompileScripts(watchDirs);

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