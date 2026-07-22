using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Components;

public class AudioSource : Component
{
    public string ClipPath = "";
    public float Volume = 1f;
    public float Pitch = 1f;
    public bool Loop = false;
    public bool Spatial2D = false;
    public bool PlayOnStart = false;

    [NonSerialized] IntPtr audio = IntPtr.Zero;
    [NonSerialized] IntPtr track = IntPtr.Zero;
    [NonSerialized] string loadedPath = "";

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new AudioClipField
        {
            Label = "Clip",
            Get = () => ClipPath,
            Set = v => ClipPath = v
        };

        yield return new FloatField
        {
            Label = "Volume",
            Get = () => Volume,
            Set = v => Volume = v,
            Speed = 0.01f,
            Min = 0f,
            Max = 1f
        };

        yield return new FloatField
        {
            Label = "Pitch",
            Get = () => Pitch,
            Set = v => Pitch = v,
            Speed = 0.01f,
            Min = 0.1f,
            Max = 3f
        };

        yield return new BoolField
        {
            Label = "Loop",
            Get = () => Loop,
            Set = v => Loop = v
        };

        yield return new BoolField
        {
            Label = "Spatial 2D",
            Get = () => Spatial2D,
            Set = v => Spatial2D = v
        };

        yield return new BoolField
        {
            Label = "Play On Start",
            Get = () => PlayOnStart,
            Set = v => PlayOnStart = v
        };

        yield return new TextField
        {
            Label = "Status",
            Get = () => track != IntPtr.Zero ? "Playing" : "Stopped"
        };

        yield return new ActionField
        {
            Label = "Play",
            Action = Play,
            Tooltip = "Prévisualiser le clip (fonctionne aussi en mode édition)."
        };

        yield return new ActionField
        {
            Label = "Stop",
            Action = Stop
        };
    }

    public override void Start()
    {
        if (PlayOnStart) Play();
    }

    public override void Update(float dt)
    {
        // Synchronise la position du track à chaque frame tant que la
        // lecture spatiale est active. Le listener SDL_mixer est fixe à
        // (0,0,0) — c'est donc à nous de fournir une position déjà relative
        // à un listener/caméra, pas la position monde brute. Pour l'instant
        // on utilise WorldPosition directement ; à revoir une fois qu'un
        // vrai concept de listener/caméra actif existe dans le moteur.
        if (Spatial2D && track != IntPtr.Zero && Owner != null)
        {
            var p = Owner.WorldPosition;
            AudioManager.SetTrackPosition(track, p.X, p.Y);
        }
    }

    public void Play()
    {
        if (string.IsNullOrEmpty(ClipPath) || !File.Exists(ClipPath))
        {
            Logger.Warning($"[AudioSourceComponent] Fichier audio introuvable : {ClipPath}");
            return;
        }

        if (audio == IntPtr.Zero || loadedPath != ClipPath)
        {
            if (audio != IntPtr.Zero) AudioManager.DestroyAudio(audio);
            audio = AudioManager.LoadAudio(ClipPath);
            loadedPath = ClipPath;
        }

        if (track == IntPtr.Zero)
            track = AudioManager.CreateTrack();

        AudioManager.PlayTrack(track, audio, Loop);
        AudioManager.SetTrackVolume(track, Volume);
        AudioManager.SetTrackPitch(track, Pitch);

        if (Spatial2D && Owner != null)
        {
            var p = Owner.WorldPosition;
            AudioManager.SetTrackPosition(track, p.X, p.Y);
        }
        else
        {
            AudioManager.ClearTrackPosition(track);
        }
    }

    public void Stop()
    {
        if (track != IntPtr.Zero) AudioManager.StopTrack(track);
    }

    public void Pause()
    {
        if (track != IntPtr.Zero) AudioManager.PauseTrack(track);
    }

    public void Resume()
    {
        if (track != IntPtr.Zero) AudioManager.ResumeTrack(track);
    }

    public override void OnDestroy()
    {
        if (track != IntPtr.Zero)
        {
            AudioManager.DestroyTrack(track);
            track = IntPtr.Zero;
        }
        if (audio != IntPtr.Zero)
        {
            AudioManager.DestroyAudio(audio);
            audio = IntPtr.Zero;
        }
    }
}