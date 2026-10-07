using System.Runtime.InteropServices;

namespace KoreEngine.Hub.Services;

public static class ProjectScaffolder
{
    public static void Create(string engineDir, string targetDir, string projectName, Action<string>? onLog = null)
    {
        void Log(string msg) => onLog?.Invoke(msg);

        string runtimeOutputDir = ResolveEngineOutputDir(engineDir, "KoreEngine.Runtime");
        string editorOutputDir = ResolveEngineOutputDir(engineDir, "KoreEngine.Editor");
        string runtimeDll = Path.Combine(runtimeOutputDir, "KoreEngine.Runtime.dll");
        string editorDll = Path.Combine(editorOutputDir, "KoreEngine.Editor.dll");

        foreach (var (label, path) in new[] { ("Runtime", runtimeDll), ("Editor", editorDll) })
        {
            if (!File.Exists(path))
                throw new Exception(
                    $"KoreEngine.{label}.dll introuvable : {path}\n" +
                    $"Compile d'abord KoreEngine.{label}.csproj avant de créer un nouveau projet.");
        }

        string projectPath = Path.Combine(targetDir, projectName);

        if (Directory.Exists(projectPath) && Directory.GetFileSystemEntries(projectPath).Length > 0)
            throw new Exception($"Le dossier cible n'est pas vide : {projectPath}");

        Directory.CreateDirectory(projectPath);

        Log($"[ProjectScaffolder] Création du projet '{projectName}' dans {targetDir}");

        File.Copy(Path.Combine(engineDir, "icon.ico"), Path.Combine(projectPath, "icon.ico"));
        CreateAssetsStructure(projectPath, Log);
        WriteProgramCs(projectPath, projectName, Log);
        WriteCsproj(projectPath, projectName, runtimeDll, editorDll, Log);
        WriteScriptsCsproj(projectPath, projectName, runtimeDll, Log);
        WritePlayerProject(projectPath, projectName, runtimeDll, engineDir, Log);
        WriteSln(projectPath, projectName, Log);
        WriteImGui(engineDir, projectPath, Log);
        WriteEditorIcons(engineDir, projectPath, Log);
        CopyEngineDependencies(engineDir, Path.Combine(projectPath, "bin", "Debug", "net10.0"),
            ["KoreEngine.Runtime", "KoreEngine.Editor"], Log);

        Log("[ProjectScaffolder] Projet créé avec succès.");
    }

