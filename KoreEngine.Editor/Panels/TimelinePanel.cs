using ImGuiNET;
using NVec2 = System.Numerics.Vector2;
using NVec4 = System.Numerics.Vector4;

namespace KoreEngine;

/// <summary>
/// Mini-vue "Animation Preview" : un clone de l'objet cible vit dans une scène privée, rendu dans une RenderTexture.
/// La scène en cours n'est jamais modifiée par le scrub, la lecture ou l'enregistrement.
/// </summary>
public sealed class AnimationPreview
{
    readonly Renderer renderer;
    readonly RenderTexture renderTexture;
    readonly Camera camera = new(512, 512, new Color(38, 38, 44));

    Scene scene = new() { Name = "Animation Preview" };
    Vector2 focus;
    float extent = 1f;       // taille de référence de l'objet, en mètres (cadrage)
    float zoomMul = 1f;      // zoom ajouté à la molette
    int pendingW = 512, pendingH = 512;

    /// <summary>Texte affiché en surimpression (temps, état d'enregistrement).</summary>
    public string Overlay = "";

    /// <summary>Faux pendant l'enregistrement ou quand la cible est verrouillée : la sélection ne change plus la cible.</summary>
    public bool AllowFollow = true;

    public GameObject? Source { get; private set; }
    public GameObject? Root { get; private set; }

    public AnimationPreview(Renderer renderer, int width, int height)
    {
        this.renderer = renderer;
        renderTexture = new RenderTexture(renderer, width, height);
    }

    /// <summary>Clone <paramref name="source"/> (avec ses enfants) dans la scène privée. null vide l'aperçu.</summary>
    public void SetSource(GameObject? source)
    {
        scene = new Scene { Name = "Animation Preview" };   // nouvelle scène : aucune trace de l'ancien clone
        Source = source;
        Root = null;
        zoomMul = 1f;
        if (source == null) return;

        try
        {
            Root = source.Instantiate(scene);
            focus = Root.transform.WorldPosition;

            // Un sprite fait 1 m x l'échelle du Transform : c'est elle qui donne le cadrage
            var s = Root.transform.WorldScale;
            extent = MathF.Max(MathF.Abs(s.X), MathF.Abs(s.Y));
            if (extent < 0.01f) extent = 1f;
        }
        catch (Exception e)
        {
            Logger.Warning($"Animation preview : not able to instantiate « {source.Name} » ({e.Message}).");
            Source = null;
            Root = null;
        }
    }

    /// <summary>
    /// Fait suivre la sélection : la cible devient le dernier objet de la scène sélectionné.
    /// Appelée par la Timeline ET par l'aperçu, pour que la cible soit à jour même si la Timeline est masquée.
    /// </summary>
    public void FollowSelection()
    {
        var sel = EditorSelection.Selected;
        if (AllowFollow && sel != null && !Owns(sel) && !ReferenceEquals(sel, Source))
            SetSource(sel);
    }

    /// <summary>Reclone la cible depuis la scène (reprend ses valeurs actuelles), en gardant le zoom.</summary>
    public void Refresh()
    {
        float zoom = zoomMul;
        SetSource(Source);
        zoomMul = zoom;
    }

    /// <summary>Vrai si <paramref name="go"/> est le clone ou l'un de ses descendants.</summary>
    public bool Owns(GameObject go)
    {
        for (var g = go; g != null; g = g.transform.Parent)
            if (ReferenceEquals(g, Root)) return true;
        return false;
    }

    /// <summary>Faux si le clone a été détruit (par exemple avec Suppr pendant l'enregistrement).</summary>
    public bool IsAlive => Root != null && scene.AllObjects.Contains(Root);

    /// <summary>Conservé pour compatibilité : le redimensionnement et le rendu se font maintenant dans <see cref="Draw"/>.</summary>
    public void ApplyPendingResize() { }

    /// <summary>Conservé pour compatibilité : le rendu se fait maintenant dans <see cref="Draw"/>.</summary>
    public void RenderScene() { }

