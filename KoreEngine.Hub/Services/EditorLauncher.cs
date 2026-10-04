using KoreEngine.Hub.Models;
using System.Diagnostics;

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
    /// <param name="onStage">Étape en cours (synchronisation, nettoyage, compilation, lancement).</param>
    /// <param name="onBuildFinished">true seulement si le build ET le lancement de l'éditeur ont réussi.</param>
    public static void OpenProject(RecentProjectEntry entry, string? engineDir, Action<string> onLogLine, Action<bool> onBuildFinished, Action<string>? onStage = null)
    {
        string slnPath = Path.Combine(entry.Path, $"{entry.Name}.sln");

        if (!File.Exists(slnPath))
        {
            onLogLine($"Solution introuvable : {slnPath}");
            onBuildFinished(false);
            return;
        }

        onStage?.Invoke("Synchronisation des dépendances du moteur...");
        SyncEngineDependencies(entry, engineDir, onLogLine);

        onStage?.Invoke("Nettoyage de la solution...");
        RunDotnet("clean", slnPath, entry.Path, onLogLine, cleanSuccess =>
        {
            // On tente le build même si le clean a échoué (ex: rien à nettoyer,
            // ou fichier verrouillé) — seul un échec du BUILD annule le lancement.
            if (!cleanSuccess)
                onLogLine("[EditorLauncher] Le nettoyage a échoué, on tente quand même le build...");

            onStage?.Invoke("Compilation du projet...");
            RunDotnet("build", slnPath, entry.Path, onLogLine, buildSuccess =>
            {
                if (!buildSuccess)
                {
                    onBuildFinished(false);
                    return;
                }

                onStage?.Invoke("Lancement de l'éditeur...");
                try
                {
                    LaunchExe(entry, onLogLine);
                    onBuildFinished(true);
                }
                catch (Exception ex)
                {
                    onLogLine($"[EditorLauncher] Lancement impossible : {ex.Message}");
                    onBuildFinished(false);
                }
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

    // Si l'éditeur se ferme avec une erreur dans ce délai, on considère qu'il a planté au démarrage.
    const int StartupGraceMs = 4000;

    static void LaunchExe(RecentProjectEntry entry, Action<string> onLogLine)
    {
        string exeDir = Path.Combine(entry.Path, "bin", "Debug", "net10.0");
        string exeName = OperatingSystem.IsWindows() ? $"{entry.Name}.exe" : entry.Name;
        string exePath = Path.Combine(exeDir, exeName);

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Build réussi mais exe introuvable : {exePath}");

        // On redirige la sortie pour VOIR pourquoi l'éditeur plante au démarrage (DLL ou
        // référence manquante, ex. SDL3...). Les flux doivent rester lus pendant toute la vie
        // du processus, sinon il se bloquerait quand le tampon du tube est plein : après le
        // délai de démarrage on continue donc à les vider, mais sans les afficher.
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = exeDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Impossible de démarrer le processus de l'éditeur.");

        int reporting = 1; // 1 = on affiche la sortie, 0 = on la jette
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
            process.WaitForExit(); // vide la sortie asynchrone restante

        Volatile.Write(ref reporting, 0);

        if (exited && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"L'éditeur s'est fermé au démarrage (code {process.ExitCode}). Détails ci-dessus.");

        RecentProjectsStore.AddOrUpdate(entry with { LastOpened = DateTime.Now });
    }
}