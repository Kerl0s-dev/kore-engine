namespace KoreEngine;

/// <summary>
/// Écrit le Logger dans un fichier. Le Player est une application sans console : sans ça, les erreurs
/// (shader non compilé, ressource introuvable, exception...) ne sont visibles nulle part dans un build.
/// </summary>
public static class FileLogger
{
    static readonly object gate = new();
    static string? path;

    public static void Enable(string filePath)
    {
        if (path != null) return;

        try
        {
            File.WriteAllText(filePath, $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch
        {
            return;   // dossier en lecture seule : on se passe de fichier de log
        }

        path = filePath;
        Logger.OnLog += Write;

        foreach (var entry in Logger.Entries.ToList())
            Write(entry);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Append($"{DateTime.Now:HH:mm:ss} [CRASH] {e.ExceptionObject}");
    }

    static void Write(LogEntry entry)
    {
        string location = entry.FilePath != null ? $" ({entry.FilePath})" : "";
        Append($"{entry.Time:HH:mm:ss} [{entry.Level}] {entry.Message}{location}");
    }

    static void Append(string line)
    {
        if (path == null) return;

        lock (gate)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); }
            catch { /* log impossible : on ne fait jamais planter le jeu pour ça */ }
        }
    }
}