    // Rend le clone dans la texture. Appelé par Draw, juste avant d'afficher l'image : la texture est donc
    // toujours à jour et à la taille du panneau, sans dépendre d'un appel dans la boucle de EditorWindow.
    void RenderToTexture()
    {
        // Même schéma que ViewportPanel : le setter Scene.Camera change Camera.Main, on le restaure ensuite.
        var current = SceneManager.Current;
        Camera? previous = null;
        try { previous = current?.Camera; } catch (InvalidOperationException) { }

        renderTexture.BeginRender();
        camera.ViewWidth = renderTexture.Width;
        camera.ViewHeight = renderTexture.Height;
        renderer.Clear(camera.Color);

        if (Root != null && IsAlive)
        {
            camera.Zoom = 0.4f * MathF.Min(renderTexture.Width, renderTexture.Height)
                          / (Renderer.PixelsPerUnit * extent) * zoomMul;
            camera.Position = focus;
            scene.Camera = camera;
            renderer.DrawRectOutline(focus.X - 0.5f, focus.Y + 0.5f, 1f, 1f, 80, 80, 95, 255, camera);   // repère de 1 m
            scene.Render(renderer);
        }
        renderTexture.EndRender();

        if (current != null && previous != null) current.Camera = previous;
    }

    public void Draw()
    {
        if (!ImGui.Begin("Animation Preview")) { ImGui.End(); return; }

        FollowSelection();

        var avail = ImGui.GetContentRegionAvail();
        pendingW = Math.Max(16, (int)avail.X);
        pendingH = Math.Max(16, (int)avail.Y);

        renderTexture.Resize(pendingW, pendingH);
        RenderToTexture();

        var imagePos = ImGui.GetCursorScreenPos();
        ImGui.Image(renderTexture.Texture, new NVec2(pendingW, pendingH));

        bool hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();

        var mouseScreen = io.MousePos;
        var localMouse = new Vector2(mouseScreen.X - imagePos.X, mouseScreen.Y - imagePos.Y);

        // Gestion unifiée de la caméra (Pan + Zoom lissé)
        if (hovered) HandleCameraInput(io, localMouse);
        StepEditorCamera(io.DeltaTime);

        if (ImGui.IsItemHovered())
        {
            float wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f) zoomMul = Math.Clamp(zoomMul * MathF.Pow(1.1f, wheel), 0.1f, 20f);
        }

        var dl = ImGui.GetWindowDrawList();
        uint white = ImGui.GetColorU32(new NVec4(1f, 1f, 1f, 0.8f));
        if (Root == null)
            dl.AddText(imagePos + new NVec2(8, 8), white, "No target selected : Select an object in the hierarchy.");
        if (Overlay.Length > 0)
            dl.AddText(imagePos + new NVec2(8, pendingH - 20), white, Overlay);

        ImGui.End();
    }

    // ---------------------------------------------------------------
    // Caméra éditeur
    // ---------------------------------------------------------------
    private float cameraTargetZoom;
    private Vector2 cameraTargetPosition;
    private Vector2 zoomAnchor;   // point sous lequel on zoome (coordonnées viewport)

    // Vitesse de zoom (facteur multiplicatif par cran de molette).
    const float ZoomStep = 0.1f;
    const float ZoomMin = 0.001f;
    const float ZoomMax = 20f;

    const float ZoomSmoothing = 0.25f;
    const float PanSmoothing = 0.25f;
    static float Damp(float smoothing, float dt) => 1f - MathF.Exp(-smoothing * dt);

    /// <summary>Recentre la caméra éditeur sur un point du monde (touche F, « Frame Selected »...).</summary>
    public void FocusOn(Vector2 worldPos) => cameraTargetPosition = worldPos;

    // Entrées : seulement quand la souris survole le viewport
    const float ScrollPanPixels = 80f;   // distance à l'écran par cran de molette

    void HandleCameraInput(ImGuiIOPtr io, Vector2 mouseViewport)
    {
        if (io.MouseDown[2])
        {
            var delta = new Vector2(-io.MouseDelta.X, io.MouseDelta.Y) / camera.Zoom / Renderer.PixelsPerUnit;
            camera.Position += delta;
            cameraTargetPosition += delta;
        }

        if (io.MouseWheel == 0f && io.MouseWheelH == 0f) return;

        if (io.KeyCtrl)
        {
            float factor = MathF.Pow(1f + ZoomStep, io.MouseWheel);
            cameraTargetZoom = System.Math.Clamp(cameraTargetZoom * factor, ZoomMin, ZoomMax);
            zoomAnchor = mouseViewport;
        }
        else
        {
            // Pas constant à l'écran, converti en mètres
            float step = ScrollPanPixels / (camera.Zoom * Renderer.PixelsPerUnit);

            float dx = io.MouseWheelH;      // molette horizontale ou pavé tactile
            float dy = io.MouseWheel;
            if (io.KeyShift) { dx -= dy; dy = 0f; }   // Shift + molette : défilement horizontal

            // On déplace la cible : StepEditorCamera lisse le mouvement
            cameraTargetPosition += new Vector2(dx * step, dy * step);
        }
    }

    // Animation : à CHAQUE frame en mode édition, même hors du viewport
    void StepEditorCamera(float dt)
    {
        float diff = cameraTargetZoom - camera.Zoom;
        if (MathF.Abs(diff) > cameraTargetZoom * 0.0005f)
        {
            Vector2 worldBefore = camera.ScreenToWorld(zoomAnchor);
            camera.Zoom += diff * Damp(ZoomSmoothing * 1000, dt);
            Vector2 worldAfter = camera.ScreenToWorld(zoomAnchor);

            Vector2 correction = worldBefore - worldAfter;
            camera.Position += correction;
            cameraTargetPosition += correction;
        }
        else camera.Zoom = cameraTargetZoom;

        camera.Position = Vector2.Lerp(camera.Position, cameraTargetPosition, Damp(PanSmoothing * 1000, dt));
    }

    public void Destroy() => renderTexture.Destroy();
}

