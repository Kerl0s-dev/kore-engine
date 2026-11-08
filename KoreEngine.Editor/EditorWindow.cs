using ImGuiNET;
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

    #region Panels
    public ScenePanel? scenePanel;
    public GamePanel? gamePanel;
    public UIEditorPanel? uiEditorPanel;
    public HierarchyPanel? hierarchyPanel;
    public InspectorPanel? inspectorPanel;
    public ProjectPanel? projectPanel;
    public ConsolePanel? consolePanel;
    public AnimationPreview? animationPreview;
    public TimelinePanel? timelinePanel;
    #endregion

    ScriptWatcher? scriptWatcher;

    public EditorWindow(string title, int width, int height)
    {
        Title = title;
        Width = width;
        Height = height;

        SDL.Init(SDL.InitFlags.Video);
        window = SDL.CreateWindow(title, width, height, SDL.WindowFlags.Resizable);
        SDL.MaximizeWindow(window);

        SDL.StartTextInput(window); // active les événements TextInput SDL3 (Cela permet de modifier des champs textuels)

        Renderer = new Renderer(window);
        TextureCache.Init(Renderer); // Initialise le cache de texture
        FontManager.Init(Renderer); // Initialise le cache de polices
        EditorIcons.Init(Renderer); //Initialise les icônes de l'éditeur
        AudioManager.Init(); // Initialise le moteur audio (SDL_mixer)

        imguiBackend = new ImGuiBackend(Renderer);
        imguiBackend.Init(width, height);

        InitializeImGUIStyle();

        #region Panels Initialization
        var viewportTexture = new RenderTexture(Renderer, width, height);
        scenePanel = new ScenePanel(Renderer, viewportTexture);
        gamePanel = new GamePanel(Renderer, width, height);
        uiEditorPanel = new UIEditorPanel(Renderer, width, height);
        hierarchyPanel = new HierarchyPanel();
        inspectorPanel = new InspectorPanel(Renderer);
        projectPanel = new ProjectPanel();
        consolePanel = new ConsolePanel();
        animationPreview = new AnimationPreview(Renderer, width, height);
        timelinePanel = new TimelinePanel(animationPreview);
        #endregion

        SceneManager.OnSceneChanging += () => EditorSelection.Selected = null;

        SceneManager.AssetsDirectory = Path.Combine(ProjectPanel.FindProjectRoot(), "Assets");
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
                if (!ImGui.GetIO().WantCaptureKeyboard || e.Type == (uint)SDL.EventType.KeyUp || e.Type == (uint)SDL.EventType.WindowFocusLost)
                    Input.HandleEvent(e);
            }

            // 2. Resize de la fenêtre réelle
            UpdateWindowSizeIfNeeded();

            // 3. dt
            Time.ComputeDeltaTime();
            scriptWatcher?.Update();

            // 4. ImGui NewFrame
            imguiBackend?.NewFrame(Width, Height);

            HandleQuitPopup();

            // 5. Applique la scène en attente — toujours, même en mode édition.
            SceneManager.ApplyPendingScene();
            scriptWatcher?.FinalizeUnloadIfPending();

            // Logique de jeu — uniquement si Playing, et pas en pause (sauf step demandé).
            if (Playing)
            {
                gamePanel?.BindUiSize(); // l'UI doit voir la taille du Game view, pas celle du Scene view

                if (!Paused)
                    SceneManager.Update();
                else if (stepRequested)
                {
                    SceneManager.Update();
                    stepRequested = false;
                }
            }

            // 6. Rendu.
            // Scene view : toujours EditorCamera (comme Unity, même en Play)
            scenePanel?.ApplyPendingResize(scenePanel.EditorCamera);
            scenePanel?.RenderScene(SceneManager.Current, scenePanel.EditorCamera, editorMode: true);

            // Game view : toujours la caméra de la scène
            gamePanel?.ApplyPendingResize();
            gamePanel?.RenderScene(SceneManager.Current);

            uiEditorPanel?.ApplyPendingResize();
            uiEditorPanel?.RenderScene(SceneManager.Current);

            // Écran réel : clear + panneaux ImGui.
            Renderer.Clear(new Color(115, 115, 115));

            #region Panel Drawing
            DrawDockspace();
            hierarchyPanel?.Draw();
            inspectorPanel?.Draw();
            projectPanel?.Draw();
            scenePanel?.Draw(editorMode: true);
            gamePanel?.Draw();
            uiEditorPanel?.Draw();
            consolePanel?.Draw();
            timelinePanel?.Draw();
            animationPreview?.Draw();
            CompilationProgressWindow.Draw();
            #endregion

            // 8. ImGui render
            imguiBackend?.Render();

            // 9. Present
            Renderer.Present();
        }
    }

    private void HandleQuitPopup()
    {
        // Popup de confirmation de fermeture de l'éditeur
        if (showQuitPopup)
        {
            var viewport = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(viewport, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
            ImGui.OpenPopup("##quit_popup");
            showQuitPopup = false;
        }

        if (ImGui.BeginPopupModal("##quit_popup",
            ImGuiWindowFlags.NoNavInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNavInputs |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoTitleBar))
        {
            ImGui.Text("Are you sure you want to quit Kore Engine?");
            ImGui.Spacing();
            ImGui.Spacing();
            if (ImGui.Button("No", new System.Numerics.Vector2(150, 20)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Yes", new System.Numerics.Vector2(150, 20)))
            {
                ScriptCompiler.RemoveLoadedAssembly();

                Running = false; // Quitter la boucle
            }

            ImGui.EndPopup();
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

        if (ScriptCompiler.IsCompiling)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 0.8f, 0, 1), ScriptCompiler.Status);
        }

        // --- Centre : Play/Pause/Stop/Step ---
        const float btnSize = 16f;
        const float spacing = 4f;

        float centerWidth = btnSize * 3 + spacing * 2; // Stop, Pause, Step

        float centerX = (ImGui.GetWindowWidth() - centerWidth) * 0.5f;
        ImGui.SameLine(centerX);
        ImGui.PushID("play");
        if (IconButton(Playing ? "stop" : "play", btnSize, highlighted: Playing))
        {
            if (!Playing)
            {
                SceneManager.SaveCurrentScene();
                scriptWatcher?.ReloadAssemblies();
            }
            else
            {
                AudioManager.StopAllTracks(0);
                string? path = SceneManager.CurrentScenePath;

                if (path != null && File.Exists(path))
                {
                    SceneManager.LoadSceneFromFile(path);
                }
            }

            Playing = !Playing;
            Paused = false;
        }
        ImGui.PopID();

        ImGui.SameLine(0, spacing);
        ImGui.PushID("pause");
        if (IconButton("pause", btnSize, highlighted: Paused))
            Paused = !Paused;

        ImGui.SameLine(0, spacing);
        ImGui.PopID();
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

    #region Shortcuts
    static readonly (string Key, string Action)[] Shortcuts =
    {
        ("Ctrl+R", "Reset Camera"),
        ("Ctrl+S", "Save Scene"),
        ("W", "Move Gizmo"),
        ("E", "Rotate Gizmo"),
        ("R", "Scale Gizmo"),
    };

    private void HandleShortcuts(ImGuiIOPtr io)
    {
        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.S))
            {
                string? name = SceneManager.CurrentSceneName;
                if (name != null)
                    SceneManager.SaveCurrentScene();
            }
        }
        else
        {
            if (ImGui.IsKeyPressed(ImGuiKey.W)) EditorSelection.ActiveGizmoMode = GizmoMode.Move; // Change the selection gizmo to 'Move'
            if (ImGui.IsKeyPressed(ImGuiKey.E)) EditorSelection.ActiveGizmoMode = GizmoMode.Rotate; // Change the selection gizmo to 'Rotate'
            if (ImGui.IsKeyPressed(ImGuiKey.R)) EditorSelection.ActiveGizmoMode = GizmoMode.Scale; // Change the selection gizmo to 'Scale'

            if (ImGui.IsKeyPressed(ImGuiKey.F) && EditorSelection.Selected != null) scenePanel?.FocusOn(EditorSelection.Selected.transform.WorldPosition);
        }
    }

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
    #endregion

    // Style de ImGUI pour l'éditeur
    private void InitializeImGUIStyle()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        var colors = style.Colors;

        colors[(int)ImGuiCol.Text] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new(0.50f, 0.50f, 0.50f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new(0.12f, 0.12f, 0.12f, 0.94f);
        colors[(int)ImGuiCol.ChildBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.PopupBg] = new(0.08f, 0.08f, 0.08f, 0.94f);
        colors[(int)ImGuiCol.Border] = new(0.00f, 0.00f, 0.00f, 0.50f);
        colors[(int)ImGuiCol.BorderShadow] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.FrameBg] = new(0.18f, 0.18f, 0.18f, 0.54f);
        colors[(int)ImGuiCol.FrameBgHovered] = new(0.35f, 0.35f, 0.35f, 0.40f);
        colors[(int)ImGuiCol.FrameBgActive] = new(0.09f, 0.09f, 0.09f, 0.67f);
        colors[(int)ImGuiCol.TitleBg] = new(0.04f, 0.04f, 0.04f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new(0.11f, 0.11f, 0.11f, 1.00f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new(0.00f, 0.00f, 0.00f, 0.51f);
        colors[(int)ImGuiCol.MenuBarBg] = new(0.14f, 0.14f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new(0.02f, 0.02f, 0.02f, 0.53f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new(0.31f, 0.31f, 0.31f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new(0.41f, 0.41f, 0.41f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new(0.51f, 0.51f, 0.51f, 1.00f);
        colors[(int)ImGuiCol.CheckMark] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.SliderGrab] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.SliderGrabActive] = new(0.69f, 0.69f, 0.69f, 1.00f);
        colors[(int)ImGuiCol.Button] = new(0.27f, 0.27f, 0.27f, 0.40f);
        colors[(int)ImGuiCol.ButtonHovered] = new(0.39f, 0.39f, 0.39f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = new(0.14f, 0.14f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.Header] = new(0.33f, 0.33f, 0.33f, 0.31f);
        colors[(int)ImGuiCol.HeaderHovered] = new(0.45f, 0.45f, 0.45f, 0.80f);
        colors[(int)ImGuiCol.HeaderActive] = new(0.15f, 0.15f, 0.15f, 1.00f);
        colors[(int)ImGuiCol.Separator] = new(0.49f, 0.49f, 0.49f, 0.50f);
        colors[(int)ImGuiCol.SeparatorHovered] = new(0.54f, 0.54f, 0.54f, 0.78f);
        colors[(int)ImGuiCol.SeparatorActive] = new(0.29f, 0.29f, 0.29f, 1.00f);
        colors[(int)ImGuiCol.ResizeGrip] = new(0.27f, 0.27f, 0.27f, 0.20f);
        colors[(int)ImGuiCol.ResizeGripHovered] = new(0.43f, 0.43f, 0.43f, 0.67f);
        colors[(int)ImGuiCol.ResizeGripActive] = new(0.16f, 0.16f, 0.16f, 0.95f);
        colors[(int)ImGuiCol.TabHovered] = new(0.49f, 0.49f, 0.49f, 0.80f);
        colors[(int)ImGuiCol.Tab] = new(0.15f, 0.15f, 0.15f, 0.86f);
        colors[(int)ImGuiCol.TabSelected] = new(0.31f, 0.31f, 0.31f, 1.00f);
        colors[(int)ImGuiCol.TabSelectedOverline] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.TabDimmed] = new(0.10f, 0.10f, 0.10f, 0.97f);
        colors[(int)ImGuiCol.TabDimmedSelected] = new(0.32f, 0.32f, 0.32f, 1.00f);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new(0.50f, 0.50f, 0.50f, 0.00f);
        colors[(int)ImGuiCol.DockingPreview] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.PlotLines] = new(0.00f, 0.00f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new(0.00f, 0.00f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new(0.00f, 0.00f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new(0.00f, 0.00f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new(0.19f, 0.19f, 0.20f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new(0.31f, 0.31f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.TableBorderLight] = new(0.23f, 0.23f, 0.25f, 1.00f);
        colors[(int)ImGuiCol.TableRowBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TableRowBgAlt] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TextLink] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.TextSelectedBg] = new(0.97f, 0.97f, 0.97f, 0.35f);
        colors[(int)ImGuiCol.DragDropTarget] = new(0.00f, 0.00f, 0.00f, 0.25f);
        colors[(int)ImGuiCol.NavCursor] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.NavWindowingHighlight] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
    }
}
