using System.Globalization;
using System.Text;

namespace KoreEngine;

/// <summary>
/// Réglages globaux du projet (scène de démarrage, état de la caméra éditeur, etc.)
/// </summary>
public static class ProjectSettings
{
    public static string? StartupScene { get; set; }

    static string FilePath => Path.Combine(SceneManager.AssetsDirectory, "ProjectSettings.txt");

    public static void Load()
    {
        StartupScene = null;

        if (!File.Exists(FilePath)) return;

        var inv = CultureInfo.InvariantCulture;

        foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

            int colon = trimmed.IndexOf(':');
            if (colon < 0) continue;

            string key = trimmed[..colon].Trim();
            string val = trimmed[(colon + 1)..].Trim();

            if (key == "StartupScene")
            {
                StartupScene = val;
            }
        }
    }

    public static void Save()
    {
        var inv = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(SceneManager.AssetsDirectory);

        var sb = new StringBuilder();
        sb.AppendLine("# KoreEngine Project Settings");
        sb.AppendLine($"StartupScene: {StartupScene ?? ""}");

        File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
    }
}