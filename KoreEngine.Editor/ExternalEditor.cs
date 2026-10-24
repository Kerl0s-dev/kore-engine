using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace KoreEngine;

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
        string absolutePath = Path.GetFullPath(path);
        string? devenv = FindDevenv();

        if (devenv == null || !OperatingSystem.IsWindows())
        {
            if (devenv == null) Logger.Warning("[ExternalEditor] devenv.exe introuvable — ouverture fallback OS.");
            FallbackOpen(path);
            return;
        }

        string? solutionPath = GetSolutionPath();

        // Pas de solution : ancien comportement (ouvre le fichier seul, à la ligne voulue).
        if (string.IsNullOrEmpty(solutionPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = devenv,
                    Arguments = $"/Edit \"{absolutePath}\" /Command \"Edit.GoTo {line}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception e)
            {
                Logger.Error($"[ExternalEditor] Erreur lors du lancement de Visual Studio: {e.Message}");
                FallbackOpen(path);
            }
            return;
        }

        // Thread de fond : le démarrage de VS peut prendre plusieurs dizaines de secondes.
        Task.Run(() =>
        {
            try { OpenInSolution(devenv, solutionPath, absolutePath, line); }
            catch (Exception e)
            {
                Logger.Error($"[ExternalEditor] {e.Message}");
                FallbackOpen(path);
            }
        });
    }

    private static void OpenInSolution(string devenv, string solution, string file, int line)
    {
        // 1. VS tourne déjà avec cette solution : on lui parle directement.
        if (TryGoTo(solution, file, line)) return;

        // 2. Sinon on lance VS sur la solution, puis on attend qu'elle soit chargée.
        Process.Start(new ProcessStartInfo
        {
            FileName = devenv,
            Arguments = $"\"{solution}\"",
            UseShellExecute = false
        });

        for (int i = 0; i < 120; i++)   // 120 x 500 ms = 60 s max
        {
            Thread.Sleep(500);
            if (TryGoTo(solution, file, line)) return;
        }

        Logger.Warning("[ExternalEditor] Visual Studio n'a pas fini de charger la solution à temps.");
    }

    /// <summary>
    /// Trouve l'instance de VS qui a CETTE solution ouverte, y ouvre le fichier et saute à la ligne.
    /// Retourne false si aucune instance ne correspond, ou si VS est occupé (chargement) : l'appelant réessaie.
    /// </summary>
    private static bool TryGoTo(string solution, string file, int line)
    {
        foreach (var obj in RunningVisualStudioInstances())
        {
            try
            {
                dynamic dte = obj;
                string openSolution = dte.Solution.FullName;   // vide tant que la solution charge
                if (!string.Equals(openSolution, solution, StringComparison.OrdinalIgnoreCase)) continue;

                dte.ItemOperations.OpenFile(file);
                dynamic selection = dte.ActiveDocument.Selection;
                selection.GotoLine(line, false);                // true = sélectionne la ligne
                dte.MainWindow.Activate();
                return true;
            }
            catch
            {
                // VS occupé (RPC_E_CALL_REJECTED) ou pas encore prêt : on laisse l'appelant réessayer.
            }
        }
        return false;
    }

    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable rot);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(uint reserved, out IBindCtx ctx);

    /// <summary>Instances de VS enregistrées dans la table des objets COM en cours (ROT).</summary>
    private static List<object> RunningVisualStudioInstances()
    {
        var result = new List<object>();

        if (GetRunningObjectTable(0, out var rot) != 0) return result;
        if (CreateBindCtx(0, out var ctx) != 0) return result;

        rot.EnumRunning(out var enumerator);
        var monikers = new IMoniker[1];

        while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
        {
            monikers[0].GetDisplayName(ctx, null, out string name);
            if (!name.StartsWith("!VisualStudio.DTE", StringComparison.OrdinalIgnoreCase)) continue;

            rot.GetObject(monikers[0], out object instance);
            result.Add(instance);
        }

        return result;
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
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    ArgumentList = { Path.GetFullPath(path) },
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { Path.GetFullPath(path) },
                    UseShellExecute = false
                });
            }
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