using KoreEngine.Engine;
using System.Diagnostics;

namespace KoreEngine.Editor;

/// <summary>
/// Lance Visual Studio sur un fichier dans le contexte de la solution du projet.
/// </summary>
public static class ExternalEditor
{
    private static string? devenvPath;
    private static bool devenvSearched = false;

    public static void OpenFile(string path)
    {
        OpenFileAtLine(path, 1);
    }

    /// <summary>
    /// Ouvre un fichier du projet dans sa solution Visual Studio (.sln) à une ligne précise.
    /// </summary>
    public static void OpenFileAtLine(string path, int line)
    {
        string? devenv = FindDevenv();
        if (devenv == null)
        {
            Logger.Warning("[ExternalEditor] devenv.exe introuvable — ouverture fallback OS.");
            FallbackOpen(path);
            return;
        }

        try
        {
            string absolutePath = Path.GetFullPath(path);
            string? solutionPath = GetSolutionPath();

            string arguments;
            if (!string.IsNullOrEmpty(solutionPath))
            {
                // Ouvre la solution et charge le fichier voulue
                arguments = $"\"{solutionPath}\" /Command \"File.OpenFile {absolutePath}\"";
            }
            else
            {
                // Ouvre le fichier direct dans Visual Studio si pas de solution détectée
                arguments = $"/Edit \"{absolutePath}\" /Command \"Edit.GoTo {line}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = devenv,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
        }
        catch (Exception e)
        {
            Logger.Error($"[ExternalEditor] Erreur lors du lancement de Visual Studio: {e.Message}");
            FallbackOpen(path);
        }
    }

    /// <summary>
    /// Recherche dynamiquement le fichier .sln du projet.
    /// </summary>
    private static string? GetSolutionPath()
    {
        try
        {
            string? projectRoot = ProjectPanel.FindProjectRoot();
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
                return null;

            // 1. Cherche un .sln qui porte exactement le nom du dossier projet (ex: MonProjet.sln)
            string folderName = new DirectoryInfo(projectRoot).Name;
            string expectedSlnPath = Path.Combine(projectRoot, $"{folderName}.sln");

            if (File.Exists(expectedSlnPath))
                return expectedSlnPath;

            // 2. Si non trouvé, retourne le premier fichier .sln disponible à la racine
            return Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch (Exception e)
        {
            Logger.Error($"[ExternalEditor] Recherche de la solution : {e.Message}");
            return null;
        }
    }

    private static void FallbackOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            Logger.Error($"[ExternalEditor] Fallback failed: {e.Message}");
        }
    }

    private static string? FindDevenv()
    {
        if (devenvSearched) return devenvPath;
        devenvSearched = true;

        try
        {
            string vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");

            if (!File.Exists(vswhere)) return null;

            var psi = new ProcessStartInfo
            {
                FileName = vswhere,
                Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.CoreEditor -property productPath",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            string output = proc!.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            devenvPath = File.Exists(output) ? output : null;
        }
        catch (Exception e)
        {
            Logger.Error($"[ExternalEditor] Recherche devenv.exe : {e.Message}");
            devenvPath = null;
        }

        return devenvPath;
    }
}