/// <summary>
/// Timeline (dopesheet) du clip en cours d'édition (voir <see cref="ClipEditSession"/>) :
/// scrub, lecture, déplacement de keyframes, mode Record. L'aperçu se fait dans <see cref="AnimationPreview"/>.
/// </summary>
public sealed class TimelinePanel
{
    const float LabelW = 230f;
    const float RowH = 22f;
    const float RulerH = 24f;
    const float KeyRadius = 5f;

    static readonly (string Name, float Step)[] Snaps = { ("Off", 0f), ("1/60 s", 1f / 60f), ("0.05 s", 0.05f), ("0.1 s", 0.1f), ("0.25 s", 0.25f) };
    static readonly string[] SnapNames = Snaps.Select(s => s.Name).ToArray();
    static readonly string[] InterpNames = Enum.GetNames<Interpolation>();

    readonly AnimationPreview preview;
    readonly KeySelection selection = new();
    readonly RecordTracker tracker = new();

    // Lecture
    float time;
    bool playing;
    bool recording;
    bool lockTarget;

    // Liaison clip -> clone
    AnimationClip? boundClip;
    GameObject? boundRoot;
    string boundSignature = "";
    PropertyBinding?[] bindings = Array.Empty<PropertyBinding?>();
    readonly List<string> bindWarnings = new();

    // Vue
    float viewStart;
    float pxPerSec = 220f;
    int snapIndex = 1;

    // Interaction
    enum DragMode { None, Scrub, Keys, Box, Pan }
    DragMode drag = DragMode.None;
    KeyRef grabbed;
    float dragMouseStartX;
    NVec2 boxStart;
    bool boxAdditive;
    int ctxRow = -1;
    KeyRef ctxKey;
    bool ctxOnKey;
    float ctxTime;

    public TimelinePanel(AnimationPreview preview) => this.preview = preview;

    // ------------------------------------------------------------------
    // Enregistrement
    // ------------------------------------------------------------------

    void StartRecording()
    {
        if (preview.Root == null || ClipEditSession.Clip == null) return;
        playing = false;
        recording = true;
        EditorSelection.Selected = preview.Root;   // l'Inspector édite maintenant le CLONE, jamais la scène
        tracker.Capture(preview.Root);
    }

    void StopRecording()
    {
        if (!recording) return;
        recording = false;
        if (preview.Root != null && EditorSelection.Selected != null && preview.Owns(EditorSelection.Selected))
            EditorSelection.Selected = preview.Source;
    }

    // ------------------------------------------------------------------
    // Mise à jour (avant le dessin)
    // ------------------------------------------------------------------

    void Rebind(AnimationClip clip)
    {
        bindWarnings.Clear();
        bindings = preview.Root != null
            ? AnimationBinder.Bind(preview.Root, clip, w => bindWarnings.Add(w))
            : Array.Empty<PropertyBinding?>();
        boundClip = clip;
        boundRoot = preview.Root;
        boundSignature = AnimationBinder.Signature(clip);
    }

