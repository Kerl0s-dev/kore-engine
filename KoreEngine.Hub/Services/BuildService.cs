using System.IO;
using KoreEngine.Hub.Models;

namespace KoreEngine.Hub.Services;

/// <summary>
/// "Build" façon Unity : publie le projet Player (headless, GameLoop — voir
/// ProjectScaffolder.WritePlayerProject) en Release, self-contained et en
/// exe unique, puis copie Assets/ à côté.
///
/// RID fixé à win-x64 : tout le reste du moteur (SDL3-CS.Windows*, l'Editor
/// et le Hub en WPF) ne cible de toute façon que Windows — inutile de faire
/// semblant de supporter autre chose ici.
///
/// Self-contained : le joueur n'a pas besoin d'installer le runtime .NET
/// pour lancer le jeu — l'exe embarque tout, au prix d'un exe nettement plus
/// gros (le runtime .NET complet est inclus, en général 60-100 Mo).
///
/// PAS de trimming (PublishTrimmed) : SceneSerializer et ScriptCompiler
/// s'appuient énormément sur la réflexion (Activator.CreateInstance, scan de
/// tous les types chargés) pour retrouver les composants des scripts
/// utilisateur compilés dynamiquement — le trimming casserait ça de façon
/// silencieuse et imprévisible sans un gros travail de configuration des
/// racines de trimming. Pas activé tant que ce n'est pas explicitement demandé.
///
/// Copier Assets/ est nécessaire car un jeu buildé lit SceneManager.
/// AssetsDirectory relatif à SON PROPRE exe (contrairement au projet éditeur,
/// où Assets/ est lu directement depuis les sources du projet) — sans cette
/// copie, le jeu publié n'aurait aucune scène ni asset à charger.
///
/// Limite à connaître : les scripts (.cs) sous Assets/ sont copiés tels
/// quels, en clair — ScriptCompiler doit pouvoir les recompiler au lancement
/// du jeu buildé (Roslyn, pas de précompilation statique dans l'exe). Un
/// joueur curieux peut donc lire le code source des scripts du jeu.
/// </summary>
public static class BuildService
{
    public static void Build(RecentProjectEntry entry, Action<string> onLogLine, Action<bool> onFinished)
    {
        string playerDir = Path.Combine(entry.Path, "Player");
        string playerCsproj = Path.Combine(playerDir, $"{entry.Name}.Player.csproj");
        string outputDir = Path.Combine(entry.Path, "Build");

        if (!File.Exists(playerCsproj))
        {
            onLogLine($"Projet Player introuvable : {playerCsproj}");
            onLogLine("(Ce projet a été créé avant l'ajout du build joueur — recrée-le pour en bénéficier.)");
            onFinished(false);
            return;
        }

        string publishArgs = string.Join(" ", new[]
        {
            "-c Release",
            "-r win-x64",
            "--self-contained false",
            "-p:PublishSingleFile=false",
            "-p:IncludeNativeLibrariesForSelfExtract=false",
            $"-o \"{outputDir}\""
        });

        DotnetRunner.Run("publish", playerCsproj, playerDir, onLogLine, success =>
        {
            if (success)
            {
                CopyAssets(entry.Path, outputDir, onLogLine);
                onLogLine($"[BuildService] Build terminé : {outputDir}");
            }

            onFinished(success);
        }, extraArgs: publishArgs);
    }

    static void CopyAssets(string projectPath, string outputDir, Action<string> onLogLine)
    {
        string sourceAssets = Path.Combine(projectPath, "Assets");
        string destAssets = Path.Combine(outputDir, "Assets");

        if (!Directory.Exists(sourceAssets))
        {
            onLogLine("[BuildService] Aucun dossier Assets/ trouvé — rien à copier.");
            return;
        }

        CopyDirectoryRecursive(sourceAssets, destAssets);
        onLogLine($"[BuildService] Assets copiés : {destAssets}");
    }

    static void CopyDirectoryRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
