using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using KoreEngine.Hub.Models;
using KoreEngine.Hub.Services;
using KoreEngine.Hub.Ui;
using SDL3;

namespace KoreEngine.Hub;

public sealed class HubWindow : IDisposable
{
    IntPtr window;

    public IntPtr Renderer { get; }
    public ImageCache Images { get; }
    public TextCache Text { get; }

    // Nom du fichier dans KoreEngine.Hub\Fonts\ (copié à côté de l'exe au build).
    const string FontFile = "RobotoMono-VariableFont_wght.ttf";
    public string Title { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Running { get; private set; }

    const int MinWidth = 960;
    const int MinHeight = 540;

    // Sous Windows, glisser/redimensionner la fenêtre bloque PollEvent (boucle modale
    // Win32). Un "event watch" est appelé quand même : on y redessine la frame.
    // Le delegate doit rester référencé en champ, sinon le GC le collecte.
    readonly SDL.EventFilter resizeWatch;
    readonly int mainThreadId = Environment.CurrentManagedThreadId;
    bool rendering;

    // --- UI ---
    readonly UiContext ui;
    readonly List<Widget> widgets = new();
    Label? titleLabel;
    Label? statusLabel;
    Label? emptyLabel;
    Button? newProjectButton;
    Button? openButton;
    ProjectList? projectList;
    bool opening; // un build/lancement est en cours
    NewProjectDialog? newProjectDialog;
    LoadingDialog? loadingDialog;

    // Les tâches de fond (création, build) ne touchent pas à l'UI : elles postent ici
    // des actions que le thread de rendu exécute au début de la frame suivante.
    readonly ConcurrentQueue<Action> mainThreadQueue = new();

    // Le delegate du sélecteur de dossier doit rester référencé (sinon collecté par le GC).
    SDL.DialogFileCallback? folderCallback;

    public HubWindow(string title, int width, int height)
    {
        Title = title;
        Width = width;
        Height = height;

        if (!SDL.Init(SDL.InitFlags.Video))
            throw new Exception($"SDL.Init a échoué : {SDL.GetError()}");

        window = SDL.CreateWindow(Title, Width, Height, SDL.WindowFlags.Resizable);
        if (window == IntPtr.Zero)
            throw new Exception($"SDL.CreateWindow a échoué : {SDL.GetError()}");

        SDL.SetWindowMinimumSize(window, MinWidth, MinHeight);

        Renderer = SDL.CreateRenderer(window, null);
        if (Renderer == IntPtr.Zero)
            throw new Exception($"SDL.CreateRenderer a échoué : {SDL.GetError()}");

        Images = new ImageCache(Renderer);
        Text = new TextCache(Renderer, Path.Combine(AppContext.BaseDirectory, "Fonts", FontFile));
        ui = new UiContext(Renderer, Text, Images);

        BuildUi();

        SDL.StartTextInput(window);

        resizeWatch = OnEventWatch;
        SDL.AddEventWatch(resizeWatch, IntPtr.Zero);
    }

    static void Main(string[] args)
    {
        using var hub = new HubWindow("Kore Engine Hub", 960, 540);
        hub.Run();
    }

    /// <summary>Crée les widgets. Les positions sont (re)calculées dans Layout().</summary>
    void BuildUi()
    {
        titleLabel = new Label("Kore Engine Hub", 28);
        statusLabel = new Label("", 14) { Color = Rgba.TextDim };
        emptyLabel = new Label("Aucun projet récent", 18) { Color = Rgba.TextDim };

        newProjectButton = new Button("Nouveau projet", 200, 40, Button.Style.Primary);
        newProjectButton.Click += ShowNewProjectDialog;

        openButton = new Button("Ouvrir", 120, 40, Button.Style.Secondary) { Enabled = false };
        openButton.Click += () => { if (projectList?.Selected is { } p) OpenProject(p); };

        projectList = new ProjectList();
        projectList.SelectionChanged += _ => RefreshButtons();
        projectList.Open += OpenProject;

        loadingDialog = new LoadingDialog();

        newProjectDialog = new NewProjectDialog();
        newProjectDialog.Submit += CreateProject;
        newProjectDialog.Cancel += () => newProjectDialog.Close();
        newProjectDialog.Browse += BrowseFolder;

        widgets.Add(projectList);
        widgets.Add(emptyLabel);
        widgets.Add(titleLabel);
        widgets.Add(statusLabel);
        widgets.Add(newProjectButton);
        widgets.Add(openButton);

        LoadProjects();
    }

    /// <summary>Charge la liste des projets récents, puis calcule taille et version en tâche de fond.</summary>
    void LoadProjects()
    {
        var items = RecentProjectsStore.Load()
            .Select(entry => new ProjectListItem(entry))
            .ToList();

        projectList!.Items = items;

        // Lecture disque potentiellement lente : hors du thread de rendu.
        Task.Run(() =>
        {
            foreach (var item in items)
            {
                item.Exists = item.Entry.Exists;
                if (!item.Exists) continue;

                item.EditorVersion = ProjectInfoService.GetEngineVersion(item.Path);
                item.Size = ProjectInfoService.FormatSize(ProjectInfoService.GetDirectorySize(item.Path));
            }
        });
    }

    void OpenProject(ProjectListItem project)
    {
        if (opening) return;

        if (!project.Exists)
        {
            SetStatus($"Projet introuvable : {project.Path}");
            return;
        }

        opening = true;
        RefreshButtons();
        loadingDialog!.Show(project.Name);

        string? engineDir = GetEngineDir();

        // Tout le chargement (synchro des dépendances, clean, build) tourne hors du thread de
        // rendu pour que la fenêtre de chargement reste animée. Les callbacks repassent par
        // RunOnMain. (LaunchExe met déjà à jour la date « dernière ouverture ».)
        Task.Run(() =>
        {
            try
            {
                EditorLauncher.OpenProject(
                    project.Entry,
                    engineDir,
                    line =>
                    {
                        Console.WriteLine(line);
                        RunOnMain(() => loadingDialog.AddLog(line));
                    },
                    success => RunOnMain(() =>
                    {
                        opening = false;
                        if (success)
                        {
                            loadingDialog.Succeed();
                            SetStatus($"« {project.Name} » lancé.");
                        }
                        else
                        {
                            loadingDialog.Fail();
                            SetStatus($"Échec de l'ouverture de « {project.Name} ».");
                        }
                    }),
                    stage => RunOnMain(() => loadingDialog.SetStage(stage)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hub] Ouverture du projet échouée : {ex}");
                RunOnMain(() =>
                {
                    opening = false;
                    loadingDialog.AddLog($"error: {ex.Message}");
                    loadingDialog.Fail();
                    SetStatus($"Échec de l'ouverture de « {project.Name} ».");
                });
            }
        });
    }

    string? GetEngineDir() => HubSettingsStore.Load().EngineDir ?? EngineLocator.AutoDetect();

    void RunOnMain(Action action) => mainThreadQueue.Enqueue(action);

    // ---------------- Nouveau projet ----------------

    void ShowNewProjectDialog()
    {
        string folder = HubSettingsStore.Load().LastProjectsDir
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KoreEngine Projects");

        newProjectDialog!.Show(folder);
    }

    void CreateProject(string name, string parentFolder)
    {
        string? engineDir = GetEngineDir();
        if (engineDir == null)
        {
            newProjectDialog!.SetError("Dossier du moteur introuvable (KoreEngine.Runtime / Editor non compilés ?).");
            return;
        }

        newProjectDialog!.SetBusy(true);

        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(parentFolder);
                ProjectScaffolder.Create(engineDir, parentFolder, name, Console.WriteLine);

                string projectPath = Path.Combine(parentFolder, name);
                RecentProjectsStore.AddOrUpdate(new RecentProjectEntry(name, projectPath, DateTime.Now));

                var settings = HubSettingsStore.Load();
                settings.LastProjectsDir = parentFolder;
                HubSettingsStore.Save(settings);

                RunOnMain(() =>
                {
                    newProjectDialog.Close();
                    LoadProjects();
                    SetStatus($"Projet « {name} » créé.");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hub] Création du projet échouée : {ex}");
                RunOnMain(() =>
                {
                    newProjectDialog.SetBusy(false);
                    newProjectDialog.SetError(ex.Message.Split('\n')[0]);
                });
            }
        });
    }

