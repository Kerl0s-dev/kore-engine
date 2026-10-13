using ImGuiNET;
using KoreEngine.Editor;
using SDL3;
using System.Numerics;

namespace KoreEngine;

public class EditorWindow
{
    /// <summary>Instance courante — utilisée par les panels (ex: ProjectPanel pour
    /// le dialogue de fichier natif) qui ont besoin du WindowHandle SDL.</summary>
    public static EditorWindow? Current { get; private set; }

    IntPtr window;
    public IntPtr WindowHandle => window;

    public Renderer Renderer;

    // Running = boucle principale active (jusqu'à fermeture de la fenêtre).
    // Playing  = façon Unity : simulation (SceneManager.Update) en pause ou non.
    //            Ne change RIEN à l'affichage de l'éditeur — la scène reste
    //            visible et figée dans le viewport quand Playing == false.
    public bool Running { get; set; } = true;
    bool showQuitPopup = false;
    bool showShortcutsWindow = false;

    bool _playing = false;
    public bool Playing
    {
        get => _playing;
        set
        {
            if (value && !_playing)
                SceneManager.NotifyStart();
            SceneManager.SaveLocked = value;
            _playing = value;
        }
    }

    public bool Paused { get; private set; } = false;
    bool stepRequested = false;

    public static string Title { get; set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }

    ImGuiBackend? imguiBackend;

    public ViewportPanel? viewportPanel;
    HierarchyPanel? hierarchyPanel;
    InspectorPanel? inspectorPanel;
    ProjectPanel? projectPanel;
    ConsolePanel? consolePanel;

    ScriptWatcher? scriptWatcher;

    float fps;
    ulong lastTicks;

