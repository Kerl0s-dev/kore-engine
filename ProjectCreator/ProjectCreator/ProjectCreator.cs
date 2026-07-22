namespace KoreEngine.ProjectCreator
{
    /// <summary>
    /// Scaffold un nouveau projet KoreEngine, façon "New Project" Unity :
    ///   - référence UNIQUEMENT KoreEngine.Runtime.dll et KoreEngine.Editor.dll
    ///     déjà compilées (HintPath), jamais le code source du moteur — ni le
    ///     jeu ni les scripts n'ont accès aux sources de KoreEngine
    ///   - une seule solution générée ({projectName}.sln), contenant le
    ///     projet du jeu et le projet Scripts
    ///   - crée un Program.cs minimal qui lance EditorWindow
    ///
    /// Usage :
    ///   ProjectTemplate.Create(
    ///       engineDir:   "D:\\KoreEngine",           // racine contenant KoreEngine.Runtime/ et KoreEngine.Editor/
    ///       targetDir:   "C:/MyGames/MonJeu",
    ///       projectName: "MonJeu");
    ///
    /// Prérequis : KoreEngine.Runtime.csproj ET KoreEngine.Editor.csproj doivent
    /// déjà être compilés (dans D:\KoreEngine\KoreEngine.Runtime\bin\Debug\net10.0\
    /// et D:\KoreEngine\KoreEngine.Editor\bin\Debug\net10.0\) avant de créer un
    /// nouveau projet.
    /// </summary>
    public static class ProjectTemplate
    {
        public static void Create(string engineDir, string targetDir, string projectName)
        {
            string runtimeDll = Path.Combine(engineDir, "KoreEngine.Runtime", "bin", "Debug", "net10.0", "KoreEngine.Runtime.dll");
            string editorDll = Path.Combine(engineDir, "KoreEngine.Editor", "bin", "Debug", "net10.0", "KoreEngine.Editor.dll");

            foreach (var (label, path) in new[] { ("Runtime", runtimeDll), ("Editor", editorDll) })
            {
                if (!File.Exists(path))
                    throw new Exception(
                        $"KoreEngine.{label}.dll introuvable : {path}\n" +
                        $"Compile d'abord KoreEngine.{label}.csproj avant de créer un nouveau projet.");
            }

            if (Directory.Exists(Path.Combine(targetDir, projectName)) &&
                Directory.GetFileSystemEntries(Path.Combine(targetDir, projectName)).Length > 0)
                throw new Exception($"Le dossier cible n'est pas vide : {Path.Combine(targetDir, projectName)}");

            var projectPath = Path.Combine(targetDir, projectName);

            Directory.CreateDirectory(projectPath);

            Console.WriteLine($"[ProjectCreator] Création du projet '{projectName}' dans {targetDir}");

            CreateAssetsStructure(projectPath);
            WriteProgramCs(projectPath, projectName);
            WriteCsproj(projectPath, projectName, runtimeDll, editorDll);
            WriteScriptsCsproj(projectPath, projectName, runtimeDll);
            WriteSln(projectPath, projectName);
            WriteImGui(engineDir, projectPath);
            WriteEditorIcons(engineDir, projectPath);
            CopyEngineRuntimeDependencies(engineDir, projectPath);

            Console.WriteLine("[ProjectCreator] Projet créé avec succès.");
        }

        // ---------------------------------------------------------------
        // 1. Structure Assets
        // ---------------------------------------------------------------

        static void CreateAssetsStructure(string targetDir)
        {
            string[] dirs = { "bin\\Debug\\net10.0\\" };

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(Path.Combine(targetDir, dir));
                Console.WriteLine($"[ProjectCreator] Crée : {dir} dans {targetDir}");
            }
        }

        // ---------------------------------------------------------------
        // 2. Program.cs minimal
        // ---------------------------------------------------------------

        static void WriteProgramCs(string targetDir, string projectName)
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
            Console.WriteLine($"[ProjectCreator] Fichier Program.cs écrit : {targetDir}\\Program.cs");
        }

        // ---------------------------------------------------------------
        // 3. .csproj du jeu — référence UNIQUEMENT la dll compilée du
        //    moteur, jamais son code source.
        // ---------------------------------------------------------------

        static void WriteCsproj(string targetDir, string projectName, string runtimeDll, string editorDll)
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
            Console.WriteLine($"[ProjectCreator] Fichier .csproj écrit : {targetDir}\\{projectName}.csproj");
        }

        // ---------------------------------------------------------------
        // 4. Scripts.csproj — même référence dll, pour l'édition des
        //    scripts avec IntelliSense, isolé du build réel.
        // ---------------------------------------------------------------

        static void WriteScriptsCsproj(string targetDir, string projectName, string runtimeDll)
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
            Console.WriteLine($"[ProjectCreator] Fichier Scripts.csproj écrit : {targetDir}\\{projectName}.Scripts.csproj");
        }

        // ---------------------------------------------------------------
        // 5. Solution unique : contient le jeu ET les scripts, aucun des
        //    deux ne référence le code source du moteur.
        // ---------------------------------------------------------------

        static void WriteSln(string targetDir, string projectName)
        {
            string gameGuid = Guid.NewGuid().ToString("B").ToUpper();
            string scriptsGuid = Guid.NewGuid().ToString("B").ToUpper();
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
	EndGlobalSection
EndGlobal
";
            File.WriteAllText(Path.Combine(targetDir, $"{projectName}.sln"), content);
            Console.WriteLine($"[ProjectCreator] Fichier .sln écrit : {targetDir}\\{projectName}.sln");
        }

        static void WriteImGui(string engineDir, string targetDir)
        {
            var source = Path.Combine(engineDir, "KoreEngine.Editor", "bin\\Debug\\net10.0\\imgui.ini");
            var destination = Path.Combine(targetDir, "bin\\Debug\\net10.0\\imgui.ini");

            if (File.Exists(destination))
                File.Delete(destination);

            if (File.Exists(source))
            {
                File.Copy(source, destination, overwrite: true);
                Console.WriteLine($"[ProjectCreator] Fichier imgui.ini écrit : {destination}");
            }
            else
            {
                Console.WriteLine($"[ProjectCreator] AVERTISSEMENT : imgui.ini introuvable dans {engineDir}");
            }
        }

        static void WriteEditorIcons(string engineDir, string targetDir)
        {
            string source = Path.Combine(engineDir, "KoreEngine.Editor", "Icons");
            string destination = Path.Combine(targetDir, "bin", "Debug", "net10.0", "Editor", "Icons");

            if (!Directory.Exists(source))
            {
                Console.WriteLine($"[ProjectCreator] AVERTISSEMENT : dossier d'icônes introuvable : {source}");
                return;
            }

            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                string destFile = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            Console.WriteLine($"[ProjectCreator] Icônes éditeur copiées : {destination}");
        }

        // ---------------------------------------------------------------
        // Copie toutes les dépendances runtime du moteur (ImGui.NET.dll,
        // binaires natifs SDL3, etc.) vers le dossier de sortie du projet.
        // Nécessaire car une référence binaire simple (<Reference><HintPath>)
        // ne copie que KoreEngine.dll elle-même, jamais ses propres dépendances —
        // contrairement à ProjectReference/PackageReference qui les résolvent
        // transitivement via NuGet.
        // ---------------------------------------------------------------

        static void CopyEngineRuntimeDependencies(string engineDir, string targetDir)
        {
            string destination = Path.Combine(targetDir, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(destination);

            // On copie tout SAUF les dll/pdb du moteur lui-même : ceux-là sont
            // déjà gérés séparément (référencés directement via HintPath) —
            // copier l'ancien binaire ici n'a aucun sens et créerait de la confusion.
            var skip = new[]
            {
                "KoreEngine.Runtime.dll", "KoreEngine.Runtime.pdb",
                "KoreEngine.Editor.dll", "KoreEngine.Editor.pdb"
            };

            foreach (var project in new[] { "KoreEngine.Runtime", "KoreEngine.Editor" })
            {
                string source = Path.Combine(engineDir, project, "bin", "Debug", "net10.0");

                if (!Directory.Exists(source))
                {
                    Console.WriteLine($"[ProjectCreator] AVERTISSEMENT : dossier de sortie introuvable : {source}");
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

            Console.WriteLine($"[ProjectCreator] Dépendances runtime du moteur copiées : {destination}");
        }
    }
}