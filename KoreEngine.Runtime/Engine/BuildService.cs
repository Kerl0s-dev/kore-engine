using System.Diagnostics;

namespace KoreEngine.Engine;

/// <summary>
/// "Build" déclenché depuis l'éditeur :
/// Publie le projet Player en Release self-contained (win-x64),
/// puis copie le dossier Assets/ à côté de l'exécutable.
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

        // Arguments de publication .NET
        // Le Player contient déjà les scripts précompilés sous forme de DLL par l'éditeur.
        string args = string.Join(" ", new[]
        {
            "publish",
            $"\"{playerCsproj}\"",
            "-c Release",
            "-r win-x64",
            "--self-contained true",
            // Options optionnelles si tu veux un fichier binaire unique :
            // "-p:PublishSingleFile=true",
            // "-p:IncludeNativeLibrariesForSelfExtract=true",
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
                CopyAssets(projectPath, outputDir, onLogLine);
                onLogLine($"[BuildService] Build terminé avec succès : {outputDir}");
            }
            else
            {
                onLogLine($"[BuildService] Le build a échoué avec le code de sortie {proc.ExitCode}.");
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

    private static void CopyAssets(string projectPath, string outputDir, Action<string> onLogLine)
    {
        string sourceAssets = Path.Combine(projectPath, "Assets");
        string destAssets = Path.Combine(outputDir, "Assets");

        if (!Directory.Exists(sourceAssets))
        {
            onLogLine("[BuildService] Aucun dossier Assets/ trouvé — rien à copier.");
            return;
        }

        // Nettoyage de l'ancien dossier d'assets s'il existe (pour supprimer les fichiers obsolètes)
        if (Directory.Exists(destAssets))
        {
            try
            {
                Directory.Delete(destAssets, true);
            }
            catch (Exception ex)
            {
                onLogLine($"[BuildService] Avertissement : impossible de nettoyer le dossier {destAssets} : {ex.Message}");
            }
        }

        CopyDirectoryRecursive(sourceAssets, destAssets);
        onLogLine($"[BuildService] Assets copiés avec succès dans : {destAssets}");
    }

    static void CopyDirectoryRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            // Ne PAS copier les sources C# dans le jeu compilé !
            if (Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}