    public EditorWindow(string title, int width, int height)
    {
        Title = title;
        Width = width;
        Height = height;

        SDL.Init(SDL.InitFlags.Video);
        window = SDL.CreateWindow(title, width, height, SDL.WindowFlags.Resizable);

        SDL.StartTextInput(window); // active les événements TextInput SDL3 (Cela permet de modifier des champs textuels)

        Renderer = new Renderer(window);
        TextureCache.Init(Renderer); // Initialise le cache de texture
        EditorIcons.Init(Renderer); //Initialise les icônes de l'éditeur
        AudioManager.Init(); // Initialise le moteur audio (SDL_mixer)

        imguiBackend = new ImGuiBackend(Renderer);
        imguiBackend.Init(width, height);

        #region Style Editeur
        ImGuiStylePtr style = ImGui.GetStyle();
        var c = style.Colors;

        static Vector4 Gray(float v, float a = 1f) => new(v, v, v, a);
        var accent = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
        var accentSoft = new Vector4(0.26f, 0.59f, 0.98f, 0.35f);

        // --- Fond (niveaux bien distincts) ---
        c[(int)ImGuiCol.WindowBg] = Gray(0.13f);
        c[(int)ImGuiCol.ChildBg] = Gray(0.10f);
        c[(int)ImGuiCol.PopupBg] = Gray(0.16f);

        // --- Bordures (visibles) ---
        c[(int)ImGuiCol.Border] = Gray(0.32f);
        c[(int)ImGuiCol.BorderShadow] = Gray(0f, 0f);

        // --- Texte ---
        c[(int)ImGuiCol.Text] = Gray(0.92f);
        c[(int)ImGuiCol.TextDisabled] = Gray(0.58f);

        // --- Frames : le survol reste sombre, texte toujours lisible ---
        c[(int)ImGuiCol.FrameBg] = Gray(0.19f);
        c[(int)ImGuiCol.FrameBgHovered] = Gray(0.26f);
        c[(int)ImGuiCol.FrameBgActive] = Gray(0.31f);

        // --- Title bar / menu ---
        c[(int)ImGuiCol.TitleBg] = Gray(0.09f);
        c[(int)ImGuiCol.TitleBgActive] = Gray(0.16f);
        c[(int)ImGuiCol.TitleBgCollapsed] = Gray(0.09f);
        c[(int)ImGuiCol.MenuBarBg] = Gray(0.10f);

        // --- Scrollbar ---
        c[(int)ImGuiCol.ScrollbarBg] = Gray(0.10f);
        c[(int)ImGuiCol.ScrollbarGrab] = Gray(0.30f);
        c[(int)ImGuiCol.ScrollbarGrabHovered] = Gray(0.40f);
        c[(int)ImGuiCol.ScrollbarGrabActive] = Gray(0.50f);

        // --- Accent unique (bleu) ---
        c[(int)ImGuiCol.CheckMark] = accent;
        c[(int)ImGuiCol.SliderGrab] = new Vector4(0.26f, 0.59f, 0.98f, 0.80f);
        c[(int)ImGuiCol.SliderGrabActive] = accent;

        // --- Boutons ---
        c[(int)ImGuiCol.Button] = Gray(0.22f);
        c[(int)ImGuiCol.ButtonHovered] = Gray(0.31f);
        c[(int)ImGuiCol.ButtonActive] = new Vector4(0.20f, 0.45f, 0.80f, 1.00f);

        // --- Headers (CollapsingHeader, TreeNode, Selectable) ---
        c[(int)ImGuiCol.Header] = Gray(0.22f);
        c[(int)ImGuiCol.HeaderHovered] = Gray(0.30f);
        c[(int)ImGuiCol.HeaderActive] = new Vector4(0.20f, 0.45f, 0.80f, 0.70f);

        // --- Séparateurs ---
        c[(int)ImGuiCol.Separator] = Gray(0.30f);
        c[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.60f);
        c[(int)ImGuiCol.SeparatorActive] = accent;

        // --- Resize grip ---
        c[(int)ImGuiCol.ResizeGrip] = new Vector4(0.26f, 0.59f, 0.98f, 0.20f);
        c[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.26f, 0.59f, 0.98f, 0.60f);
        c[(int)ImGuiCol.ResizeGripActive] = accent;

        // --- Tabs : l'onglet actif ressort grâce à l'overline ---
        c[(int)ImGuiCol.Tab] = Gray(0.14f);
        c[(int)ImGuiCol.TabHovered] = Gray(0.30f);
        c[(int)ImGuiCol.TabSelected] = Gray(0.22f);
        c[(int)ImGuiCol.TabSelectedOverline] = accent;
        c[(int)ImGuiCol.TabDimmed] = Gray(0.11f);
        c[(int)ImGuiCol.TabDimmedSelected] = Gray(0.17f);

        // --- Docking ---
        c[(int)ImGuiCol.DockingPreview] = new Vector4(0.26f, 0.59f, 0.98f, 0.40f);
        c[(int)ImGuiCol.DockingEmptyBg] = Gray(0.09f);

        // --- Divers ---
        c[(int)ImGuiCol.TextSelectedBg] = accentSoft;
        c[(int)ImGuiCol.DragDropTarget] = accent;
        c[(int)ImGuiCol.NavCursor] = accent;
        c[(int)ImGuiCol.ModalWindowDimBg] = Gray(0f, 0.50f);

        // --- Arrondis ---
        style.WindowRounding = 0f;
        style.FrameRounding = 2f;
        style.PopupRounding = 2f;
        style.ScrollbarRounding = 2f;
        style.GrabRounding = 2f;
        style.TabRounding = 0f;

        // --- Bordures ---
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 1f;   // délimite les inputs
        style.PopupBorderSize = 1f;

        // --- Espacements (plus aérés) ---
        style.WindowPadding = new System.Numerics.Vector2(10f, 10f);
        style.FramePadding = new System.Numerics.Vector2(6f, 4f);
        style.ItemSpacing = new System.Numerics.Vector2(8f, 6f);
        style.ItemInnerSpacing = new System.Numerics.Vector2(6f, 4f);
        style.IndentSpacing = 16f;
        style.ScrollbarSize = 14f;
        style.GrabMinSize = 10f;
        style.DisabledAlpha = 0.55f;
        #endregion

        // L'éditeur est toujours actif : la RenderTexture du viewport est créée
        // ici avec la taille de la fenêtre comme point de départ. Elle sera
        // recalée sur la taille réelle du panneau ImGui dès la 2e frame (lag
        // d'une frame géré par ViewportPanel.ApplyPendingResize, voir Run()).
        var viewportTexture = new RenderTexture(Renderer, width, height);
        viewportPanel = new ViewportPanel(Renderer, viewportTexture);

        hierarchyPanel = new HierarchyPanel();
        inspectorPanel = new InspectorPanel(Renderer);
        projectPanel = new ProjectPanel();
        consolePanel = new ConsolePanel();

        SceneManager.OnSceneChanging += () => EditorSelection.Selected = null;

        // SceneManager ne connaît pas ProjectPanel (Runtime pur) — c'est
        // l'Editor qui lui indique où se trouve le vrai dossier Assets du
        // projet, ici, avant tout scan/chargement de scène. Un jeu buildé
        // n'exécute jamais cette ligne et garde le défaut (Assets à côté
        // de l'exe).
        SceneManager.AssetsDirectory = Path.Combine(ProjectPanel.FindProjectRoot(), "Assets");

        lastTicks = SDL.GetTicks();
    }