    void UpdateState(AnimationClip? clip)
    {
        // Suivi de la sélection : la cible est le dernier objet de la scène sélectionné (sauf verrouillage / enregistrement)
        var sel = EditorSelection.Selected;
        preview.AllowFollow = !recording && !lockTarget;
        preview.FollowSelection();

        // La cible ou le clone a disparu
        if (preview.Source != null && preview.Root != null && !preview.IsAlive)
        {
            StopRecording();
            preview.SetSource(null);
        }

        // Quitter l'enregistrement si l'utilisateur sélectionne autre chose
        if (recording && (preview.Root == null || sel == null || !preview.Owns(sel)))
            recording = false;

        if (clip == null) return;
        selection.Prune(clip);

        if (!ReferenceEquals(clip, boundClip) || !ReferenceEquals(preview.Root, boundRoot)
            || AnimationBinder.Signature(clip) != boundSignature)
            Rebind(clip);

        float duration = clip.EffectiveDuration;

        // Mode Record : les modifications faites dans l'Inspector depuis la frame précédente deviennent des keyframes
        if (recording && preview.Root != null)
        {
            if (tracker.Commit(preview.Root, clip, time) > 0)
                Rebind(clip);   // une piste a pu être créée
        }

        // Lecture
        if (playing)
        {
            time += ImGui.GetIO().DeltaTime;
            if (time >= duration)
            {
                if (clip.Loop) time %= duration;
                else { time = duration; playing = false; }
            }
        }
        time = Math.Clamp(time, 0f, duration);

        // Le clip est appliqué au clone, puis on mémorise son état comme nouvelle référence d'enregistrement
        if (preview.Root != null)
        {
            AnimationBinder.Apply(bindings, clip, time);
            if (recording) tracker.Capture(preview.Root);
        }

        preview.Overlay = $"{time:0.000} s / {duration:0.###} s" + (recording ? "   REC" : "");
    }

    // ------------------------------------------------------------------
    // Fenêtre
    // ------------------------------------------------------------------

    public void Draw()
    {
        if (!ImGui.Begin("Timeline")) { ImGui.End(); return; }

        var clip = ClipEditSession.Clip;
        UpdateState(clip);

        // Tant que la Timeline a le focus, les raccourcis globaux de l'éditeur (Suppr, W/E/R, F, Ctrl+S) sont suspendus :
        // Suppr supprime des keyframes, pas l'objet sélectionné.
        bool focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (focused) ImGui.SetNextFrameWantCaptureKeyboard(true);

        if (clip == null)
        {
            ImGui.TextDisabled("Select a \'.kanim\' file in the project panel (or create one : Create > Animation Clip).");
            ImGui.End();
            return;
        }

        DrawToolbar(clip);
        DrawWarnings();
        DrawDopesheet(clip, focused);

        ImGui.End();
    }

    void DrawToolbar(AnimationClip clip)
    {
        float duration = clip.EffectiveDuration;

        if (ImGui.Button(playing ? "Pause" : "Play"))
        {
            playing = !playing;
            if (playing) StopRecording();
            if (playing && time >= duration) time = 0f;
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop")) { playing = false; time = 0f; }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        float t = time;
        if (ImGui.DragFloat("##time", ref t, 0.005f, 0f, duration, "%.3f s")) { time = t; playing = false; }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        float dur = clip.Duration;
        if (ImGui.DragFloat("Duration", ref dur, 0.01f, 0.01f, 600f, "%.2f")) clip.Duration = MathF.Max(0.01f, dur);

        ImGui.SameLine();
        bool loop = clip.Loop;
        if (ImGui.Checkbox("Loop", ref loop)) clip.Loop = loop;

        ImGui.SameLine();
        bool canRecord = preview.Root != null;
        ImGui.BeginDisabled(!canRecord && !recording);
        // 'recording' change pendant le clic : on fige sa valeur pour que Push et Pop restent appariés
        bool wasRecording = recording;
        if (wasRecording) ImGui.PushStyleColor(ImGuiCol.Button, new NVec4(0.75f, 0.15f, 0.15f, 1f));
        if (ImGui.Button(wasRecording ? "Recording" : "Record"))
        {
            if (wasRecording) StopRecording(); else StartRecording();
        }
        if (wasRecording) ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Record : The values modified in the inspector are save as keyframes in realtime.");

        // Ligne 2
        ImGui.TextDisabled("Target :");
        ImGui.SameLine();
        ImGui.Text(preview.Source?.Name ?? "(none)");
        ImGui.SameLine();
        ImGui.Checkbox("Lock", ref lockTarget);
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh")) { StopRecording(); preview.Refresh(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Refresh the preview");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.Combo("Snap", ref snapIndex, SnapNames, SnapNames.Length);

        ImGui.SameLine();
        if (ImGui.SmallButton("+ Track")) ImGui.OpenPopup("##addtrack");
        DrawAddTrackPopup(clip);

        ImGui.SameLine();
        if (ImGui.SmallButton("Fit")) { pxPerSec = MathF.Max(20f, (ImGui.GetContentRegionAvail().X - LabelW - 30f) / clip.EffectiveDuration); viewStart = 0f; }

        ImGui.SameLine();
        bool dirty = ClipEditSession.IsDirty;
        ImGui.BeginDisabled(!dirty);
        if (ImGui.Button(dirty ? "Save*" : "Save")) ClipEditSession.Save();
        ImGui.EndDisabled();
    }

