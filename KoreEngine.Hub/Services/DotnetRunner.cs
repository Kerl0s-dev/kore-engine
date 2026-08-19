using System.Diagnostics;
using System.IO;

namespace KoreEngine.Hub.Services;

/// <summary>
/// Lance une commande "dotnet" (clean/build/publish...) en process caché, en
/// streamant stdout/stderr ligne par ligne. Partagé par EditorLauncher (clean
/// + build avant de lancer un projet) et BuildService (publish du Player),
/// pour ne pas dupliquer la logique de process entre les deux.
/// </summary>
public static class DotnetRunner
{
    public static void Run(string command, string targetPath, string workingDir,
        Action<string> onLogLine, Action<bool> onFinished, string extraArgs = "")
    {
        string argsSuffix = extraArgs.Length > 0 ? $" {extraArgs}" : "";
        onLogLine($"> dotnet {command} \"{Path.GetFileName(targetPath)}\"{argsSuffix}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{command} \"{targetPath}\"{argsSuffix}",
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onLogLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onLogLine(e.Data); };

        proc.Exited += (_, _) =>
        {
            bool success = proc.ExitCode == 0;
            onFinished(success);
            proc.Dispose();
        };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            onLogLine($"Impossible de lancer dotnet {command} : {ex.Message}");
            onFinished(false);
        }
    }
}