    // Resolve the engine binaries from Release when available (the normal
    // distribution/publish configuration), while keeping Debug as a fallback
    // for development builds. Generated projects still use their own Debug
    // output directory, so this only changes where engine binaries are read from.
    public static string ResolveEngineOutputDir(string engineDir, string projectName)
    {
        string projectDir = Path.Combine(engineDir, projectName);
        string binDir = Path.Combine(projectDir, "bin");

        // dotnet can place outputs in many valid layouts, for example:
        //   bin/Debug/net10.0/
        //   bin/Release/net10.0/
        //   bin/Release/net10.0/linux-x64/
        //   bin/Release/net10.0/linux-x64/publish/
        // and the equivalent Windows/RID layouts. Do not assume one of them.
        if (!Directory.Exists(binDir))
            return Path.Combine(binDir, "Release", "net10.0");

        string dllName = projectName + ".dll";
        var candidates = Directory.GetFiles(binDir, dllName, SearchOption.AllDirectories)
            .Select(path => new { Path = path, Score = ScoreEngineOutput(path) })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => File.GetLastWriteTimeUtc(x.Path))
            .Select(x => Path.GetDirectoryName(x.Path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count > 0
            ? candidates[0]
            : Path.Combine(binDir, "Release", "net10.0");
    }

    static int ScoreEngineOutput(string dllPath)
    {
        string normalized = dllPath.Replace('\\', '/');
        string lower = normalized.ToLowerInvariant();
        int score = 0;

        // Prefer the current runtime identifier when a RID-specific build exists.
        string? rid = CurrentNativeRid();
        if (rid != null && lower.Contains("/" + rid + "/")) score += 1000;

        // A publish directory is the most complete output (native files included).
        if (lower.EndsWith("/publish/" + Path.GetFileName(dllPath).ToLowerInvariant())) score += 500;
        if (lower.Contains("/publish/")) score += 400;

        // Prefer Release over Debug, but both are valid.
        if (lower.Contains("/release/")) score += 200;
        if (lower.Contains("/debug/")) score += 100;

        // Prefer the requested target framework, while still allowing any TFM
        // dotnet actually generated if the project changes in the future.
        if (lower.Contains("/net10.0/")) score += 50;

        return score;
    }

    // ---------------------------------------------------------------
    // 1. Structure Assets
    // ---------------------------------------------------------------

    static void CreateAssetsStructure(string targetDir, Action<string> log)
    {
        string[] dirs = { Path.Combine("bin", "Debug", "net10.0") };

        foreach (var dir in dirs)
        {
            Directory.CreateDirectory(Path.Combine(targetDir, dir));
            log($"[ProjectScaffolder] Crée : {dir} dans {targetDir}");
        }
    }

    // ---------------------------------------------------------------
    // 2. Program.cs minimal
    // ---------------------------------------------------------------

    static void WriteProgramCs(string targetDir, string projectName, Action<string> log)
    {
        string content =
$@"using KoreEngine;

class Program
{{
    static void Main()
    {{
        new EditorWindow(""{projectName}"", 1280, 720).Run();
    }}
}}
";
        File.WriteAllText(Path.Combine(targetDir, "Program.cs"), content);
        log($"[ProjectScaffolder] Fichier Program.cs écrit : {targetDir}{Path.DirectorySeparatorChar}Program.cs");
    }

    // ---------------------------------------------------------------
    // 3. .csproj du jeu — référence UNIQUEMENT la dll compilée du
    //    moteur, jamais son code source.
    // ---------------------------------------------------------------

    static void WriteCsproj(string targetDir, string projectName, string runtimeDll, string editorDll, Action<string> log)
    {
        string runtimeBinDir = Path.GetDirectoryName(runtimeDll)!;
        string editorBinDir = Path.GetDirectoryName(editorDll)!;

        string rid = CurrentNativeRid()
            ?? throw new PlatformNotSupportedException("Plateforme/RID non supporté pour le projet généré.");

        string content =
    $@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RuntimeIdentifier>{rid}</RuntimeIdentifier>
        <SelfContained>true</SelfContained>
        <UseAppHost>true</UseAppHost>
        <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <RootNamespace>{projectName}</RootNamespace>
        <AssemblyName>{projectName}</AssemblyName>
        <ApplicationIcon Condition=""'$(OS)' == 'Windows_NT'"">{targetDir}/icon.ico</ApplicationIcon>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include=""ImGui.NET"" Version=""1.91.6.1"" />
        <PackageReference Include=""Microsoft.CodeAnalysis.CSharp"" Version=""5.3.0"" />
        <PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
    </ItemGroup>

{SdlPackages.PlatformItemGroups(includeShadercross: true)}

    <ItemGroup>
        <Compile Remove=""Assets/**/*.cs"" />
        <Compile Remove=""Player/**/*.cs"" />
    </ItemGroup>

    <ItemGroup>
        <Reference Include=""KoreEngine.Runtime"">
            <HintPath>bin/Debug/net10.0/KoreEngine.Runtime.dll</HintPath>
            <Private>True</Private>
        </Reference>
        <Reference Include=""KoreEngine.Editor"">
            <HintPath>bin/Debug/net10.0/KoreEngine.Editor.dll</HintPath>
            <Private>True</Private>
        </Reference>
    </ItemGroup>

    <!--
      Les références de plateforme SDL3-CS ci-dessus fournissent les .so/.dll
      natifs correspondant à l'OS du build. On ne copie donc plus à la main
      un dossier runtimes basé sur NETCoreSdkRuntimeIdentifier : cette propriété
      peut être vide ou différente du RID réellement publié sous Linux.
    -->

</Project>
";
        File.WriteAllText(Path.Combine(targetDir, $"{projectName}.csproj"), content);
        log($"[ProjectScaffolder] Fichier .csproj écrit : {targetDir}{Path.DirectorySeparatorChar}{projectName}.csproj");
    }

    // ---------------------------------------------------------------
    // 4. Scripts.csproj — même référence dll, pour l'édition des
    //    scripts avec IntelliSense, isolé du build réel.
    // ---------------------------------------------------------------

    static void WriteScriptsCsproj(string targetDir, string projectName, string runtimeDll, Action<string> log)
    {
        string content =
$@"<Project Sdk=""Microsoft.NET.Sdk"">

	<PropertyGroup>
		<OutputType>Library</OutputType>
		<TargetFramework>net10.0</TargetFramework>
		<ImplicitUsings>enable</ImplicitUsings>
		<Nullable>enable</Nullable>
		<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
		<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
	</PropertyGroup>

	<ItemGroup>
		<Compile Include=""Assets/**/*.cs"" />
	</ItemGroup>

	<ItemGroup>
	<!-- Les scripts n'ont besoin que des types managés SDL (ex: SDL.Keycode
		utilisé par InputAction.BindKey) — pas des binaires natifs Windows,
		qui ne servent qu'à l'exécution, jamais à l'IntelliSense. -->
		<PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
	</ItemGroup>

	<ItemGroup>
	<!-- Uniquement Runtime : un script gameplay n'a jamais besoin d'ImGui
		ni de l'Editor. -->
		<Reference Include=""KoreEngine.Runtime"">
			<HintPath>bin/Debug/net10.0/KoreEngine.Runtime.dll</HintPath>
			<Private>False</Private>
		</Reference>
	</ItemGroup>
</Project>
";
        File.WriteAllText(Path.Combine(targetDir, $"{projectName}.Scripts.csproj"), content);
        log($"[ProjectScaffolder] Fichier Scripts.csproj écrit : {targetDir}{Path.DirectorySeparatorChar}{projectName}.Scripts.csproj");
    }

