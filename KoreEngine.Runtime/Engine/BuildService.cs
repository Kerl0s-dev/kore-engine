using System.Diagnostics;

namespace KoreEngine.Engine;

/// <summary>
/// "Build" façon Unity, déclenché depuis l'éditeur (bouton dans la toolbar) :
/// publie le projet Player (headless, GameLoop — voir
/// ProjectScaffolder.WritePlayerProject côté KoreEngine.Hub) en Release,
/// self-contained et en exe unique (win-x64, seule plateforme ciblée par le
/// reste du moteur), puis copie Assets/ à côté.
///
/// Vivait auparavant côté Hub (bouton par projet dans la liste des projets
/// récents) — déplacé ici pour rester là où le développeur travaille déjà,
/// sans repasser par le Hub juste pour lancer un build.
///
/// PAS de trimming (PublishTrimmed) : SceneSerializer et ScriptCompiler
/// s'appuient énormément sur la réflexion (Activator.CreateInstance, scan de
/// tous les types chargés) pour retrouver les composants des scripts
/// utilisateur compilés dynamiquement — le trimming casserait ça
/// silencieusement sans un gros travail de configuration des racines de
/// trimming. Pas activé tant que ce n'est pas explicitement demandé.
///
/// Copier Assets/ est nécessaire car un jeu buildé lit SceneManager.
/// AssetsDirectory relatif à SON PROPRE exe (contrairement au projet éditeur,
/// où Assets/ est lu directement depuis les sources du projet).
/// </summary>
public static class BuildService
{
    public static bool IsBuilding { get; private set; }

    public static void Build(string projectPath, string projectName, Action<string> onLogLine, Action<bool> onFinished)
    {
        if (IsBuilding)
        {
            onLogLine("[BuildService] Un build est déjà en cours.");
            return;
        }

        string playerDir = Path.Combine(projectPath, "Player");
        string playerCsproj = Path.Combine(playerDir, $"{projectName}.Player.csproj");
        string outputDir = Path.Combine(projectPath, "Build");

        if (!File.Exists(playerCsproj))
        {
            onLogLine($"[BuildService] Projet Player introuvable : {playerCsproj}");
            onLogLine("[BuildService] (Recrée le projet depuis le Hub pour bénéficier du build joueur.)");
            onFinished(false);
            return;
        }

        string args = string.Join(" ", new[]
        {
            "publish",
            $"\"{playerCsproj}\"",
            "-c Release",
            "-r win-x64",
            "--self-contained true",
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            $"-o \"{outputDir}\""
        });

        IsBuilding = true;
        onLogLine($"> dotnet {args}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = playerDir,
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

            if (success)
            {
                CopyMissingDependencies(playerDir, outputDir, onLogLine);
                CopyAssets(projectPath, outputDir, onLogLine);
                onLogLine($"[BuildService] Build terminé : {outputDir}");
            }

            IsBuilding = false;
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
            onLogLine($"[BuildService] Impossible de lancer dotnet publish : {ex.Message}");
            IsBuilding = false;
            onFinished(false);
        }
    }

    /// <summary>
    /// KoreEngine.Runtime est référencée par le Player en <Reference><HintPath>
    /// brut (pas en PackageReference/ProjectReference), donc "dotnet publish"
    /// n'a AUCUNE visibilité sur ses propres dépendances (Microsoft.CodeAnalysis,
    /// Microsoft.CodeAnalysis.CSharp, System.Collections.Immutable, etc.) —
    /// seule KoreEngine.Runtime.dll elle-même est copiée (Private=True suffit
    /// pour ça, c'est une simple copie locale MSBuild). Sans ce complément,
    /// ScriptCompiler plante au premier lancement du jeu buildé avec un
    /// FileNotFoundException sur Microsoft.CodeAnalysis.
    ///
    /// Deux sources possibles, dans cet ordre :
    ///   1. Player/bin/Debug/net10.0 — rempli par
    ///      ProjectScaffolder.CopyEngineDependencies/RefreshDependencies, MAIS
    ///      seulement si le projet a déjà été ouvert au moins une fois depuis
    ///      le Hub, ou "Refresh Dependencies" utilisé manuellement.
    ///   2. Le dossier de L'ÉDITEUR EN COURS D'EXÉCUTION (AppContext.
    ///      BaseDirectory) — TOUJOURS fiable : si l'éditeur tourne et que la
    ///      compilation de scripts y fonctionne déjà (hot-reload), c'est que
    ///      son propre dossier a forcément ces DLL. Sert de filet de
    ///      sécurité si (1) n'a jamais été fait pour ce projet.
    /// Ni l'une ni l'autre n'écrase ce que le publish a lui-même correctement
    /// résolu (son propre runtime .NET packagé notamment) — on ne complète
    /// QUE ce qui manque encore après chaque source, dans l'ordre.
    /// </summary>
    static void CopyMissingDependencies(string playerDir, string outputDir, Action<string> onLogLine)
    {
        string playerDebugBin = Path.Combine(playerDir, "bin", "Debug", "net10.0");
        string editorBin = AppContext.BaseDirectory;

        int copied = 0;
        copied += CopyMissingFrom(playerDebugBin, outputDir);
        copied += CopyMissingFrom(editorBin, outputDir);

        onLogLine($"[BuildService] {copied} dépendance(s) manquante(s) complétée(s) (Player/bin puis dossier de l'éditeur).");
    }

    static int CopyMissingFrom(string sourceDir, string outputDir)
    {
        if (!Directory.Exists(sourceDir)) return 0;

        int copied = 0;

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string name = Path.GetFileName(file);

            // Jamais copier l'exe/dll/pdb du projet lui-même depuis le dossier
            // de l'éditeur — ce sont des artefacts DIFFÉRENTS (build éditeur
            // vs build Player), les mélanger corromprait le dossier publié.
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            string destFile = Path.Combine(outputDir, name);
            if (File.Exists(destFile)) continue; // déjà fourni par le publish, on ne touche pas

            try
            {
                File.Copy(file, destFile);
                copied++;
            }
            catch
            {
                // Fichier verrouillé ou autre — tant pis pour celui-là, on continue.
            }
        }

        return copied;
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
