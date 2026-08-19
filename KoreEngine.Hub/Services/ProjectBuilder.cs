using System.Diagnostics;
using System.IO;
using KoreEngine.Hub.Models;

namespace KoreEngine.Hub.Services;

/// <summary>
/// Génère un exécutable "joueur" autonome pour un projet, via GameLoop
/// (KoreEngine.Runtime) — code qui existait déjà dans le moteur mais n'était
/// jusqu'ici jamais instancié : ProjectScaffolder ne génère que des
/// Program.cs qui lancent EditorWindow (ImGui + Editor).
///
/// Fonctionnement : génère un mini projet "Player" éphémère à côté du projet
/// (référence UNIQUEMENT KoreEngine.Runtime.dll — jamais KoreEngine.Editor.dll
/// ni Roslyn), le publish en Release, puis copie Assets/ et le
/// {Nom}.Scripts.dll déjà compilé par la solution (le même que celui utilisé
/// pour l'IntelliSense, voir ProjectScaffolder.WriteScriptsCsproj) à côté de
/// l'exe. Les scripts ne sont donc PAS recompilés au lancement du jeu buildé
/// — juste chargés par réflexion — contrairement à l'éditeur qui les
/// recompile à la volée via Roslyn pour le hot-reload.
/// </summary>
public static class ProjectBuilder
{
    public static void Build(
        RecentProjectEntry entry,
        string engineDir,
        string startupScene,
        string outputDir,
        Action<string> onLogLine,
        Action<bool> onFinished)
    {
        string tempDir = Path.Combine(entry.Path, ".player-build");

        try
        {
            string runtimeDll = Path.Combine(engineDir, "KoreEngine.Runtime", "bin", "Debug", "net10.0", "KoreEngine.Runtime.dll");
            if (!File.Exists(runtimeDll))
            {
                onLogLine($"KoreEngine.Runtime.dll introuvable : {runtimeDll}");
                onLogLine("Compile d'abord le moteur (KoreEngine.Runtime.csproj).");
                onFinished(false);
                return;
            }

            string scriptsDll = Path.Combine(entry.Path, "bin", "Debug", "net10.0", $"{entry.Name}.Scripts.dll");
            if (!File.Exists(scriptsDll))
            {
                onLogLine($"{entry.Name}.Scripts.dll introuvable : {scriptsDll}");
                onLogLine("Ouvre d'abord le projet dans l'éditeur au moins une fois pour compiler les scripts.");
                onFinished(false);
                return;
            }

            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            WritePlayerCsproj(tempDir, entry.Name, runtimeDll);
            WritePlayerProgram(tempDir, entry.Name, startupScene);

            onLogLine("> dotnet publish (Release)...");

            RunDotnet(
                $"publish \"{Path.Combine(tempDir, "Player.csproj")}\" -c Release -o \"{outputDir}\"",
                tempDir, onLogLine, publishSuccess =>
                {
                    if (!publishSuccess)
                    {
                        onFinished(false);
                        return;
                    }

                    onLogLine("> Copie des Assets et des scripts compilés...");

                    try
                    {
                        FinalizeOutput(entry, scriptsDll, outputDir, onLogLine);
                        onLogLine("Build terminé.");
                        onFinished(true);
                    }
                    catch (Exception ex)
                    {
                        onLogLine($"Erreur pendant la copie finale : {ex.Message}");
                        onFinished(false);
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, recursive: true); } catch { /* pas bloquant */ }
                    }
                });
        }
        catch (Exception ex)
        {
            onLogLine($"Erreur : {ex.Message}");
            onFinished(false);
        }
    }

    static void WritePlayerCsproj(string dir, string projectName, string runtimeDll)
    {
        string content =
$@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <AssemblyName>{projectName}</AssemblyName>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Image"" Version=""3.4.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Mixer"" Version=""3.2.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.TTF"" Version=""3.2.2.2"" />
    </ItemGroup>

    <ItemGroup>
    <!-- Référence binaire uniquement, comme pour le projet principal —
        jamais le code source du moteur. Pas de référence à
        KoreEngine.Editor : le joueur buildé n'a aucune dépendance
        ImGui/Roslyn. -->
        <Reference Include=""KoreEngine.Runtime"">
            <HintPath>{runtimeDll}</HintPath>
            <Private>True</Private>
        </Reference>
    </ItemGroup>
</Project>
";
        File.WriteAllText(Path.Combine(dir, "Player.csproj"), content);
    }

    static void WritePlayerProgram(string dir, string projectName, string startupScene)
    {
        string content =
$@"using System.Reflection;
using KoreEngine.Engine;

class Program
{{
    static void Main()
    {{
        // Les scripts utilisateur ne sont PAS référencés à la compilation
        // (Player.csproj ne connaît que KoreEngine.Runtime) — on les charge
        // nous-même au runtime pour que SceneSerializer.FindType (réflexion)
        // puisse retrouver les types de composants en désérialisant la scène.
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, ""{projectName}.Scripts.dll""));

        SceneManager.ScanAndRegisterScenes();
        SceneManager.LoadScene(""{startupScene}"");

        new GameLoop(""{projectName}"", 1280, 720).Run();
    }}
}}
";
        File.WriteAllText(Path.Combine(dir, "Program.cs"), content);
    }

    static void FinalizeOutput(RecentProjectEntry entry, string scriptsDll, string outputDir, Action<string> onLogLine)
    {
        string assetsSource = Path.Combine(entry.Path, "Assets");
        string assetsDest = Path.Combine(outputDir, "Assets");

        if (Directory.Exists(assetsSource))
            CopyDirectory(assetsSource, assetsDest);
        else
            onLogLine($"Attention : aucun dossier Assets trouvé dans {entry.Path}");

        File.Copy(scriptsDll, Path.Combine(outputDir, Path.GetFileName(scriptsDll)), overwrite: true);

        string scriptsPdb = Path.ChangeExtension(scriptsDll, ".pdb");
        if (File.Exists(scriptsPdb))
            File.Copy(scriptsPdb, Path.Combine(outputDir, Path.GetFileName(scriptsPdb)), overwrite: true);
    }

    static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var subDir in Directory.GetDirectories(source))
            CopyDirectory(subDir, Path.Combine(dest, Path.GetFileName(subDir)));
    }

    static void RunDotnet(string arguments, string workingDir, Action<string> onLogLine, Action<bool> onFinished)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
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
            onFinished(proc.ExitCode == 0);
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
            onLogLine($"Impossible de lancer dotnet : {ex.Message}");
            onFinished(false);
        }
    }
}