    void DrawAddTrackPopup(AnimationClip clip)
    {
        if (!ImGui.BeginPopup("##addtrack")) return;
        for (int i = 1; i < ClipInspector.TrackPresets.Length; i++)
        {
            string path = ClipInspector.TrackPresets[i];
            bool exists = clip.Tracks.Any(tr => tr.Path == path);
            ImGui.BeginDisabled(exists);
            if (ImGui.MenuItem(path)) AddTrack(clip, path);
            ImGui.EndDisabled();
        }
        ImGui.EndPopup();
    }

    void AddTrack(AnimationClip clip, string path)
    {
        var track = new AnimationTrack { Path = path };
        float value = 0f;
        if (preview.Root != null) RecordTracker.TryRead(preview.Root, path, out value);
        bool isStep = path.EndsWith(".Frame") || path.EndsWith(".FlipX") || path.EndsWith(".FlipY");
        track.SetKey(0f, value, isStep ? Interpolation.Step : Interpolation.Linear);
        clip.Tracks.Add(track);
    }

    void DrawWarnings()
    {
        if (ClipEditSession.Warnings.Count == 0 && bindWarnings.Count == 0) return;
        var yellow = new NVec4(1f, 0.8f, 0.3f, 1f);
        foreach (var w in ClipEditSession.Warnings.Take(2)) ImGui.TextColored(yellow, w);
        foreach (var w in bindWarnings.Take(3)) ImGui.TextColored(yellow, w);
        int more = ClipEditSession.Warnings.Count + bindWarnings.Count - 5;
        if (more > 0) ImGui.TextDisabled($"... and {more} other warning(s)");
    }

    // ------------------------------------------------------------------
    // Dopesheet
    // ------------------------------------------------------------------

    static uint Col(int r, int g, int b, int a = 255) =>
        ImGui.GetColorU32(new NVec4(r / 255f, g / 255f, b / 255f, a / 255f));

    float XOf(float t, float x0) => x0 + (t - viewStart) * pxPerSec;
    float TOf(float x, float x0) => (x - x0) / pxPerSec + viewStart;
    float SnapStep => Snaps[snapIndex].Step;