    void BrowseFolder()
    {
        folderCallback ??= OnFolderSelected;
        string start = HubSettingsStore.Load().LastProjectsDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        SDL.ShowOpenFolderDialog(folderCallback, IntPtr.Zero, window, start, false);
    }

    // Peut être appelé depuis un autre thread : filelist est un tableau natif de chaînes
    // UTF-8 terminé par NULL (NULL lui-même = erreur, tableau vide = annulé).
    void OnFolderSelected(IntPtr userdata, IntPtr filelist, int filter)
    {
        if (filelist == IntPtr.Zero) return;

        IntPtr first = Marshal.ReadIntPtr(filelist);
        if (first == IntPtr.Zero) return;

        string? path = Marshal.PtrToStringUTF8(first);
        if (!string.IsNullOrEmpty(path))
            RunOnMain(() => newProjectDialog?.SetFolder(path));
    }

    void SetStatus(string text)
    {
        if (statusLabel != null) statusLabel.Text = text;
    }

    void RefreshButtons()
    {
        if (openButton != null)
            openButton.Enabled = projectList?.Selected != null && !opening;
    }

    /// <summary>Positionne les widgets selon la taille de la fenêtre.</summary>
    void Layout()
    {
        const float margin = 32;
        const float headerHeight = 96;
        const float footerHeight = 48;

        titleLabel!.X = margin; titleLabel.Y = 24;

        newProjectButton!.X = Width - newProjectButton.Width - margin;
        newProjectButton.Y = 24;

        openButton!.X = newProjectButton.X - openButton.Width - 12;
        openButton.Y = 24;

        projectList!.X = margin;
        projectList.Y = headerHeight;
        projectList.Width = Width - margin * 2;
        projectList.Height = Math.Max(0, Height - headerHeight - footerHeight);

        statusLabel!.X = margin;
        statusLabel.Y = Height - footerHeight + 12;

        // Message « liste vide », centré dans la zone de la liste.
        emptyLabel!.Visible = projectList.Items.Count == 0;
        var (ew, eh) = Text.Measure(emptyLabel.Text, emptyLabel.FontSize);
        emptyLabel.X = projectList.X + (projectList.Width - ew) / 2f;
        emptyLabel.Y = projectList.Y + (projectList.Height - eh) / 2f;

        RefreshButtons();
    }

