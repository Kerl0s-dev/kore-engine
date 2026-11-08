using KoreEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

public class ScriptLoadContext : AssemblyLoadContext
{
    public ScriptLoadContext() : base(isCollectible: true) { }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Permet de résoudre les dépendances si nécessaire (ex: le moteur lui-même)
        return null;
    }
}

public static class ScriptCompiler
{
    private static ScriptLoadContext? currentAlc;
    private static WeakReference? previousAlcWeakRef;

    public static bool IsCompiling { get; private set; } = false;
    public static string Status { get; private set; } = "Ready";

    public static event Action? OnCompileSuccess;
    public static event Action? OnCompileError;

    // Indique au thread principal qu'une compilation vient de réussir
    public static volatile bool PendingSuccessNotification = false;
    public static volatile bool PendingErrorNotification = false;

    public static Assembly? LoadedAssembly { get; private set; }

    static readonly string OutputDll = Path.Combine(Directory.GetCurrentDirectory(), "Scripts.dll");
    static readonly CSharpParseOptions ParseOptions = new(preprocessorSymbols: new[] { "KORE_ENGINE_EDITOR" });

    /// <summary>
    /// Compile tous les fichiers .cs trouvés dans les dossiers surveillés.
    /// </summary>
    public static void CompileScripts(string[] watchDirs)
    {
        if (IsCompiling) return;
        IsCompiling = true;
        Status = "Compilation en cours...";

        // Exécution en arrière-plan pour ne pas figer l'interface de l'éditeur
        Task.Run(() =>
        {
            try
            {
                // 1. Récupérer tous les fichiers .cs
                var sourceFiles = new List<string>();
                foreach (var dir in watchDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        sourceFiles.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories));
                    }
                }

                if (sourceFiles.Count == 0)
                {
                    IsCompiling = false;
                    Status = "Aucun script trouvé.";
                    return;
                }

                // 2. Préparer la compilation Roslyn
                var syntaxTrees = sourceFiles.Select(f =>
                {
                    using var stream = File.OpenRead(f);
                    var sourceText = SourceText.From(stream, Encoding.Default);
                    return CSharpSyntaxTree.ParseText(sourceText, ParseOptions, path: f);
                }).ToList();

                var references = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => MetadataReference.CreateFromFile(a.Location))
                    .Cast<MetadataReference>()
                    .ToList();

                var options = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Disable);

                var compilation = CSharpCompilation.Create(
                    "Scripts",
                    syntaxTrees,
                    references,
                    options);

                using (var ms = new MemoryStream())
                using (var pdbStream = new MemoryStream())
                {
                    var result = compilation.Emit(ms, pdbStream: pdbStream);

                    if (!result.Success)
                    {
                        var failures = result.Diagnostics.Where(diagnostic =>
                            diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                        string errors = string.Join("\n", failures.Select(f => f.ToString()));
                        Status = $"Erreur de compilation :\n{errors}";

                        Logger.Error(errors);
                        Console.WriteLine($"[ScriptCompiler] {Status}");

                        IsCompiling = false;
                        PendingErrorNotification = true;
                        return;
                    }

                    // 3. Déchargement de l'ancien ALC
                    if (currentAlc != null)
                    {
                        currentAlc.Unload();
                        previousAlcWeakRef = new WeakReference(currentAlc);
                        currentAlc = null;
                    }

                    // 4. Charger le nouveau contexte et l'assembly en mémoire via MemoryStream
                    currentAlc = new ScriptLoadContext();
                    ms.Seek(0, SeekOrigin.Begin);
                    pdbStream.Seek(0, SeekOrigin.Begin);

                    using (var fs = File.Create(OutputDll))
                        ms.CopyTo(fs);

                    ms.Seek(0, SeekOrigin.Begin);
                    pdbStream.Seek(0, SeekOrigin.Begin);

                    LoadedAssembly = currentAlc.LoadFromStream(ms, pdbStream);

                    IsCompiling = false;
                    Status = "Compilation réussie !";

                    PendingSuccessNotification = true;
                }
            }
            catch (Exception ex)
            {
                IsCompiling = false;
                Status = $"Exception de compilation : {ex.Message}";

                Console.WriteLine($"[ScriptCompiler] {Status}");

                PendingErrorNotification = true;
            }
        });
    }

    /// <summary>
    /// Force le GC pour vérifier si l'ancien ALC a bien été libéré de la mémoire.
    /// </summary>
    public static void FinalizeUnload()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (previousAlcWeakRef != null)
        {
            if (previousAlcWeakRef.IsAlive)
            {
                Console.WriteLine("[ScriptCompiler] Fuite détectée : L'ancien AssemblyLoadContext est toujours en mémoire.");
            }
            else
            {
                Console.WriteLine("[ScriptCompiler] Nettoyage réussi : L'ancien AssemblyLoadContext a été libéré.");
            }
            previousAlcWeakRef = null;
        }
    }

    /// <summary>
    /// Décharge l'assembly Scripts.dll chargé par l'éditeur pour libérer la mémoire et éviter les conflits avec le moteur.
    /// Cette méthode est appelée à la fermeture de l'éditeur pour pouvoir lancer un projet même si des erreurs de compilation sont présentes dans les scripts.
    /// </summary>
    public static void RemoveLoadedAssembly()
    {
        // Décharge l'assembly Scripts.dll chargé par l'éditeur pour libérer la mémoire et éviter les conflits avec le moteur
        LoadedAssembly = null;
        if (currentAlc != null)
        {
            currentAlc.Unload();
            previousAlcWeakRef = new WeakReference(currentAlc);
            currentAlc = null;
        }

        // Supprime le .dll de scripts compilé pour éviter les conflits à la prochaine compilation
        File.Delete(OutputDll);
    }

    public static void RaiseCompileSuccess()
    {
        OnCompileSuccess?.Invoke();
    }

    public static void RaiseCompileError()
    {
        OnCompileError?.Invoke();
    }
}