using System.Diagnostics;
using System.IO;
using KoreEngine.Hub.Models;

namespace KoreEngine.Hub.Services;

/// <summary>
/// Resynchronise les dépendances moteur, nettoie puis rebuild systématiquement
/// la solution d'un projet avant de le lancer :
///   - la resynchronisation évite de tourner avec des dépendances copiées une
///     seule fois à la création du projet et jamais rafraîchies depuis (ex:
///     Microsoft.CodeAnalysis.CSharp ajoutée à KoreEngine.Runtime après coup)
///   - le "clean" évite de tourner avec des binaires de script résiduels
///     (obj/bin corrompus, ancien code compilé qui traîne)
///   - le rebuild garantit qu'on ne tourne jamais avec des
///     KoreEngine.Runtime/Editor.dll périmées non plus
/// </summary>
public static class EditorLauncher
{
    public static void OpenProject(RecentProjectEntry entry, string? engineDir, Action<string> onLogLine, Action<bool> onBuildFinished)
    {
        string slnPath = Path.Combine(entry.Path, $"{entry.Name}.sln");

        if (!File.Exists(slnPath))
        {
            onLogLine($"Solution introuvable : {slnPath}");
            onBuildFinished(false);
            return;
        }

        SyncEngineDependencies(entry, engineDir, onLogLine);

        RunDotnet("clean", slnPath, entry.Path, onLogLine, cleanSuccess =>
        {
            // On tente le build même si le clean a échoué (ex: rien à nettoyer,
            // ou fichier verrouillé) — seul un échec du BUILD annule le lancement.
            if (!cleanSuccess)
                onLogLine("[EditorLauncher] Le nettoyage a échoué, on tente quand même le build...");

            RunDotnet("build", slnPath, entry.Path, onLogLine, buildSuccess =>
            {
                onBuildFinished(buildSuccess);

                if (buildSuccess)
                    LaunchExe(entry);
            });
        });
    }

    /// <summary>
    /// Resynchronise les dépendances du moteur pour ce projet avant de le
    /// lancer — pas seulement à la création. Délègue à
    /// ProjectScaffolder.RefreshDependencies (même logique que le "Refresh
    /// Dependencies" manuel du menu contextuel), pour ne pas la dupliquer ici.
    /// </summary>
    public static void SyncEngineDependencies(RecentProjectEntry entry, string? engineDir, Action<string> onLogLine)
    {
        if (engineDir == null)
        {
            onLogLine("[EditorLauncher] Dossier moteur inconnu — resynchronisation des dépendances ignorée.");
            return;
        }

        ProjectScaffolder.RefreshDependencies(engineDir, entry.Path, entry.Name, onLogLine);
    }

    static void RunDotnet(string command, string slnPath, string workingDir, Action<string> onLogLine, Action<bool> onFinished)
        => DotnetRunner.Run(command, slnPath, workingDir, onLogLine, onFinished);

    static void LaunchExe(RecentProjectEntry entry)
    {
        string exeDir = Path.Combine(entry.Path, "bin", "Debug", "net10.0");
        string exePath = Path.Combine(exeDir, $"{entry.Name}.exe");

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Build réussi mais exe introuvable : {exePath}");

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = exeDir,
            UseShellExecute = true
        });

        RecentProjectsStore.AddOrUpdate(entry with { LastOpened = DateTime.Now });
    }
}
