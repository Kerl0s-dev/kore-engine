using ImGuiNET;
using SDL3;

namespace KoreEngine;

public class EditorWindow
{
    public static EditorWindow? Current { get; private set; }

    public string ProjectRoot { get; }
    public string ProjectName { get; }

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
    ShaderWatcher? shaderWatcher;

    public EditorWindow(string projectRoot, string? title = null, int width = 1280, int height = 720)
    {
        ProjectRoot = ResolveProjectRoot(projectRoot);
        ProjectName = new DirectoryInfo(ProjectRoot).Name;
        Title = string.IsNullOrWhiteSpace(title) ? $"{ProjectName} - KoreEngine Editor" : title;
        Width = width;
        Height = height;

        // Configurer les chemins du projet AVANT d'initialiser les caches et les panels
        SceneManager.AssetsDirectory = Path.Combine(ProjectRoot, "Assets");
        SceneManager.OnSceneChanging += () => EditorSelection.Selected = null;

        SDL.Init(SDL.InitFlags.Video);
        window = SDL.CreateWindow(Title, width, height, SDL.WindowFlags.Resizable | SDL.WindowFlags.Maximized);
        SDL.StartTextInput(window);

        Renderer = new Renderer(window);
        TextureCache.Init(Renderer);
        FontManager.Init(Renderer);
        EditorIcons.Init(Renderer);
        AudioManager.Init();
        SDL.SetRenderVSync(Renderer.Handle, 1);

        imguiBackend = new ImGuiBackend(Renderer);
        imguiBackend.Init(width, height);

        InitializeImGUIStyle();

        #region Panels Initialization
        var viewportTexture = new RenderTexture(Renderer, width, height);
        var uiTexture = new RenderTexture(Renderer, width, height);

        scenePanel = new ScenePanel(Renderer, viewportTexture);
        gamePanel = new GamePanel(Renderer, width, height);
        uiEditorPanel = new UIEditorPanel(Renderer, uiTexture);
        hierarchyPanel = new HierarchyPanel();
        inspectorPanel = new InspectorPanel(Renderer);
        projectPanel = new ProjectPanel();
        consolePanel = new ConsolePanel();
        animationPreview = new AnimationPreview(Renderer, width, height);
        timelinePanel = new TimelinePanel(animationPreview);
        #endregion
    }

    public static void Main(string[] args)
    {
        string? projectPath = ParseProjectArg(args);

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            Console.Error.WriteLine("Usage: KoreEngine.Editor [--project <path/to/project>]");
            Environment.ExitCode = 1;
            return;
        }

