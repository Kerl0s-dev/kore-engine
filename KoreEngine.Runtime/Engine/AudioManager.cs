using SDL3;

namespace KoreEngine.Engine;

public static class AudioManager
{
    static IntPtr mixer;
    static bool initialized = false;

    public static void Init()
    {
        if (initialized) return;
        Mixer.Init();
        mixer = Mixer.CreateMixerDevice(SDL.AudioDeviceDefaultPlayback, IntPtr.Zero);
        initialized = true;
    }

    // Charge un fichier audio
    public static IntPtr LoadAudio(string path, bool predecode = false)
    {
        return Mixer.LoadAudio(mixer, path, predecode);
    }

    // Crée un track réutilisable
    public static IntPtr CreateTrack()
    {
        return Mixer.CreateTrack(mixer);
    }

    // Joue un son fire-and-forget (effets sonores)
    public static void PlaySound(IntPtr audio)
    {
        Mixer.PlayAudio(mixer, audio);
    }

    // Joue un track avec options
    public static void PlayTrack(IntPtr track, IntPtr audio, bool loop = false)
    {
        Mixer.SetTrackAudio(track, audio);
        uint props = SDL.CreateProperties();
        if (loop) SDL.SetNumberProperty(props, Mixer.Props.PlayLoopsNumber, -1);
        Mixer.PlayTrack(track, props);
        SDL.DestroyProperties(props);
    }

    public static void PauseTrack(IntPtr track) => Mixer.PauseTrack(track);
    public static void ResumeTrack(IntPtr track) => Mixer.ResumeTrack(track);
    public static void StopTrack(IntPtr track) => Mixer.StopTrack(track, 10);

    public static void SetTrackVolume(IntPtr track, float gain) => Mixer.SetTrackGain(track, gain);
    public static void SetMasterVolume(float gain) => Mixer.SetMixerGain(mixer, gain);

    public static void DestroyAudio(IntPtr audio) => Mixer.DestroyAudio(audio);
    public static void DestroyTrack(IntPtr track) => Mixer.DestroyTrack(track);

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
        Mixer.DestroyMixer(mixer);
        Mixer.Quit();
        initialized = false;
    }
}