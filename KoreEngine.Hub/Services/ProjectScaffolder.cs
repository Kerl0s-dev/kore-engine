using System.IO;

namespace KoreEngine.Hub.Services;
public static class ProjectScaffolder
{
    public static void Create(string engineDir, string targetDir, string projectName, Action<string>? onLog = null)
    {
        void Log(string msg) => onLog?.Invoke(msg);

        string runtimeDll = Path.Combine(engineDir, "KoreEngine.Runtime", "bin", "Debug", "net10.0", "KoreEngine.Runtime.dll");
        string editorDll = Path.Combine(engineDir, "KoreEngine.Editor", "bin", "Debug", "net10.0", "KoreEngine.Editor.dll");

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

        CreateAssetsStructure(projectPath, Log);
        WriteProgramCs(projectPath, projectName, Log);
        WriteCsproj(projectPath, projectName, runtimeDll, editorDll, Log);
        WriteScriptsCsproj(projectPath, projectName, runtimeDll, Log);
        WritePlayerProject(projectPath, projectName, runtimeDll, engineDir, Log);
        WriteSln(projectPath, projectName, Log);
        WriteImGui(engineDir, projectPath, Log);
        WriteEditorIcons(engineDir, projectPath, Log);
        CopyEngineDependencies(engineDir, Path.Combine(projectPath, "bin", "Debug", "net10.0"),
            new[] { "KoreEngine.Runtime", "KoreEngine.Editor" }, Log);

        Log("[ProjectScaffolder] Projet créé avec succès.");
    }

    // ---------------------------------------------------------------
    // 1. Structure Assets
    // ---------------------------------------------------------------

    static void CreateAssetsStructure(string targetDir, Action<string> log)
    {
        string[] dirs = { "bin\\Debug\\net10.0\\" };

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
$@"using KoreEngine.Editor;

class Program
{{
    static void Main()
    {{
        new EditorWindow(""{projectName}"", 1280, 720).Run();
    }}
}}
";
        File.WriteAllText(Path.Combine(targetDir, "Program.cs"), content);
        log($"[ProjectScaffolder] Fichier Program.cs écrit : {targetDir}\\Program.cs");
    }

    // ---------------------------------------------------------------
    // 3. .csproj du jeu — référence UNIQUEMENT la dll compilée du
    //    moteur, jamais son code source.
    // ---------------------------------------------------------------

    static void WriteCsproj(string targetDir, string projectName, string runtimeDll, string editorDll, Action<string> log)
    {
        string content =
$@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <RootNamespace>{projectName}</RootNamespace>
        <AssemblyName>{projectName}</AssemblyName>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include=""ImGui.NET"" Version=""1.91.6.1"" />
        <PackageReference Include=""Microsoft.CodeAnalysis.CSharp"" Version=""5.3.0"" />
        <PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Image"" Version=""3.4.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Mixer"" Version=""3.2.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Shadercross"" Version=""3.0.0.2"" />
        <PackageReference Include=""SDL3-CS.Windows.TTF"" Version=""3.2.2.2"" />
    </ItemGroup>

    <ItemGroup>
    <!-- Les scripts utilisateur ne sont jamais compilés en dur dans l'exe —
        ScriptCompiler les compile à part, au runtime, via Roslyn. -->
        <Compile Remove=""Assets/**/*.cs"" />
    <!-- Le Program.cs du Player (sous-dossier Player/) ne doit pas se
        retrouver mélangé dans CET exe — sinon deux méthodes Main en conflit. -->
        <Compile Remove=""Player/**/*.cs"" />
    </ItemGroup>

    <ItemGroup>
    <!-- Référence binaire uniquement — aucun accès au code source du moteur.
        Les deux dll doivent être compilées au préalable (KoreEngine.Runtime.csproj
        et KoreEngine.Editor.csproj). -->
        <Reference Include=""KoreEngine.Runtime"">
            <HintPath>{runtimeDll}</HintPath>
            <Private>True</Private>
        </Reference>
        <Reference Include=""KoreEngine.Editor"">
            <HintPath>{editorDll}</HintPath>
            <Private>True</Private>
        </Reference>
    </ItemGroup>
</Project>
";
        File.WriteAllText(Path.Combine(targetDir, $"{projectName}.csproj"), content);
        log($"[ProjectScaffolder] Fichier .csproj écrit : {targetDir}\\{projectName}.csproj");
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
			<HintPath>{runtimeDll}</HintPath>
			<Private>False</Private>
		</Reference>
	</ItemGroup>
</Project>
";
        File.WriteAllText(Path.Combine(targetDir, $"{projectName}.Scripts.csproj"), content);
        log($"[ProjectScaffolder] Fichier Scripts.csproj écrit : {targetDir}\\{projectName}.Scripts.csproj");
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
        log($"[ProjectScaffolder] Fichier .sln écrit : {targetDir}\\{projectName}.sln");
    }

