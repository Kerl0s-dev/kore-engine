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
        return null;
    }
}

public static class ScriptCompiler
{
    private static ScriptLoadContext? currentAlc;
    private static WeakReference? previousAlcWeakRef;

    private static volatile bool isCompiling = false;
    public static bool IsCompiling
    {
        get => isCompiling;
        private set => isCompiling = value;
    }

    public static event Action? OnCompileSuccess;
    public static event Action? OnCompileError;

    public static volatile bool PendingSuccessNotification = false;
    public static volatile bool PendingErrorNotification = false;

    public static Assembly? LoadedAssembly { get; private set; }

    static readonly CSharpParseOptions ParseOptions = new(preprocessorSymbols: new[] { "KORE_ENGINE_EDITOR" });

    static string GetOutputDllPath(string[] watchDirs)
    {
        string? projectRoot = EditorWindow.Current?.ProjectRoot;

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            string? firstExistingDir = watchDirs.FirstOrDefault(Directory.Exists);
            if (!string.IsNullOrWhiteSpace(firstExistingDir))
            {
                var parent = Directory.GetParent(firstExistingDir);
                projectRoot = parent?.FullName ?? firstExistingDir;
            }
            else
            {
                projectRoot = Directory.GetCurrentDirectory();
            }
        }

        string outDir = Path.Combine(projectRoot, "bin", "Release", "net10.0");
        Directory.CreateDirectory(outDir);
        return Path.Combine(outDir, "Scripts.dll");
    }

    public static void CompileScripts(string[] watchDirs)
    {
        if (IsCompiling) return;
        IsCompiling = true;

        string outputDll = GetOutputDllPath(watchDirs);

        Task.Run(() =>
        {
            try
            {
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
                    return;
                }

                var syntaxTrees = sourceFiles.Select(f =>
                {
                    using var stream = File.OpenRead(f);
                    var sourceText = SourceText.From(stream, Encoding.Default);
                    return CSharpSyntaxTree.ParseText(sourceText, ParseOptions, path: f);
                }).ToList();

                var assemblyLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!asm.IsDynamic && !string.IsNullOrWhiteSpace(asm.Location) && File.Exists(asm.Location))
                        assemblyLocations.Add(asm.Location);
                }

                // Références explicites utiles pour les scripts utilisateur
                string coreLocation = typeof(Component).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(coreLocation) && File.Exists(coreLocation))
                    assemblyLocations.Add(coreLocation);

                string sdlLocation = typeof(SDL3.SDL).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(sdlLocation) && File.Exists(sdlLocation))
                    assemblyLocations.Add(sdlLocation);

                var references = assemblyLocations
                    .Select(loc => (MetadataReference)MetadataReference.CreateFromFile(loc))
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

                using var ms = new MemoryStream();
                using var pdbStream = new MemoryStream();

                var result = compilation.Emit(ms, pdbStream: pdbStream);

                Logger.Clear();

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                    string errors = string.Join("\n", failures.Select(f => f.ToString()));

                    Logger.Error(errors);

                    IsCompiling = false;
                    PendingErrorNotification = true;
                    return;
                }

                if (currentAlc != null)
                {
                    currentAlc.Unload();
                    previousAlcWeakRef = new WeakReference(currentAlc);
                    currentAlc = null;
                }

                currentAlc = new ScriptLoadContext();

                ms.Seek(0, SeekOrigin.Begin);
                using (var fs = File.Create(outputDll))
                    ms.CopyTo(fs);

                ms.Seek(0, SeekOrigin.Begin);
                pdbStream.Seek(0, SeekOrigin.Begin);

                LoadedAssembly = currentAlc.LoadFromStream(ms, pdbStream);

                IsCompiling = false;
                PendingSuccessNotification = true;
            }
            catch (Exception ex)
            {
                IsCompiling = false;
                Console.WriteLine($"[ScriptCompiler] Exception de compilation : {ex.Message}");
                PendingErrorNotification = true;
            }
        });
    }

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

    public static void RemoveLoadedAssembly()
    {
        LoadedAssembly = null;
        if (currentAlc != null)
        {
            currentAlc.Unload();
            previousAlcWeakRef = new WeakReference(currentAlc);
            currentAlc = null;
        }

        string? projectRoot = EditorWindow.Current?.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            string candidate = Path.Combine(projectRoot, "bin", "Release", "net10.0", "Scripts.dll");
            TryDeleteFile(candidate);
        }

        TryDeleteFile(Path.Combine(Directory.GetCurrentDirectory(), "Scripts.dll"));
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignorer si verrouillé ou déjà supprimé
        }
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