    public void Run()
    {
        Running = true;

        while (Running)
        {
            while (SDL.PollEvent(out var e))
                HandleEvent(e);

            Render();
        }
    }

    /// <summary>Met à jour l'UI et dessine une frame. Appelé par la boucle et par le watch de resize.</summary>
    void Render()
    {
        if (rendering) return; // évite la réentrance (RenderPresent peut pomper des events)
        rendering = true;

        try
        {
            Text.BeginFrame();

            SDL.GetWindowSize(window, out int w, out int h);
            Width = w;
            Height = h;
            ui.WindowWidth = w;
            ui.WindowHeight = h;

            while (mainThreadQueue.TryDequeue(out var action))
                action();

            Layout();

            ui.Input.Update();

            // Une fenêtre modale ouverte (chargement ou « Nouveau projet ») reçoit seule les entrées.
            if (loadingDialog is { Visible: true })
            {
                loadingDialog.Update(ui);
            }
            else if (newProjectDialog is { Visible: true })
            {
                newProjectDialog.Update(ui);
            }
            else
            {
                foreach (var widget in widgets)
                    if (widget.Visible) widget.Update(ui);
            }

            SDL.SetRenderDrawColor(Renderer, 32, 32, 32, 255);
            SDL.RenderClear(Renderer);

            foreach (var widget in widgets)
                if (widget.Visible) widget.Draw(ui);

            if (newProjectDialog is { Visible: true })
                newProjectDialog.Draw(ui);

            if (loadingDialog is { Visible: true })
                loadingDialog.Draw(ui);

            SDL.FlushRenderer(Renderer);
            SDL.RenderPresent(Renderer);
        }
        finally
        {
            rendering = false;
        }
    }

    bool OnEventWatch(IntPtr userdata, ref SDL.Event e)
    {
        // Le watch peut être appelé depuis un autre thread : on ne dessine que sur le principal.
        if (Environment.CurrentManagedThreadId != mainThreadId) return true;

        switch (e.Type)
        {
            case (uint)SDL.EventType.WindowExposed:
            case (uint)SDL.EventType.WindowResized:
            case (uint)SDL.EventType.WindowPixelSizeChanged:
                Render();
                break;
        }
        return true;
    }

    void HandleEvent(SDL.Event e)
    {
        switch (e.Type)
        {
            case (uint)SDL.EventType.Quit:
            case (uint)SDL.EventType.WindowCloseRequested:
                Running = false;
                break;

            case (uint)SDL.EventType.MouseWheel:
                ui.Input.AddWheel(e.Wheel.Y);
                break;

            case (uint)SDL.EventType.TextInput:
                ui.Input.AddText(ReadTextInput(e));
                break;

            case (uint)SDL.EventType.KeyDown:
                // 0x00C0 = Ctrl gauche ou droit
                ui.Input.AddKey((uint)e.Key.Key, ((uint)SDL.GetModState() & 0x00C0u) != 0);
                break;
        }
    }

    // Isolé ici : c'est le seul endroit qui dépend de la forme exacte de l'événement TextInput.
    static string ReadTextInput(SDL.Event e)
    {
        // Text.Text est un pointeur natif (nint) vers une chaîne UTF-8 terminée par NULL.
        // Il n'appartient pas à nous : on le lit sans le libérer.
        IntPtr ptr = e.Text.Text;
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
    }

    public void Dispose()
    {
        SDL.RemoveEventWatch(resizeWatch, IntPtr.Zero);
        Text.Dispose();   // avant le renderer
        Images.Dispose();
        SDL.DestroyRenderer(Renderer);
        SDL.DestroyWindow(window);
        SDL.Quit();
    }
}