        new EditorWindow(projectPath, width: 1280, height: 720).Run();
    }

    static string? ParseProjectArg(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--project" or "-p" && i + 1 < args.Length)
                return args[i + 1];
        }

        foreach (var arg in args)
        {
            if (!arg.StartsWith('-'))
                return arg;
        }

        return null;
    }

    static string ResolveProjectRoot(string rawPath)
    {
        string fullPath = Path.GetFullPath(rawPath);
        if (File.Exists(fullPath) && fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.GetDirectoryName(fullPath)!;

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Project directory not found: {fullPath}");

        return fullPath;
    }

    public void Run()
    {
        Current = this;

        scriptWatcher = new ScriptWatcher(ProjectRoot);
        shaderWatcher = new ShaderWatcher(ProjectRoot);

        while (Running)
        {
            // 1. Input
            Input.NewFrame();
            while (SDL.PollEvent(out var e))
            {
                switch (e.Type)
                {
                    case (uint)SDL.EventType.Quit:
                    case (uint)SDL.EventType.WindowCloseRequested:
                        showQuitPopup = true;
                        break;
                }

                imguiBackend?.HandleEvent(e);

                if (!ImGui.GetIO().WantCaptureKeyboard || e.Type == (uint)SDL.EventType.KeyUp || e.Type == (uint)SDL.EventType.WindowFocusLost)
                    Input.HandleEvent(e);
            }

            // 2. Resize de la fenêtre réelle
            UpdateWindowSizeIfNeeded();

            // 3. dt
            Time.ComputeDeltaTime();
            scriptWatcher?.Update();
            shaderWatcher?.Update();

            // 4. ImGui NewFrame
            imguiBackend?.NewFrame(Width, Height);

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

                HandleShortcuts(io);
            }

            HandleQuitPopup();

            // 5. Applique la scène en attente — toujours, même en mode édition.
            SceneManager.ApplyPendingScene();
            scriptWatcher?.FinalizeUnloadIfPending();

            if (Playing)
            {
                gamePanel?.BindUiSize();

                if (!Paused)
                    SceneManager.Update();
                else if (stepRequested)
                {
                    SceneManager.Update();
                    stepRequested = false;
                }
            }

            // 6. Rendu.
            scenePanel?.ApplyPendingResize(scenePanel.EditorCamera);
            scenePanel?.RenderScene(SceneManager.Current, scenePanel.EditorCamera, editorMode: true);

            uiEditorPanel?.ApplyPendingResize(uiEditorPanel.View);
            uiEditorPanel?.RenderScene(SceneManager.Current);

            gamePanel?.ApplyPendingResize();
            gamePanel?.RenderScene(SceneManager.Current);

            Renderer.Clear(new Color(115, 115, 115));

            #region Panel Drawing
            DrawDockspace();
            hierarchyPanel?.Draw();
            inspectorPanel?.Draw();
            projectPanel?.Draw();
            consolePanel?.Draw();
            timelinePanel?.Draw();
            animationPreview?.Draw();
            uiEditorPanel?.Draw();
            gamePanel?.Draw();
            scenePanel?.Draw(editorMode: true);
            CompilationProgressWindow.Draw();
            #endregion

            // 8. ImGui render
            imguiBackend?.Render();

            // 9. Present
            Renderer.Present();
        }

        Shutdown();
    }

    void Shutdown()
    {
        ScriptCompiler.RemoveLoadedAssembly();
        AudioManager.Quit();
        imguiBackend?.Shutdown();

        if (window != IntPtr.Zero)
        {
            SDL.DestroyWindow(window);
            window = IntPtr.Zero;
        }

        SDL.Quit();
        if (Current == this)
            Current = null;
    }

    private void HandleQuitPopup()
    {
        if (showQuitPopup)
        {
            var viewport = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(viewport, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
            ImGui.OpenPopup("##quit_popup");
            showQuitPopup = false;
        }

        if (ImGui.BeginPopupModal("##quit_popup",
            ImGuiWindowFlags.NoNavInputs | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove |
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
                Running = false;
            }

            ImGui.EndPopup();
        }
    }

    void UpdateWindowSizeIfNeeded()
    {
        SDL.GetWindowSize(window, out int w, out int h);
        if (w != Width || h != Height)
        {
            Width = w;
            Height = h;
        }
    }

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

    void DrawToolbar()
    {
        ImGui.BeginChild("##toolbar", new System.Numerics.Vector2(0, 38));

        const float btnSize = 16f;
        const float spacing = 4f;

        bool isInWorldSpace = EditorSelection.ActiveGizmoSpace == GizmoSpace.World;

        if (Widgets.IconTextButton(isInWorldSpace ? "world" : "local", 20))
            EditorSelection.ActiveGizmoSpace = isInWorldSpace ? GizmoSpace.Local : GizmoSpace.World;

        float centerWidth = btnSize * 3 + spacing * 2;

        float centerX = (ImGui.GetWindowWidth() - centerWidth) * 0.5f;
        ImGui.SameLine(centerX);
        ImGui.PushID("play");
        if (Widgets.IconButton(Playing ? "stop" : "play", btnSize, highlighted: Playing))
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
        if (Widgets.IconButton("pause", btnSize, highlighted: Paused))
            Paused = !Paused;

        ImGui.SameLine(0, spacing);
        ImGui.PopID();
        ImGui.BeginDisabled(!Paused);
        if (Widgets.IconButton("step", btnSize))
            stepRequested = true;
        ImGui.EndDisabled();

        string buildLabel = BuildService.IsBuilding ? "Building..." : "Build";
        float buildWidth = ImGui.CalcTextSize(buildLabel).X + 24f;
        ImGui.SameLine(ImGui.GetWindowWidth() - buildWidth - 12f);

        ImGui.BeginDisabled(BuildService.IsBuilding);
        if (ImGui.Button(buildLabel))
        {
            string buildPlatform = OperatingSystem.IsWindows() ? "Windows" :
                OperatingSystem.IsLinux() ? "Linux" : "plateforme non supportée";
            Logger.Log($"[Build] Démarrage du build (Release, {buildPlatform}, self-contained)...");

            BuildService.Build(ProjectRoot, ProjectName, onLogLine: Logger.Log, onFinished: success =>
            {
                if (success) Logger.Sucess("[Build] Build terminé avec succès — voir le dossier Build/ du projet.");
                else Logger.Error("[Build] Échec du build — voir les lignes ci-dessus dans la Console.");
            });
        }
        ImGui.EndDisabled();

        ImGui.EndChild();
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
            if (ImGui.IsKeyPressed(ImGuiKey.W)) EditorSelection.ActiveGizmoMode = GizmoMode.Move;
            if (ImGui.IsKeyPressed(ImGuiKey.E)) EditorSelection.ActiveGizmoMode = GizmoMode.Rotate;
            if (ImGui.IsKeyPressed(ImGuiKey.R)) EditorSelection.ActiveGizmoMode = GizmoMode.Scale;

            if (ImGui.IsKeyPressed(ImGuiKey.F) && EditorSelection.Selected != null)
                scenePanel?.FocusOn(EditorSelection.Selected.transform.WorldPosition);
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

    private void InitializeImGUIStyle()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        var colors = style.Colors;

        style.WindowPadding = new System.Numerics.Vector2(10, 10);
        style.FramePadding = new System.Numerics.Vector2(8, 5);
        style.CellPadding = new System.Numerics.Vector2(6, 4);
        style.ItemSpacing = new System.Numerics.Vector2(8, 6);
        style.ItemInnerSpacing = new System.Numerics.Vector2(6, 4);
        style.IndentSpacing = 16f;
        style.ScrollbarSize = 12f;
        style.GrabMinSize = 10f;

        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.PopupBorderSize = 1f;
        style.WindowRounding = 0f;
        style.ChildRounding = 3f;
        style.FrameRounding = 3f;
        style.PopupRounding = 3f;
        style.ScrollbarRounding = 6f;
        style.GrabRounding = 3f;
        style.TabRounding = 3f;
        style.DockingSeparatorSize = 2f;

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
        colors[(int)ImGuiCol.NavWindowingHighlight] = new(1.00f, 1.00f, 1.00f, 0.25f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
    }
}