    public void Run()
    {
        Current = this;

        scriptWatcher = new ScriptWatcher(ProjectPanel.FindProjectRoot());

        while (Running)
        {
            // 1. Input
            Input.NewFrame();
            while (SDL.PollEvent(out var e))
            {
                // Affiche un popup de confirmation de l'action avant de quitter
                switch (e.Type)
                {
                    case (uint)SDL.EventType.Quit:
                    case (uint)SDL.EventType.WindowCloseRequested:
                        showQuitPopup = true; // On demande confirmation
                        break;
                }

                imguiBackend?.HandleEvent(e);

                // Ne transmet les événements clavier au jeu que si ImGui
                // n'est pas en train de capturer le clavier (champ texte actif, etc.)
                if (!ImGui.GetIO().WantCaptureKeyboard)
                    Input.HandleEvent(e);
            }

            // 2. Resize de la fenêtre réelle
            UpdateWindowSizeIfNeeded();

            // 3. dt
            float dt = ComputeDeltaTime();
            scriptWatcher?.Update(dt);

            // 4. ImGui NewFrame
            imguiBackend?.NewFrame(dt, Width, Height);

            var io = ImGui.GetIO();

            if (!io.WantCaptureKeyboard)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                {
                    if (EditorSelection.Selected != null)
                    {
                        EditorSelection.Selected.Destroy();
                        EditorSelection.Selected = null;
                    }
                }
                if (io.KeyCtrl)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.S))
                    {
                        string? name = SceneManager.CurrentSceneName;
                        if (name != null)
                            SceneManager.SaveCurrentScene();
                    }

