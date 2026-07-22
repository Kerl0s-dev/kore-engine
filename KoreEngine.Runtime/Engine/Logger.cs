namespace KoreEngine.Engine;

public enum LogLevel { Info, Warning, Error, Sucess }

// FilePath/Line optionnels : seuls les logs qui en ont un affichage cliquable
// dans ConsolePanel (typiquement les erreurs de compilation) les renseignent.
public record LogEntry(LogLevel Level, string Message, DateTime Time,
    string? FilePath = null, int? Line = null);

public static class Logger
{
    const int MaxEntries = 500;

    static readonly List<LogEntry> entries = new();
    public static IReadOnlyList<LogEntry> Entries => entries;

    public static event Action<LogEntry>? OnLog;

    public static void Log(string message) => Add(LogLevel.Info, message);
    public static void Warning(string message) => Add(LogLevel.Warning, message);
    public static void Sucess(string message) => Add(LogLevel.Sucess, message);

    // Surcharge avec fichier/ligne, utilisée par ScriptCompiler pour les
    // erreurs de compilation cliquables. Les autres appels à Error(msg)
    // continuent de fonctionner sans rien casser.
    public static void Error(string message, string? filePath = null, int? line = null)
        => Add(LogLevel.Error, message, filePath, line);

    static void Add(LogLevel level, string message, string? filePath = null, int? line = null)
    {
        var entry = new LogEntry(level, message, DateTime.Now, filePath, line);
        lock (entries)
        {
            entries.Add(entry);
            if (entries.Count > MaxEntries)
                entries.RemoveAt(0);
        }
        OnLog?.Invoke(entry);
    }

    public static void Clear()
    {
        lock (entries) entries.Clear();
    }
}