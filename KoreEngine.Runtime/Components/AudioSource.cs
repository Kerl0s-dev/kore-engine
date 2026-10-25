namespace KoreEngine;

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
    [NonSerialized] DateTime loadedMetaStamp;

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
            // Applique aussi le changement à un son déjà en cours de lecture.
            Set = v => { Volume = v; if (track != IntPtr.Zero) AudioManager.SetTrackVolume(track, v); },
            Value = 0.01f,
            Min = 0f,
            Max = 1f
        };

        yield return new FloatField
        {
            Label = "Pitch",
            Get = () => Pitch,
            Set = v => Pitch = v,
            Value = 0.01f,
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

        yield return new ButtonField
        {
            Label = "Play",
            Action = Play,
            Tooltip = "Prévisualiser le clip (fonctionne aussi en mode édition)."
        };

        yield return new ButtonField
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
        if (Spatial2D && track != IntPtr.Zero && gameObject != null)
        {
            var worldPos = gameObject.transform.WorldPosition;

            // Récupère la position de la caméra (adapte selon l'API de ton moteur)
            var cameraPos = Camera.Main != null ? Camera.Main.transform.WorldPosition : default; // ou Vector2.Zero

            // Calcule la position relative par rapport à l'écran/caméra
            float relX = worldPos.X - cameraPos.X;
            float relY = worldPos.Y - cameraPos.Y;

            AudioManager.SetTrackPosition(track, relX, relY);
        }
    }

    public void Play()
    {
        // Même résolution que les textures : marche dans l'éditeur ET dans le jeu buildé
        // (le chemin sérialisé peut être absolu, ou relatif au projet comme "Assets/clip.wav").
        string resolved = string.IsNullOrEmpty(ClipPath) ? "" : TextureCache.Resolve(ClipPath);

        if (resolved.Length == 0 || !File.Exists(resolved))
        {
            Logger.Warning($"[AudioSourceComponent] Fichier audio introuvable : {ClipPath}");
            return;
        }

        // Recharge si le chemin OU les réglages d'import (.kmeta) ont changé depuis le dernier chargement.
        // GetLastWriteTimeUtc renvoie toujours la même date si le .kmeta n'existe pas : pas de rechargement inutile.
        var stamp = File.GetLastWriteTimeUtc(AssetMeta.PathFor(resolved));

        if (audio == IntPtr.Zero || loadedPath != ClipPath || stamp != loadedMetaStamp)
        {
            if (audio != IntPtr.Zero) AudioManager.DestroyAudio(audio);
            audio = AudioManager.LoadAudioAsset(ClipPath);   // applique mode de chargement + volume par défaut
            loadedPath = ClipPath;
            loadedMetaStamp = stamp;
        }

        if (audio == IntPtr.Zero)
        {
            Logger.Warning($"[AudioSourceComponent] Échec du chargement : {ClipPath}");
            return;
        }

        if (track == IntPtr.Zero)
            track = AudioManager.CreateTrack();

        AudioManager.SetTrackVolume(track, Volume);
        AudioManager.SetTrackPitch(track, Pitch);
        AudioManager.PlayTrack(track, audio, Loop);

        if (Spatial2D && gameObject != null)
        {
            var worldPos = gameObject.transform.WorldPosition;
            var cameraPos = Camera.Main != null ? Camera.Main.transform.WorldPosition : default;

            // Application directe de la position relative au démarrage
            AudioManager.SetTrackPosition(track, worldPos.X - cameraPos.X, worldPos.Y - cameraPos.Y);
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