using KoreEngine.Engine;
using System.Diagnostics;

namespace KoreEngine.Editor;

/// <summary>
/// Lance Visual Studio sur un fichier, avec ou sans ligne précise.
/// La localisation de devenv.exe passe par vswhere.exe, installé avec
/// toute instance de VS depuis VS2017 — pas besoin de chemin configuré à la main.
/// </summary>
public static class ExternalEditor
{
    static string? devenvPath;
    static bool devenvSearched = false;

    public static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception e) { Logger.Error($"OpenInEditor: {e.Message}"); }
    }

    /// <summary>
    /// Ouvre un fichier à une ligne précise dans Visual Studio via
    /// "devenv /edit fichier /command Edit.GoTo ligne". Si devenv.exe est
    /// introuvable, se rabat sur une ouverture simple sans positionnement.
    /// </summary>
    public static void OpenFileAtLine(string path, int line)
    {
        string? devenv = FindDevenv();
        if (devenv == null)
        {
            Logger.Warning("[ExternalEditor] devenv.exe introuvable — ouverture sans positionnement sur la ligne.");
            OpenFile(path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = devenv,
                Arguments = $"/edit \"{path}\" /command \"Edit.GoTo {line}\"",
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            Logger.Error($"[ExternalEditor] {e.Message}");
            OpenFile(path);
        }
    }

    static string? FindDevenv()
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
                Arguments = "-latest -products * -property productPath",
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