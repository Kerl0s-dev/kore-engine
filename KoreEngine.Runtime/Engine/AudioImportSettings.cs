using System.Globalization;

namespace KoreEngine;

public enum AudioLoadMode { InMemory, Streaming }   // l'ordre = l'index du combo

public class AudioImportSettings
{
    public static readonly string[] SupportedExtensions = { ".wav", ".mp3", ".ogg", ".flac" };

    public const float DefaultVolume = 1f;

    // Streaming = le comportement historique (AudioManager.LoadAudio avait predecode = false) :
    // les clips sans .kmeta ne changent pas de comportement.
    public const AudioLoadMode DefaultLoadMode = AudioLoadMode.Streaming;

    /// <summary>Volume appliqué par défaut à ce clip (0 à 1) ; le volume d'une source s'y multiplie.</summary>
    public float Volume = DefaultVolume;
    public AudioLoadMode LoadMode = DefaultLoadMode;

    public static AudioImportSettings Load(string assetPath)
    {
        var s = new AudioImportSettings();
        var values = AssetMeta.Read(TextureCache.Resolve(assetPath));

        if (values.TryGetValue("Volume", out var v) &&
            float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol))
            s.Volume = Math.Clamp(vol, 0f, 1f);

        if (values.TryGetValue("LoadMode", out var m) && Enum.TryParse<AudioLoadMode>(m, true, out var mode))
            s.LoadMode = mode;

        return s;
    }

    public void Save(string assetPath)
    {
        string resolved = TextureCache.Resolve(assetPath);

        // Pas de .kmeta créé tant que tout est au défaut.
        bool isDefault = Volume == DefaultVolume && LoadMode == DefaultLoadMode;
        if (isDefault && !File.Exists(AssetMeta.PathFor(resolved))) return;

        AssetMeta.Write(resolved, new[]
        {
            new KeyValuePair<string, string>("Volume", Volume.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("LoadMode", LoadMode.ToString()),
        });
    }

    public IEnumerable<InspectorField> GetInspectorFields(string assetPath)
    {
        yield return new FloatField
        {
            Label = "Default Volume",
            Get = () => Volume,
            Set = v => { Volume = Math.Clamp(v, 0f, 1f); Save(assetPath); },
            Slider = true,
            Min = 0f,
            Max = 1f
        };

        yield return new EnumField
        {
            Label = "Load Mode",
            Names = Enum.GetNames<AudioLoadMode>(),
            Get = () => (int)LoadMode,
            Set = v => { LoadMode = (AudioLoadMode)v; Save(assetPath); }
        };
    }
}