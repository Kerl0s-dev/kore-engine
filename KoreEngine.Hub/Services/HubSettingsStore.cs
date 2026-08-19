using System.IO;
using System.Text.Json;

namespace KoreEngine.Hub.Services;

public class HubSettings
{
    public string? EngineDir { get; set; }
}

public static class HubSettingsStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KoreEngine", "hub_settings.json");

    public static HubSettings Load()
    {
        if (!File.Exists(FilePath)) return new HubSettings();

        try
        {
            return JsonSerializer.Deserialize<HubSettings>(File.ReadAllText(FilePath)) ?? new HubSettings();
        }
        catch
        {
            return new HubSettings();
        }
    }

    public static void Save(HubSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