    static void WriteImGui(string engineDir, string targetDir, Action<string> log)
    {
        var source = Path.Combine(engineDir, "KoreEngine.Editor", "bin\\Debug\\net10.0\\imgui.ini");
        var destination = Path.Combine(targetDir, "bin\\Debug\\net10.0\\imgui.ini");

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
    /// Recopie les dépendances du moteur (bin/Debug/net10.0 du projet principal
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

        // On copie tout SAUF les dll/pdb du moteur lui-même : ceux-là sont
        // déjà gérés séparément (référencés directement via HintPath) —
        // copier l'ancien binaire ici n'a aucun sens et créerait de la confusion.
        var skip = new[]
        {
            "KoreEngine.Runtime.dll", "KoreEngine.Runtime.pdb",
            "KoreEngine.Editor.dll", "KoreEngine.Editor.pdb"
        };

        foreach (var project in sourceProjects)
        {
            string source = Path.Combine(engineDir, project, "bin", "Debug", "net10.0");

            if (!Directory.Exists(source))
            {
                log($"[ProjectScaffolder] AVERTISSEMENT : dossier de sortie introuvable : {source}");
                continue;
            }

            foreach (var file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);
                if (skip.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                string destFile = Path.Combine(destination, name);
                File.Copy(file, destFile, overwrite: true);
            }
        }

        log($"[ProjectScaffolder] Dépendances copiées : {destination}");
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
using KoreEngine.Engine;

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
        log($"[ProjectScaffolder] Fichier Program.cs (Player) écrit : {playerDir}\\Program.cs");

        string csprojContent =
$@"<Project Sdk=""Microsoft.NET.Sdk"">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <RootNamespace>{projectName}.Player</RootNamespace>
        <AssemblyName>{projectName}</AssemblyName>
        <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    </PropertyGroup>

    <ItemGroup>
    <!-- Pas d'ImGui.NET ici : le Player n'affiche jamais d'éditeur. -->
        <PackageReference Include=""SDL3-CS"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows"" Version=""3.4.10.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Image"" Version=""3.4.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Mixer"" Version=""3.2.4.2"" />
        <PackageReference Include=""SDL3-CS.Windows.Shadercross"" Version=""3.0.0.2"" />
        <PackageReference Include=""SDL3-CS.Windows.TTF"" Version=""3.2.2.2"" />
    </ItemGroup>

    <ItemGroup>
    <!-- Référence binaire uniquement, comme le jeu éditeur — jamais le code
        source du moteur. -->
        <Reference Include=""KoreEngine.Runtime"">
            <HintPath>{runtimeDll}</HintPath>
            <Private>True</Private>
        </Reference>
    </ItemGroup>
</Project>
";
        File.WriteAllText(Path.Combine(playerDir, $"{projectName}.Player.csproj"), csprojContent);
        log($"[ProjectScaffolder] Fichier .csproj (Player) écrit : {playerDir}\\{projectName}.Player.csproj");

        CopyEngineDependencies(engineDir, Path.Combine(playerDir, "bin", "Debug", "net10.0"),
            new[] { "KoreEngine.Runtime" }, log);
    }
}
