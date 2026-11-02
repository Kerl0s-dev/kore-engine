using KoreEngine.Hub.Models;
using System.Diagnostics;

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
            string runtimeDll = Path.Combine(ProjectScaffolder.ResolveEngineOutputDir(engineDir, "KoreEngine.Runtime"), "KoreEngine.Runtime.dll");
            if (!File.Exists(runtimeDll))
            {
                onLogLine($"KoreEngine.Runtime.dll not found : {runtimeDll}");
                onLogLine("Compile the engine first (KoreEngine.Runtime.csproj).");
                onFinished(false);
                return;
            }

            string scriptsDll = Path.Combine(entry.Path, "bin", "Release", "net10.0", $"{entry.Name}.Scripts.dll");
            if (!File.Exists(scriptsDll))
            {
                onLogLine($"{entry.Name}.Scripts.dll not found : {scriptsDll}");
                onLogLine("Open the project in the editor at least once to compile scripts.");
                onFinished(false);
                return;
            }

            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            WritePlayerCsproj(tempDir, entry.Name, runtimeDll);
            WritePlayerProgram(tempDir, entry.Name, startupScene);

            onLogLine("> dotnet publish (Release)...");

            RunDotnet(
                $"publish \"{Path.Combine(tempDir, "Player.csproj")}\" -c Release -r {CurrentRid()} --self-contained true -o \"{outputDir}\"",
                tempDir, onLogLine, publishSuccess =>
                {
                    if (!publishSuccess)
                    {
                        onFinished(false);
                        return;
                    }

                    onLogLine("> Copy of Assets and compiled scripts...");

                    try
                    {
                        FinalizeOutput(entry, scriptsDll, outputDir, onLogLine);
                        onLogLine("Build finished.");
                        onFinished(true);
                    }
                    catch (Exception ex)
                    {
                        onLogLine($"Error during the finale copy : {ex.Message}");
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
            onLogLine($"Error : {ex.Message}");
            onFinished(false);
        }
    }

    static string CurrentRid()
    {
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Architecture not supported for the build.")
        };

        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsLinux()) return $"linux-{arch}";

        throw new PlatformNotSupportedException("KoreEngine only have native binairies for Windows and Linux.");
    }

    static void WritePlayerCsproj(string dir, string projectName, string runtimeDll)
    {
        string content =
$@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <AssemblyName>{projectName}</AssemblyName>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
    </ItemGroup>

{SdlPackages.PlatformItemGroups(includeShadercross: false)}

    <ItemGroup>
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
using KoreEngine;

class Program
{{
    static void Main()
    {{
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, ""{projectName}.Scripts.dll""));

        SceneManager.ScanAndRegisterScenes();

        var loop = new GameLoop(""{projectName}"", 1280, 720);
        AudioManager.Init();

        SceneManager.LoadScene(""{startupScene}"");

        loop.Run();
        AudioManager.Quit();
    }}
}}
";
        File.WriteAllText(Path.Combine(dir, "Program.cs"), content);
    }

    static void FinalizeOutput(RecentProjectEntry entry, string scriptsDll, string outputDir, Action<string> onLogLine)
    {
        foreach (var folder in new[] { "Assets", "Packages" })
        {
            string source = Path.Combine(entry.Path, folder);
            string dest = Path.Combine(outputDir, folder);

            if (Directory.Exists(source))
                CopyDirectory(source, dest);
            else if (folder == "Assets")
                onLogLine($"Warning : no directory name \'Assets\' found in {entry.Path}");
        }

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
            onLogLine($"Not to able launch dotnet : {ex.Message}");
            onFinished(false);
        }
    }
}