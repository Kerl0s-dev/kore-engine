using SDL3;

namespace KoreEngine;

public static class AudioManager
{
    static IntPtr mixer;
    static bool initialized = false;

    // Volume par défaut de chaque clip chargé via LoadAudioAsset (réglage d'import .kmeta)
    static readonly Dictionary<IntPtr, float> clipVolume = new();
    // Volume demandé par la source (SetTrackVolume) et clip actuellement lié à chaque track
    static readonly Dictionary<IntPtr, float> trackUserGain = new();
    static readonly Dictionary<IntPtr, IntPtr> trackAudio = new();
    // Tracks réutilisés pour les sons ponctuels dont le volume n'est pas standard
    static readonly List<IntPtr> oneShotTracks = new();

    public static void Init()
    {
        if (initialized) return;
        Mixer.Init();
        mixer = Mixer.CreateMixerDevice(SDL.AudioDeviceDefaultPlayback, IntPtr.Zero);
        initialized = true;
    }

    // Charge un fichier audio (chemin déjà résolu, sans réglages d'import)
    public static IntPtr LoadAudio(string path, bool predecode = false)
    {
        return Mixer.LoadAudio(mixer, path, predecode);
    }

    /// <summary>
    /// Charge un clip du projet en appliquant ses réglages d'import (.kmeta) :
    /// mode de chargement (mémoire ou flux) et volume par défaut.
    /// </summary>
    public static IntPtr LoadAudioAsset(string path)
    {
        var settings = AudioImportSettings.Load(path);
        IntPtr audio = LoadAudio(TextureCache.Resolve(path), predecode: settings.LoadMode == AudioLoadMode.InMemory);
        if (audio != IntPtr.Zero) clipVolume[audio] = settings.Volume;
        return audio;
    }

    // Crée un track réutilisable
    public static IntPtr CreateTrack()
    {
        return Mixer.CreateTrack(mixer);
    }

    static void ApplyTrackGain(IntPtr track)
    {
        float user = trackUserGain.TryGetValue(track, out var g) ? g : 1f;
        float clip = trackAudio.TryGetValue(track, out var a) && clipVolume.TryGetValue(a, out var v) ? v : 1f;
        Mixer.SetTrackGain(track, user * clip);
    }

    // Joue un son fire-and-forget (effets sonores)
    public static void PlaySound(IntPtr audio)
    {
        // Volume standard : lecture directe, comme avant.
        if (!clipVolume.TryGetValue(audio, out var volume) || volume >= 0.999f)
        {
            Mixer.PlayAudio(mixer, audio);
            return;
        }

        // Volume réglé : il faut un track pour porter le gain. On réutilise un track libre.
        IntPtr track = IntPtr.Zero;
        foreach (var t in oneShotTracks)
            if (!Mixer.TrackPlaying(t)) { track = t; break; }

        if (track == IntPtr.Zero)
        {
            track = Mixer.CreateTrack(mixer);
            oneShotTracks.Add(track);
        }

        Mixer.SetTrackAudio(track, audio);
        Mixer.SetTrackGain(track, volume);
        Mixer.PlayTrack(track, 0);
    }

    // Joue un track avec options
    public static void PlayTrack(IntPtr track, IntPtr audio, bool loop = false)
    {
        Mixer.SetTrackAudio(track, audio);
        trackAudio[track] = audio;
        ApplyTrackGain(track);            // volume de la source x volume du clip

        uint props = SDL.CreateProperties();
        if (loop) SDL.SetNumberProperty(props, Mixer.Props.PlayLoopsNumber, -1);
        Mixer.PlayTrack(track, props);
        SDL.DestroyProperties(props);
    }

    public static void PauseTrack(IntPtr track) => Mixer.PauseTrack(track);
    public static void ResumeTrack(IntPtr track) => Mixer.ResumeTrack(track);
    public static void StopTrack(IntPtr track) => Mixer.StopTrack(track, 10);
    public static bool StopAllTracks(long fadeOutSeconds = 0) => Mixer.StopAllTracks(mixer, fadeOutSeconds);

    public static void SetTrackVolume(IntPtr track, float gain)
    {
        trackUserGain[track] = gain;
        ApplyTrackGain(track);
    }

    public static void SetMasterVolume(float gain) => Mixer.SetMixerGain(mixer, gain);

    public static void DestroyAudio(IntPtr audio)
    {
        clipVolume.Remove(audio);
        Mixer.DestroyAudio(audio);
    }

    public static void DestroyTrack(IntPtr track)
    {
        trackUserGain.Remove(track);
        trackAudio.Remove(track);
        Mixer.DestroyTrack(track);
    }

    public static void SetTrackPitch(IntPtr track, float ratio) => Mixer.SetTrackFrequencyRatio(track, ratio);

    // position en unités du monde du jeu, relative au listener (voir note plus bas)
    public static unsafe void SetTrackPosition(IntPtr track, float x, float y, float z = 0f)
    {
        var pos = new Mixer.Point3D { X = x, Y = y, Z = z };
        Mixer.Point3D* ptr = &pos;
        Mixer.SetTrack3DPosition(track, (nint)ptr);
    }

    // désactive la spatialisation (repasse le track en mixage standard stéréo)
    public static void ClearTrackPosition(IntPtr track) => Mixer.SetTrack3DPosition(track, IntPtr.Zero);

    public static void Quit()
    {
        if (!initialized) return;

        clipVolume.Clear();
        trackUserGain.Clear();
        trackAudio.Clear();
        oneShotTracks.Clear();

        Mixer.DestroyMixer(mixer);
        Mixer.Quit();
        initialized = false;
    }
}