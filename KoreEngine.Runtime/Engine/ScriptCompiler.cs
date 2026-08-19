using Microsoft.CodeAnalysis;
using KoreEngine.Engine;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.Loader;

public static class ScriptCompiler
{
    public static event Action<string>? OnCompileError;
    public static event Action? OnCompileSuccess;

    public static bool IsCompiling { get; private set; }
    public static string Status { get; private set; } = "";

    static AssemblyLoadContext? _currentContext;

    // Contexte remplacé mais pas encore déchargé — en attente que la scène
    // ait fini de lâcher ses références aux anciennes instances de composants.
    static AssemblyLoadContext? _pendingUnloadContext;
    static WeakReference? _pendingUnloadRef;

    static readonly string OutputDll = Path.Combine(
        Directory.GetCurrentDirectory(), "Scripts.dll");

    public static async Task CompileAsync(IEnumerable<string> csFiles)
    {
        IsCompiling = true;
        Status = "Compilation en cours...";

        await Task.Run(() => Compile(csFiles.ToList()));
    }

    static void Compile(List<string> csFiles)
    {
        try
        {
            var syntaxTrees = csFiles
                .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
                .ToList();

            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable);

            var compilation = CSharpCompilation.Create(
                "KoreEngineScripts",
                syntaxTrees,
                references,
                options);

            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);

            if (result.Success)
            {
                ms.Seek(0, SeekOrigin.Begin);

                _pendingUnloadContext = _currentContext;

                _currentContext = new AssemblyLoadContext("ScriptsContext", isCollectible: true);
                _currentContext.LoadFromStream(ms);

                ms.Seek(0, SeekOrigin.Begin);
                using (var fs = File.Create(OutputDll))
                    ms.CopyTo(fs);

                Status = csFiles.Count > 0
                    ? $"Compilation réussie — {csFiles.Count} script(s)."
                    : "Compilation réussie — aucun script.";
                Console.WriteLine($"[ScriptCompiler] {Status}");
                OnCompileSuccess?.Invoke();
            }
            else
            {
                var diagnostics = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                Status = $"Erreur de compilation ({diagnostics.Count} erreur(s)).";
                Console.WriteLine($"[ScriptCompiler] {Status}");

                foreach (var diag in diagnostics)
                {
                    var span = diag.Location.GetLineSpan();
                    string? file = span.IsValid ? span.Path : null;
                    int? line = span.IsValid ? span.StartLinePosition.Line + 1 : null;

                    string message = file != null
                        ? $"{Path.GetFileName(file)}({line}): {diag.GetMessage()}"
                        : diag.GetMessage();

                    Console.WriteLine($"  {message}");
                    Logger.Error(message, file, line);
                }

                OnCompileError?.Invoke(string.Join("\n", diagnostics.Select(d => d.ToString())));
            }
        }
        catch (Exception e)
        {
            Status = $"Exception : {e.Message}";
            Console.WriteLine($"[ScriptCompiler] {Status}");
            OnCompileError?.Invoke(e.Message);
        }
        finally
        {
            IsCompiling = false;
        }
    }

    /// <summary>
    /// À appeler UNE FOIS la scène rechargée avec les nouveaux types (donc
    /// une fois qu'aucune instance de composant de l'ancienne assembly n'est
    /// plus référencée). Tente de décharger l'ancien AssemblyLoadContext.
    /// </summary>
    public static void FinalizeUnload()
    {
        if (_pendingUnloadContext == null) return;

        _pendingUnloadRef = new WeakReference(_pendingUnloadContext);
        _pendingUnloadContext.Unload();
        _pendingUnloadContext = null;

        for (int i = 0; i < 10 && _pendingUnloadRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (_pendingUnloadRef.IsAlive)
            Console.WriteLine("[ScriptCompiler] Attention : l'ancien contexte de scripts n'a pas pu être déchargé (références encore vivantes).");
    }
}