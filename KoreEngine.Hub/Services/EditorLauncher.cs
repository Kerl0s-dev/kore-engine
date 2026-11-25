using KoreEngine.Hub.Models;
using System.Diagnostics;

namespace KoreEngine.Hub.Services;

public static class EditorLauncher
{
    const int StartupGraceMs = 4000;

    public static void OpenProject(
        RecentProjectEntry entry,
        string? engineDir,
        Action<string> onLogLine,
        Action<bool> onBuildFinished,
        Action<string>? onStage = null)
    {
        if (!Directory.Exists(entry.Path))
        {
            onLogLine($"[EditorLauncher] Project folder not found : {entry.Path}");
            onBuildFinished(false);
            return;
        }

        string? editorExe = ResolveStandaloneEditorExe(engineDir, onLogLine);
        if (editorExe == null || !File.Exists(editorExe))
        {
            onLogLine($"[EditorLauncher] Standalone editor executable not found ({editorExe ?? "null"}).");
            onBuildFinished(false);
            return;
        }

        // Optionnel : mettre à jour uniquement les dépendances Core/Player si nécessaire
        if (!string.IsNullOrWhiteSpace(engineDir))
        {
            onStage?.Invoke("Synchronization of core dependencies...");
            SyncEngineDependencies(entry, engineDir, onLogLine);
        }

        onStage?.Invoke("Launching standalone editor...");
        try
        {
            LaunchStandaloneEditor(editorExe, entry, onLogLine);
            onBuildFinished(true);
        }
        catch (Exception ex)
        {
            onLogLine($"[EditorLauncher] Not possible to launch : {ex.Message}");
            onBuildFinished(false);
        }
    }

    static string? ResolveStandaloneEditorExe(string? engineDir, Action<string> onLogLine)
    {
        string exeName = OperatingSystem.IsWindows() ? "KoreEngine.Editor.exe" : "KoreEngine.Editor";

        // 1. À côté du Hub (cas d'une distribution/installation côte à côte)
        string besideHub = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(besideHub))
            return besideHub;

        // 2. Depuis engineDir (ex: KoreEngine.Editor/bin/Release/net10.0/... ou publish/)
        if (!string.IsNullOrWhiteSpace(engineDir))
        {
            string editorBinRoot = Path.Combine(engineDir, "KoreEngine.Editor", "bin");
            if (Directory.Exists(editorBinRoot))
            {
                string? candidate = Directory.GetFiles(editorBinRoot, exeName, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (candidate != null)
                    return candidate;
            }
        }

        onLogLine($"[EditorLauncher] Searched '{exeName}' beside Hub and under '{engineDir}'.");
        return null;
    }

    public static void SyncEngineDependencies(RecentProjectEntry entry, string? engineDir, Action<string> onLogLine)
    {
        if (string.IsNullOrWhiteSpace(engineDir))
        {
            onLogLine("[EditorLauncher] Engine folder not found — resynchronization ignored.");
            return;
        }

        ProjectScaffolder.RefreshDependencies(engineDir, entry.Path, entry.Name, onLogLine);
    }

    static void LaunchStandaloneEditor(string editorExe, RecentProjectEntry entry, Action<string> onLogLine)
    {
        string editorDir = Path.GetDirectoryName(editorExe)!;

        var startInfo = new ProcessStartInfo
        {
            FileName = editorExe,
            Arguments = $"--project \"{entry.Path}\"",
            WorkingDirectory = editorDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Not able to start the editor.");

        int reporting = 1;
        void OnData(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line) && Volatile.Read(ref reporting) == 1)
                onLogLine("[Editor] " + line);
        }

        process.OutputDataReceived += (_, e) => OnData(e.Data);
        process.ErrorDataReceived += (_, e) => OnData(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited = process.WaitForExit(StartupGraceMs);
        if (exited)
            process.WaitForExit();

        Volatile.Write(ref reporting, 0);

        if (exited && process.ExitCode != 0)
            throw new InvalidOperationException($"The editor closed itself while starting (code {process.ExitCode}).");

        RecentProjectsStore.AddOrUpdate(entry with { LastOpened = DateTime.Now });
    }
}