                    if (ImGui.IsKeyPressed(ImGuiKey.R)) viewportPanel?.EditorCamera.Reset(); // Reset the camera
                }
                else
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.W)) EditorSelection.ActiveGizmoMode = GizmoMode.Move; // Change the selection gizmo to 'Move'
                    if (ImGui.IsKeyPressed(ImGuiKey.E)) EditorSelection.ActiveGizmoMode = GizmoMode.Rotate; // Change the selection gizmo to 'Rotate'
                    if (ImGui.IsKeyPressed(ImGuiKey.R)) EditorSelection.ActiveGizmoMode = GizmoMode.Scale; // Change the selection gizmo to 'Scale'

                    if (ImGui.IsKeyPressed(ImGuiKey.F)) viewportPanel?.EditorCamera.Position = (Vector2)(EditorSelection.Selected != null ? EditorSelection.Selected.transform.Position : viewportPanel?.EditorCamera.Position)!;
                }
            }

            // Popup de confirmation de fermeture de l'éditeur
            if (showQuitPopup)
            {
                var viewport = ImGui.GetMainViewport().GetCenter();
                ImGui.SetNextWindowPos(viewport, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
                ImGui.Begin("quit_popup", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoNavFocus);
                ImGui.Text("Are you sure you want to quit Kore Engine?");
                ImGui.Separator();

                if (ImGui.Button("No", new System.Numerics.Vector2(150, 20)))
                {
                    showQuitPopup = false; // Annuler
                }

                ImGui.SameLine();

                if (ImGui.Button("Yes", new System.Numerics.Vector2(150, 20)))
                {
                    Running = false; // Quitter la boucle
                }

                ImGui.End();
            }

            // 5. Applique la scène en attente — toujours, même en mode édition.
            SceneManager.ApplyPendingScene();
            scriptWatcher?.FinalizeUnloadIfPending();

            // Logique de jeu — uniquement si Playing, et pas en pause (sauf step demandé).
            if (Playing)
            {
                if (!Paused)
                    SceneManager.Update(dt);
                else if (stepRequested)
                {
                    SceneManager.Update(dt);
                    stepRequested = false;
                }
            }

            // 6. Rendu.
            //    a) Caméra active : EditorCamera en mode édition, scene.Camera en mode jeu.
            Camera? activeCamera = Playing
                ? SceneManager.Current?.Camera
                : viewportPanel?.EditorCamera;

            //    b) Applique le resize sur la caméra active.
            viewportPanel?.ApplyPendingResize(activeCamera);

            //    c) Rend la scène dans la RenderTexture avec la caméra active.
            viewportPanel?.RenderScene(SceneManager.Current, activeCamera, !Playing);

            //    d) Écran réel : clear + panneaux ImGui.
            Renderer.Clear(SceneManager.Current?.Camera?.Color);

            DrawDockspace();
            hierarchyPanel?.Draw();
            inspectorPanel?.Draw();
            projectPanel?.Draw();
            viewportPanel?.Draw(!Playing); // editorMode = true quand Playing == false
            consolePanel?.Draw();

            // 8. ImGui render
            imguiBackend?.Render();

            // 9. Present
            Renderer.Present();
        }
    }

    /// <summary>Resynchronise Width/Height sur la taille réelle de la fenêtre SDL.</summary>
    void UpdateWindowSizeIfNeeded()
    {
        SDL.GetWindowSize(window, out int w, out int h);
        if (w != Width || h != Height)
        {
            Width = w;
            Height = h;
        }
    }

    /// <summary>Delta time en secondes, clampé entre 0.0001f et 1/30f (contrainte ImGui).</summary>
    float ComputeDeltaTime()
    {
        ulong now = SDL.GetTicks();               // currentTime
        float rawDt = (now - lastTicks) / 1000f;   // deltaTime = currentTime - lastTime (en secondes)
        lastTicks = now;                           // Mise à jour de lastTime pour la prochaine frame

        // Optionnel : clamping et calcul FPS
        fps = rawDt > 0f ? 1f / rawDt : 0f;
        return MathF.Min(MathF.Max(rawDt, 0.0001f), 1f / 30f);
    }

    /// <summary>Dockspace ImGui plein écran qui accueille tous les panneaux éditeur.</summary>
    void DrawDockspace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);

        ImGui.Begin("DockSpace",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.MenuBar);

        ImGui.BeginMenuBar();
        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("Keyboard Shortcuts"))
                showShortcutsWindow = true;
            ImGui.EndMenu();
        }
        ImGui.EndMenuBar();

        DrawToolbar();

        uint dockspaceId = ImGui.GetID("MainDockSpace");
        ImGui.DockSpace(dockspaceId, System.Numerics.Vector2.Zero, ImGuiDockNodeFlags.None);

        ImGui.End();

        DrawShortcutsWindow();
    }

    /// <summary>
    /// Barre d'outils horizontale sous la zone de dockspace - façon Unity :
    /// Play/Pause/Stop/Step centrés, Reset Camera à gauche, statut à droite.
    /// </summary>
    void DrawToolbar()
    {
        ImGui.BeginChild("##toolbar", new System.Numerics.Vector2(0, 48), ImGuiChildFlags.Borders);

        string spaceLabel = EditorSelection.ActiveGizmoSpace == GizmoSpace.World ? "World" : "Local";
        if (ImGui.Button(spaceLabel))
            EditorSelection.ActiveGizmoSpace = EditorSelection.ActiveGizmoSpace == GizmoSpace.World
                ? GizmoSpace.Local : GizmoSpace.World;

        if (scriptWatcher?.IsBuilding == true)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), scriptWatcher.BuildStatus);
        }

        // --- Centre : Play/Pause/Stop/Step ---
        const float btnSize = 16f;
        const float spacing = 4f;

        float centerWidth = btnSize * 3 + spacing * 2; // Stop, Pause, Step

        float centerX = (ImGui.GetWindowWidth() - centerWidth) * 0.5f;
        ImGui.SameLine(centerX);

        if (IconButton(Playing ? "stop" : "play", btnSize, highlighted: Playing))
        {
            if (!Playing)
            {
                SceneManager.SaveCurrentScene();
            }
            else
            {
                string? path = SceneManager.CurrentScenePath;

                if (path != null && File.Exists(path))
                {
                    SceneManager.LoadSceneFromFile(path);
                }
            }

            Playing = !Playing;
            Paused = false;
        }

        ImGui.SameLine(0, spacing);

        if (IconButton("pause", btnSize, highlighted: Paused))
            Paused = !Paused;

        ImGui.SameLine(0, spacing);

        ImGui.BeginDisabled(!Paused);
        if (IconButton("step", btnSize))
            stepRequested = true;
        ImGui.EndDisabled();

        // --- Droite : Build ---
        string buildLabel = BuildService.IsBuilding ? "Building..." : "Build";
        float buildWidth = ImGui.CalcTextSize(buildLabel).X + 24f;
        ImGui.SameLine(ImGui.GetWindowWidth() - buildWidth - 12f);

        ImGui.BeginDisabled(BuildService.IsBuilding);
        if (ImGui.Button(buildLabel))
        {
            string projectRoot = ProjectPanel.FindProjectRoot();
            string buildPlatform = OperatingSystem.IsWindows() ? "Windows" :
                OperatingSystem.IsLinux() ? "Linux" : "plateforme non supportée";
            Logger.Log($"[Build] Démarrage du build (Release, {buildPlatform}, self-contained)...");

            BuildService.Build(projectRoot, Title, onLogLine: Logger.Log, onFinished: success =>
            {
                if (success) Logger.Sucess("[Build] Build terminé avec succès — voir le dossier Build/ du projet.");
                else Logger.Error("[Build] Échec du build — voir les lignes ci-dessus dans la Console.");
            });
        }
        ImGui.EndDisabled();

        ImGui.EndChild();
    }

    /// <summary>
    /// Bouton icône carré, avec fallback texte (première lettre en majuscule)
    /// si l'icône n'a pas été trouvée par EditorIcons — évite un bouton vide
    /// et invisible si un .png manque dans Editor/Icons/.
    /// </summary>
    bool IconButton(string iconName, float size, bool highlighted = false)
    {
        if (highlighted)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.59f, 0.98f, 0.6f));

        bool clicked;
        IntPtr icon = EditorIcons.Get(iconName);

        if (icon != IntPtr.Zero)
        {
            clicked = ImGui.ImageButton($"##{iconName}", icon, new System.Numerics.Vector2(size, size));
        }
        else
        {
            // Fallback si l'icône n'existe pas encore sur disque
            clicked = ImGui.Button(iconName.Substring(0, 1).ToUpper(), new System.Numerics.Vector2(size, 0));
        }

        if (highlighted)
            ImGui.PopStyleColor();

        return clicked;
    }

    /// <summary>
    /// Liste centralisée des raccourcis clavier de l'éditeur — ajoute une
    /// entrée ici à chaque nouveau raccourci pour qu'il apparaisse
    /// automatiquement dans cette fenêtre, sans dupliquer la maintenance
    /// ailleurs.
    /// </summary>
    static readonly (string Key, string Action)[] Shortcuts =
    {
        ("Ctrl+R", "Reset Camera"),
        ("Ctrl+S", "Save Scene"),
        ("W", "Move Gizmo"),
        ("E", "Rotate Gizmo"),
        ("R", "Scale Gizmo"),
    };

    void DrawShortcutsWindow()
    {
        if (!showShortcutsWindow) return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(320, 200), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Keyboard Shortcuts", ref showShortcutsWindow))
        {
            ImGui.Columns(2, "##shortcuts_cols", true);
            ImGui.Text("Key");
            ImGui.NextColumn();
            ImGui.Text("Action");
            ImGui.NextColumn();
            ImGui.Separator();

            foreach (var (key, action) in Shortcuts)
            {
                ImGui.Text(key);
                ImGui.NextColumn();
                ImGui.Text(action);
                ImGui.NextColumn();
            }

            ImGui.Columns(1);
        }
        ImGui.End();
    }
}
