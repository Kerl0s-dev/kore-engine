using System.IO;

namespace KoreEngine.Hub.Services;
public static class EngineLocator
{
    public static string? AutoDetect()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KoreEngine.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }

    public static bool IsValidEngineDir(string dir)
    {
        return File.Exists(Path.Combine(dir, "KoreEngine.Runtime", "KoreEngine.Runtime.csproj")) &&
               File.Exists(Path.Combine(dir, "KoreEngine.Editor", "KoreEngine.Editor.csproj"));
    }
}
