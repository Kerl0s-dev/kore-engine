using System.Diagnostics;
using System.IO;

namespace KoreEngine.Hub.Services;

public static class ProjectInfoService
{
    /// <summary>Taille totale du dossier projet, en octets (Assets, bin/, obj/, tout compris).</summary>
    public static long GetDirectorySize(string path)
    {
        long total = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* fichier verrouillé ou supprimé entre-temps : ignoré */ }
            }
        }
        catch { /* dossier inaccessible */ }

        return total;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go" };
        double size = bytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    /// <summary>
    /// Version du moteur réellement utilisé par CE projet : lue depuis le
    /// KoreEngine.Runtime.dll copié dans son propre bin/Debug/net10.0 après
    /// build (copie automatique via <Reference Private="True">), pas depuis
    /// le moteur source — un vieux projet non reconstruit affichera donc la
    /// version qu'il utilise réellement, pas la version courante du moteur.
    /// </summary>
    public static string GetEngineVersion(string projectPath)
    {
        string dllPath = Path.Combine(projectPath, "bin", "Debug", "net10.0", "KoreEngine.Runtime.dll");

        if (!File.Exists(dllPath))
            return "—";

        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return info.FileVersion ?? info.ProductVersion ?? "—";
        }
        catch
        {
            return "—";
        }
    }
}