    // ---------------------------------------------------------------
    // 5. Solution unique : contient le jeu ET les scripts, aucun des
    //    deux ne référence le code source du moteur.
    // ---------------------------------------------------------------

    static void WriteSln(string targetDir, string projectName, Action<string> log)
    {
        string gameGuid = Guid.NewGuid().ToString("B").ToUpper();
        string scriptsGuid = Guid.NewGuid().ToString("B").ToUpper();
        string playerGuid = Guid.NewGuid().ToString("B").ToUpper();
        string typeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";

        string content =
$@"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{typeGuid}"") = ""{projectName}"", ""{projectName}.csproj"", ""{gameGuid}""
EndProject
Project(""{typeGuid}"") = ""{projectName}.Scripts"", ""{projectName}.Scripts.csproj"", ""{scriptsGuid}""
EndProject
Project(""{typeGuid}"") = ""{projectName}.Player"", ""Player\{projectName}.Player.csproj"", ""{playerGuid}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{gameGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{gameGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{gameGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{gameGuid}.Release|Any CPU.Build.0 = Release|Any CPU
		{scriptsGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{scriptsGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{scriptsGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{scriptsGuid}.Release|Any CPU.Build.0 = Release|Any CPU
		{playerGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{playerGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{playerGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{playerGuid}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
";
        File.WriteAllText(Path.Combine(targetDir, $"{projectName}.sln"), content);
        log($"[ProjectScaffolder] Fichier .sln écrit : {targetDir}{Path.DirectorySeparatorChar}{projectName}.sln");
    }

    static void WriteImGui(string engineDir, string targetDir, Action<string> log)
    {
        var source = Path.Combine(ResolveEngineOutputDir(engineDir, "KoreEngine.Editor"), "imgui.ini");
        var destination = Path.Combine(targetDir, "bin", "Debug", "net10.0", "imgui.ini");

        if (File.Exists(destination))
            File.Delete(destination);

        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: true);
            log($"[ProjectScaffolder] Fichier imgui.ini écrit : {destination}");
        }
        else
        {
            log($"[ProjectScaffolder] AVERTISSEMENT : imgui.ini introuvable dans {engineDir}");
        }
    }

    static void WriteEditorIcons(string engineDir, string targetDir, Action<string> log)
    {
        string source = Path.Combine(engineDir, "KoreEngine.Editor", "Icons");
        string destination = Path.Combine(targetDir, "bin", "Debug", "net10.0", "Editor", "Icons");

        if (!Directory.Exists(source))
        {
            log($"[ProjectScaffolder] AVERTISSEMENT : dossier d'icônes introuvable : {source}");
            return;
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        log($"[ProjectScaffolder] Icônes éditeur copiées : {destination}");
    }

    /// <summary>
    /// Recopie les dépendances du moteur (bin/Release/net10.0 si disponible,
    /// sinon bin/Debug/net10.0) du projet principal
    /// ET de Player/, s'il existe) sans rien recréer d'autre — utile quand le
    /// moteur a gagné une nouvelle dépendance (ex: Microsoft.CodeAnalysis.CSharp
    /// ajoutée à KoreEngine.Runtime) après la création du projet : la copie
    /// figée à la création ne se met jamais à jour toute seule.
    ///
    /// Prérequis : KoreEngine.Runtime/Editor doivent déjà avoir été recompilés
    /// avec la nouvelle dépendance AVANT d'appeler ceci — sinon il n'y a
    /// simplement rien à copier.
    /// </summary>
    public static void RefreshDependencies(string engineDir, string projectPath, string projectName, Action<string>? onLog = null)
    {
        void Log(string msg) => onLog?.Invoke(msg);

        CopyEngineDependencies(engineDir, Path.Combine(projectPath, "bin", "Debug", "net10.0"),
            new[] { "KoreEngine.Runtime", "KoreEngine.Editor" }, Log);

        string playerBin = Path.Combine(projectPath, "Player", "bin", "Debug", "net10.0");
        if (Directory.Exists(Path.Combine(projectPath, "Player")))
            CopyEngineDependencies(engineDir, playerBin, new[] { "KoreEngine.Runtime" }, Log);
        else
            Log("[ProjectScaffolder] Pas de dossier Player/ (projet créé avant le build joueur) — ignoré.");

        Log("[ProjectScaffolder] Dépendances rafraîchies.");
    }

    // ---------------------------------------------------------------
    // Copie les dépendances runtime du moteur (ImGui.NET.dll, binaires
    // natifs SDL3, Microsoft.CodeAnalysis.CSharp.dll, etc.) vers le dossier
    // de sortie d'un projet. Nécessaire car une référence binaire simple
    // (<Reference><HintPath>) ne copie que KoreEngine.dll elle-même, jamais
    // ses propres dépendances — contrairement à ProjectReference/
    // PackageReference qui les résolvent transitivement via NuGet.
    //
    // sourceProjects contrôle QUELS dossiers de sortie du moteur copier :
    // juste Runtime pour le Player (headless, pas d'ImGui), Runtime+Editor
    // pour le jeu éditeur (a besoin d'ImGui.NET etc.).
    // ---------------------------------------------------------------

    public static void CopyEngineDependencies(string engineDir, string destination, string[] sourceProjects, Action<string> log)
    {
        Directory.CreateDirectory(destination);

        // The generated project references the engine DLLs from its own
        // bin/Debug/net10.0 directory, so those DLLs MUST be copied here.
        // Only debug symbols are optional. Never skip the engine assemblies.
        var skip = new[]
        {
            "KoreEngine.Runtime.pdb",
            "KoreEngine.Editor.pdb"
        };

        foreach (var project in sourceProjects)
        {
            string source = ResolveEngineOutputDir(engineDir, project);

            if (!Directory.Exists(source))
            {
                log($"[ProjectScaffolder] AVERTISSEMENT : dossier de sortie introuvable : {source}");
                continue;
            }

            // Copie récursive de tous les fichiers ET sous-dossiers (notamment /runtimes)
            CopyDirectoryRecursive(source, destination, skip, log);

            CopyNativesToRoot(source, destination, log);
        }

        log($"[ProjectScaffolder] Dépendances copiées : {destination}");
    }

    /// <summary>
    /// Copie aussi les binaires natifs de l'OS courant (runtimes/{rid}/native/*) à la RACINE
    /// du dossier de sortie. Dans le moteur, SDL3.dll n'existe que dans runtimes/win-x64/native :
    /// il n'est trouvé que si le deps.json du projet le déclare, ce qui n'est pas fiable
    /// (DllNotFoundException 'SDL3' au démarrage de l'éditeur). À la racine, à côté de l'exe,
    /// il est toujours trouvé.
    /// </summary>
    static void CopyNativesToRoot(string engineOutputDir, string destination, Action<string> log)
    {
        string? rid = CurrentNativeRid();
        if (rid == null) return;

        // Published .NET applications may put native assets either under
        // runtimes/<rid>/native or directly beside the executable. Search
        // both layouts instead of assuming one particular SDK output shape.
        var nativeFiles = new List<string>();

        string nativeDir = Path.Combine(engineOutputDir, "runtimes", rid, "native");
        if (Directory.Exists(nativeDir))
            nativeFiles.AddRange(Directory.GetFiles(nativeDir, "*", SearchOption.AllDirectories));

        foreach (var file in Directory.GetFiles(engineOutputDir, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (OperatingSystem.IsLinux() && name.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                nativeFiles.Add(file);
            else if (OperatingSystem.IsWindows() && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                nativeFiles.Add(file);
            else if (OperatingSystem.IsMacOS() &&
                     (name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)))
                nativeFiles.Add(file);
        }

        nativeFiles = nativeFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (nativeFiles.Count == 0)
        {
            log($"[ProjectScaffolder] AVERTISSEMENT : aucun binaire natif trouvé pour {rid} dans {engineOutputDir}");
            return;
        }

        foreach (var file in nativeFiles)
        {
            try
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }
            catch (IOException ex)
            {
                log($"[ProjectScaffolder] Natif non copié ({Path.GetFileName(file)}) : {ex.Message}");
            }
        }
    }

    static string? CurrentNativeRid()
    {
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => ""
        };
        if (arch.Length == 0) return null;

        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsLinux()) return $"linux-{arch}";
        if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
        return null;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir, string[] skipFiles, Action<string> log)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            if (skipFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase)) continue;

            string destFile = Path.Combine(destinationDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            string destSubDir = Path.Combine(destinationDir, dirName);
            CopyDirectoryRecursive(subDir, destSubDir, Array.Empty<string>(), log);
        }
    }

    // ---------------------------------------------------------------
    // Projet "Player" : build headless (GameLoop, pas d'ImGui/Editor) pour
    // un jeu buildé/distribuable — Player/{projectName}.Player.csproj, dans
    // un sous-dossier pour que le glob par défaut du .csproj principal ne
    // ramasse pas son Program.cs (et vice-versa).
    // ---------------------------------------------------------------

    static void WritePlayerProject(string projectPath, string projectName, string runtimeDll, string engineDir, Action<string> log)
    {
        string playerDir = Path.Combine(projectPath, "Player");
        Directory.CreateDirectory(playerDir);

        string programContent =
$@"using System;
using System.IO;
using System.Reflection;
using System.Linq;
using KoreEngine;

class Program
{{
    static void Main()
    {{
        // 1. Charge la DLL de scripts précompilée au build (ex: GameScripts.dll)
        string scriptsDllPath = Path.Combine(AppContext.BaseDirectory, ""GameScripts.dll"");
        if (File.Exists(scriptsDllPath))
        {{
            Assembly.LoadFrom(scriptsDllPath);
        }}

        // 2. Initialise et lance la boucle de jeu sans AUCUNE compilation
        SceneManager.ScanAndRegisterScenes();
        ProjectSettings.Load();

        string? startupScene = ProjectSettings.StartupScene;
        string? firstScene = SceneManager.SceneFiles().FirstOrDefault();

        if (startupScene != null)
            SceneManager.LoadScene(startupScene);
        else if (firstScene != null)
            SceneManager.LoadSceneFromFile(firstScene);
        else
            throw new Exception(""Aucune scène trouvée dans Assets/ — impossible de démarrer."");

        AudioManager.Init();

        var loop = new GameLoop(""{projectName}"", 1280, 720);
        loop.Run();

        AudioManager.Quit();
    }}
}}
";
        File.WriteAllText(Path.Combine(playerDir, "Program.cs"), programContent);
        log($"[ProjectScaffolder] Fichier Program.cs (Player) écrit : {playerDir}{Path.DirectorySeparatorChar}Program.cs");

        string rid = CurrentNativeRid()
            ?? throw new PlatformNotSupportedException("Plateforme/RID non supporté pour le Player généré.");

        string csprojContent =
$@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <RuntimeIdentifier>{rid}</RuntimeIdentifier>
        <SelfContained>true</SelfContained>
        <UseAppHost>true</UseAppHost>
        <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <RootNamespace>{projectName}.Player</RootNamespace>
        <AssemblyName>{projectName}</AssemblyName>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>

    <ItemGroup>
    <!-- Pas d'ImGui.NET ici : le Player n'affiche jamais d'éditeur. -->
        <PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
    </ItemGroup>

{SdlPackages.PlatformItemGroups(includeShadercross: true)}

    <ItemGroup>
    <!-- Référence binaire uniquement, comme le jeu éditeur — jamais le code
        source du moteur. -->
        <Reference Include=""KoreEngine.Runtime"">
            <HintPath>bin/Debug/net10.0/KoreEngine.Runtime.dll</HintPath>
            <Private>True</Private>
        </Reference>
    </ItemGroup>
</Project>
";
        File.WriteAllText(Path.Combine(playerDir, $"{projectName}.Player.csproj"), csprojContent);
        log($"[ProjectScaffolder] Fichier .csproj (Player) écrit : {playerDir}{Path.DirectorySeparatorChar}{projectName}.Player.csproj");

        CopyEngineDependencies(engineDir, Path.Combine(playerDir, "bin", "Debug", "net10.0"),
            new[] { "KoreEngine.Runtime" }, log);
    }
}