    static float TickStep(float pxPerSecond)
    {
        float[] steps = { 0.01f, 0.025f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
        foreach (float s in steps)
            if (s * pxPerSecond >= 70f) return s;
        return 60f;
    }

    void DrawDopesheet(AnimationClip clip, bool windowFocused)
    {
        var io = ImGui.GetIO();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        if (avail.X < LabelW + 60f || avail.Y < RulerH + RowH) return;

        float x0 = origin.X + LabelW;
        float x1 = origin.X + avail.X;
        float duration = clip.EffectiveDuration;
        var dl = ImGui.GetWindowDrawList();

        // ---------------- Règle ----------------
        dl.AddRectFilled(origin, new NVec2(x1, origin.Y + RulerH), Col(45, 45, 50));
        float tickStep = TickStep(pxPerSec);
        float firstTick = MathF.Floor(viewStart / tickStep) * tickStep;
        for (float tt = firstTick; XOf(tt, x0) <= x1; tt += tickStep)
        {
            float x = XOf(tt, x0);
            if (x < x0) continue;
            dl.AddLine(new NVec2(x, origin.Y + RulerH - 8f), new NVec2(x, origin.Y + RulerH), Col(150, 150, 155));
            dl.AddText(new NVec2(x + 3f, origin.Y + 3f), Col(190, 190, 195), tt.ToString("0.###"));
        }

        ImGui.SetCursorScreenPos(new NVec2(x0, origin.Y));
        ImGui.InvisibleButton("##ruler", new NVec2(x1 - x0, RulerH));
        bool rulerHovered = ImGui.IsItemHovered();
        if (ImGui.IsItemActivated()) drag = DragMode.Scrub;

        // ---------------- Lignes ----------------
        ImGui.SetCursorScreenPos(new NVec2(origin.X, origin.Y + RulerH));
        ImGui.BeginChild("##rows", new NVec2(avail.X, avail.Y - RulerH), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollWithMouse);
        var cdl = ImGui.GetWindowDrawList();
        var content = ImGui.GetCursorScreenPos();
        int rows = clip.Tracks.Count;
        float contentH = MathF.Max(rows * RowH, ImGui.GetContentRegionAvail().Y);
        float rowsRight = content.X + ImGui.GetContentRegionAvail().X;

        // Fonds
        for (int i = 0; i < rows; i++)
        {
            float y = content.Y + i * RowH;
            cdl.AddRectFilled(new NVec2(content.X, y), new NVec2(rowsRight, y + RowH), i % 2 == 0 ? Col(34, 34, 38) : Col(40, 40, 45));
        }
        // Zone hors durée
        float xEnd = XOf(duration, x0);
        if (xEnd < rowsRight)
            cdl.AddRectFilled(new NVec2(MathF.Max(xEnd, x0), content.Y), new NVec2(rowsRight, content.Y + contentH), Col(0, 0, 0, 70));

        // Labels de pistes (clic droit : supprimer)
        ImGui.InvisibleButton("##labels", new NVec2(LabelW, contentH));
        bool labelsHovered = ImGui.IsItemHovered();
        var mouse = ImGui.GetMousePos();
        int labelRow = (int)MathF.Floor((mouse.Y - content.Y) / RowH);
        if (labelsHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && labelRow >= 0 && labelRow < rows)
        {
            ctxRow = labelRow;
            ImGui.OpenPopup("##trackctx");
        }
        for (int i = 0; i < rows; i++)
        {
            float y = content.Y + i * RowH;
            var track = clip.Tracks[i];
            bool bad = i < bindings.Length && bindings[i] == null && preview.Root != null;
            cdl.AddText(new NVec2(content.X + 6f, y + 4f), bad ? Col(255, 190, 80) : Col(210, 210, 215),
                string.IsNullOrEmpty(track.Path) ? "(without a path)" : track.Path);
        }

        // Zone des keyframes
        ImGui.SetCursorScreenPos(new NVec2(x0, content.Y));
        ImGui.InvisibleButton("##canvas", new NVec2(rowsRight - x0, contentH),
            ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        bool canvasHovered = ImGui.IsItemHovered();

        // Quelle clé est sous la souris ?
        KeyRef? hoverKey = null;
        if (canvasHovered && drag == DragMode.None)
        {
            float best = 8f;
            for (int i = 0; i < rows; i++)
            {
                float cy = content.Y + i * RowH + RowH * 0.5f;
                var keys = clip.Tracks[i].Keys;
                for (int k = 0; k < keys.Count; k++)
                {
                    float dx = MathF.Abs(XOf(keys[k].Time, x0) - mouse.X);
                    float dy = MathF.Abs(cy - mouse.Y);
                    if (dy < RowH * 0.5f && dx < best) { best = dx; hoverKey = new KeyRef(clip.Tracks[i], k); }
                }
            }
        }

        int mouseRow = (int)MathF.Floor((mouse.Y - content.Y) / RowH);

        // Clic gauche / droit sur la zone
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (hoverKey is KeyRef hk)
            {
                if (io.KeyCtrl) selection.Toggle(hk.Track, hk.Index);
                else if (!selection.Contains(hk.Track, hk.Index)) selection.Select(hk.Track, hk.Index, false);
                if (selection.Contains(hk.Track, hk.Index))
                {
                    grabbed = hk;
                    selection.BeginDrag(hk);
                    dragMouseStartX = mouse.X;
                    drag = DragMode.Keys;
                    playing = false;
                }
            }
            else
            {
                boxAdditive = io.KeyCtrl;
                if (!boxAdditive) selection.Clear();
                boxStart = mouse;
                drag = DragMode.Box;
            }
        }
        if (canvasHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && hoverKey == null
            && mouseRow >= 0 && mouseRow < rows)
        {
            var tr = clip.Tracks[mouseRow];
            float tNew = Math.Max(0f, KeySelection.Snap(TOf(mouse.X, x0), SnapStep));
            tr.SetKey(tNew, tr.Evaluate(tNew));
            drag = DragMode.None;
            selection.Clear();
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            ctxOnKey = hoverKey != null;
            ctxRow = mouseRow;
            ctxTime = Math.Max(0f, KeySelection.Snap(TOf(mouse.X, x0), SnapStep));
            if (hoverKey is KeyRef rk)
            {
                ctxKey = rk;
                if (!selection.Contains(rk.Track, rk.Index)) selection.Select(rk.Track, rk.Index, false);
            }
            ImGui.OpenPopup("##keyctx");
        }

        // Tooltip
        if (hoverKey is KeyRef tk && drag == DragMode.None)
        {
            var key = tk.Track.Keys[tk.Index];
            ImGui.SetTooltip($"{tk.Track.Path}\ntime = {key.Time:0.###} s | value = {key.Value:0.###}\nInterpolaiton = {key.Interpolation}");
        }

        // Dessin des keyframes
        for (int i = 0; i < rows; i++)
        {
            float cy = content.Y + i * RowH + RowH * 0.5f;
            var track = clip.Tracks[i];
            // Ligne reliant des clés consécutives de même valeur : repère visuel des « maintiens »
            for (int k = 0; k + 1 < track.Keys.Count; k++)
            {
                if (MathF.Abs(track.Keys[k].Value - track.Keys[k + 1].Value) > 1e-6f) continue;
                cdl.AddLine(new NVec2(XOf(track.Keys[k].Time, x0), cy), new NVec2(XOf(track.Keys[k + 1].Time, x0), cy), Col(90, 90, 100), 2f);
            }
            for (int k = 0; k < track.Keys.Count; k++)
            {
                float kx = XOf(track.Keys[k].Time, x0);
                if (kx < x0 - KeyRadius || kx > rowsRight + KeyRadius) continue;
                bool sel = selection.Contains(track, k);
                bool hot = hoverKey is KeyRef h && ReferenceEquals(h.Track, track) && h.Index == k;
                uint fill = sel ? Col(255, 170, 40)
                    : track.Keys[k].Interpolation switch
                    {
                        Interpolation.Step => Col(110, 170, 255),
                        Interpolation.Smooth => Col(120, 220, 140),
                        _ => Col(225, 225, 230)
                    };
                float r = hot ? KeyRadius + 1.5f : KeyRadius;
                cdl.AddQuadFilled(new NVec2(kx, cy - r), new NVec2(kx + r, cy), new NVec2(kx, cy + r), new NVec2(kx - r, cy), fill);
                cdl.AddQuad(new NVec2(kx, cy - r), new NVec2(kx + r, cy), new NVec2(kx, cy + r), new NVec2(kx - r, cy), Col(20, 20, 24), 1f);
            }
        }

        // Rectangle de sélection
        if (drag == DragMode.Box)
        {
            var a = new NVec2(MathF.Min(boxStart.X, mouse.X), MathF.Min(boxStart.Y, mouse.Y));
            var b = new NVec2(MathF.Max(boxStart.X, mouse.X), MathF.Max(boxStart.Y, mouse.Y));
            cdl.AddRectFilled(a, b, Col(255, 170, 40, 40));
            cdl.AddRect(a, b, Col(255, 170, 40, 160));
        }

        // Tête de lecture (lignes)
        float px = XOf(time, x0);
        if (px >= x0 && px <= rowsRight)
            cdl.AddLine(new NVec2(px, content.Y), new NVec2(px, content.Y + contentH), recording ? Col(230, 60, 60) : Col(255, 90, 90), 2f);

        DrawContextPopups(clip);

        ImGui.EndChild();

        // Tête de lecture (règle)
        if (px >= x0 && px <= x1)
        {
            dl.AddLine(new NVec2(px, origin.Y), new NVec2(px, origin.Y + RulerH), recording ? Col(230, 60, 60) : Col(255, 90, 90), 2f);
            dl.AddTriangleFilled(new NVec2(px - 5f, origin.Y), new NVec2(px + 5f, origin.Y), new NVec2(px, origin.Y + 8f), recording ? Col(230, 60, 60) : Col(255, 90, 90));
        }

        // ---------------- Interactions continues ----------------
        if (drag == DragMode.Scrub)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                time = Math.Clamp(KeySelection.Snap(TOf(mouse.X, x0), SnapStep), 0f, duration);
                playing = false;
            }
            else drag = DragMode.None;
        }
        else if (drag == DragMode.Keys)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
                selection.DragBy((mouse.X - dragMouseStartX) / pxPerSec, SnapStep);
            else { selection.EndDrag(); drag = DragMode.None; }
        }
        else if (drag == DragMode.Box)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                int r0 = (int)MathF.Floor((MathF.Min(boxStart.Y, mouse.Y) - content.Y) / RowH);
                int r1 = (int)MathF.Floor((MathF.Max(boxStart.Y, mouse.Y) - content.Y) / RowH);
                selection.SelectRect(clip, r0, r1, TOf(MathF.Min(boxStart.X, mouse.X), x0), TOf(MathF.Max(boxStart.X, mouse.X), x0), boxAdditive);
                drag = DragMode.None;
            }
        }

        // Zoom (molette) et déplacement (clic milieu / Maj + molette)
        if ((canvasHovered || rulerHovered) && drag != DragMode.Keys)
        {
            float wheel = io.MouseWheel;
            if (wheel != 0f)
            {
                if (io.KeyShift) viewStart = MathF.Max(0f, viewStart - wheel * 60f / pxPerSec);
                else
                {
                    float tMouse = TOf(mouse.X, x0);
                    pxPerSec = Math.Clamp(pxPerSec * MathF.Pow(1.15f, wheel), 20f, 5000f);
                    viewStart = MathF.Max(0f, tMouse - (mouse.X - x0) / pxPerSec);
                }
            }
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
                viewStart = MathF.Max(0f, viewStart - io.MouseDelta.X / pxPerSec);
        }

        // Raccourcis (fenêtre focalisée)
        if (windowFocused && !io.WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Delete) && selection.Count > 0) selection.DeleteSelected();
            if (ImGui.IsKeyPressed(ImGuiKey.Space)) { playing = !playing; if (playing) StopRecording(); }
            if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) { time = MathF.Max(0f, time - (SnapStep > 0 ? SnapStep : 1f / 60f)); playing = false; }
            if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) { time = MathF.Min(duration, time + (SnapStep > 0 ? SnapStep : 1f / 60f)); playing = false; }
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S)) ClipEditSession.Save();
        }
    }

    void DrawContextPopups(AnimationClip clip)
    {
        if (ImGui.BeginPopup("##trackctx"))
        {
            if (ctxRow >= 0 && ctxRow < clip.Tracks.Count)
            {
                ImGui.TextDisabled(clip.Tracks[ctxRow].Path);
                if (ImGui.MenuItem("Delete Track"))
                {
                    selection.Clear();
                    clip.Tracks.RemoveAt(ctxRow);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("##keyctx"))
        {
            if (ctxOnKey)
            {
                if (ImGui.MenuItem("Delete Key(s)", "Del")) { selection.DeleteSelected(); ImGui.CloseCurrentPopup(); }
                ImGui.Separator();
                for (int i = 0; i < InterpNames.Length; i++)
                    if (ImGui.MenuItem(InterpNames[i])) { selection.SetInterpolation((Interpolation)i); ImGui.CloseCurrentPopup(); }
            }
            else if (ctxRow >= 0 && ctxRow < clip.Tracks.Count)
            {
                if (ImGui.MenuItem($"Add Key at {ctxTime:0.###} s"))
                {
                    var tr = clip.Tracks[ctxRow];
                    tr.SetKey(ctxTime, tr.Evaluate(ctxTime));
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }
    }
}