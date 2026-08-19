using KoreEngine.Hub.Models;
using KoreEngine.Hub.Services;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;

namespace KoreEngine.Hub;

public partial class MainWindow : Window
{
    readonly ObservableCollection<ProjectListItem> _projects = new();
    ICollectionView? _projectsView;
    string? _engineDir;

    public MainWindow()
    {
        InitializeComponent();

        _projectsView = CollectionViewSource.GetDefaultView(_projects);
        _projectsView.Filter = FilterProject;
        ProjectsList.ItemsSource = _projectsView;

        ResolveEngineDir();
        RefreshRecentList();
    }

    bool FilterProject(object obj)
    {
        if (obj is not ProjectListItem item) return false;
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        return query.Length == 0 || item.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase);
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _projectsView?.Refresh();

    void ResolveEngineDir()
    {
        var settings = HubSettingsStore.Load();

        if (settings.EngineDir != null && EngineLocator.IsValidEngineDir(settings.EngineDir))
        {
            _engineDir = settings.EngineDir;
            return;
        }

        var detected = EngineLocator.AutoDetect();
        if (detected != null && EngineLocator.IsValidEngineDir(detected))
        {
            _engineDir = detected;
            HubSettingsStore.Save(new HubSettings { EngineDir = detected });
            return;
        }

        MessageBox.Show(
            "Impossible de localiser automatiquement KoreEngine.Runtime / KoreEngine.Editor.\n" +
            "Merci de sélectionner le dossier racine du moteur.",
            "KoreEngine Hub", MessageBoxButton.OK, MessageBoxImage.Information);

        PromptForEngineDir();
    }

    void PromptForEngineDir()
    {
        var dlg = new OpenFolderDialog { Title = "Sélectionne le dossier racine de KoreEngine" };

        if (dlg.ShowDialog() == true)
        {
            if (EngineLocator.IsValidEngineDir(dlg.FolderName))
            {
                _engineDir = dlg.FolderName;
                HubSettingsStore.Save(new HubSettings { EngineDir = dlg.FolderName });
            }
            else
            {
                MessageBox.Show(
                    "Ce dossier ne contient pas KoreEngine.Runtime.csproj et KoreEngine.Editor.csproj.",
                    "KoreEngine Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    void RefreshRecentList()
    {
        _projects.Clear();

        foreach (var entry in RecentProjectsStore.Load())
        {
            var item = new ProjectListItem(entry);
            _projects.Add(item);
            LoadProjectDetailsAsync(item);
        }

        UpdateEmptyState();
    }

    void UpdateEmptyState()
    {
        bool hasProjects = _projects.Count > 0;
        EmptyState.Visibility = hasProjects ? Visibility.Collapsed : Visibility.Visible;
        ProjectsList.Visibility = hasProjects ? Visibility.Visible : Visibility.Collapsed;
    }

    void LoadProjectDetailsAsync(ProjectListItem item)
    {
        if (!item.Exists)
        {
            item.Size = "—";
            item.EditorVersion = "—";
            return;
        }

        Task.Run(() =>
        {
            long bytes = ProjectInfoService.GetDirectorySize(item.Path);
            string version = ProjectInfoService.GetEngineVersion(item.Path);

            Dispatcher.Invoke(() =>
            {
                item.Size = ProjectInfoService.FormatSize(bytes);
                item.EditorVersion = version;
            });
        });
    }

    void ChangeEngineDir_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => PromptForEngineDir();

    // -------- Barre de titre custom --------

    void Window_SourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowCornerHelper.EnableRoundedCorners(hwnd);
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // E923 = ChromeRestore, E922 = ChromeMaximize (glyphes Segoe MDL2 Assets)
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_engineDir == null)
        {
            MessageBox.Show("Dossier moteur non configuré.", "KoreEngine Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new NewProjectWindow(_engineDir) { Owner = this };

        if (dlg.ShowDialog() == true && dlg.CreatedEntry != null)
        {
            RecentProjectsStore.AddOrUpdate(dlg.CreatedEntry);
            RefreshRecentList();
            LaunchWithLog(dlg.CreatedEntry);
        }
    }

    void ImportProject_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Sélectionne le dossier d'un projet KoreEngine existant" };
        if (dlg.ShowDialog() != true) return;

        var slnFiles = Directory.GetFiles(dlg.FolderName, "*.sln");

        if (slnFiles.Length != 1)
        {
            MessageBox.Show(
                slnFiles.Length == 0
                    ? "Aucun fichier .sln trouvé dans ce dossier."
                    : "Plusieurs fichiers .sln trouvés — sélectionne le dossier racine d'un seul projet.",
                "KoreEngine Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string projectName = Path.GetFileNameWithoutExtension(slnFiles[0]);
        var entry = new RecentProjectEntry(projectName, dlg.FolderName, DateTime.Now);

        RecentProjectsStore.AddOrUpdate(entry);
        RefreshRecentList();
    }

    void ProjectsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProjectsList.SelectedItem is ProjectListItem item && item.Exists)
            LaunchWithLog(item.Entry);
    }

    void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectListItem item })
        {
            RecentProjectsStore.Remove(item.Path);
            RefreshRecentList();
        }
    }

    /// <summary>
    /// Resynchronise manuellement les dépendances du moteur pour un projet,
    /// sans passer par un clean+build+lancement complet — utile juste après
    /// avoir recompilé le moteur, pour vérifier que la copie fonctionne sans
    /// attendre le prochain lancement.
    /// </summary>
    void RefreshDependencies_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProjectListItem item }) return;

        if (_engineDir == null)
        {
            MessageBox.Show("Dossier moteur non configuré.", "KoreEngine Hub", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var logLines = new List<string>();
        EditorLauncher.SyncEngineDependencies(item.Entry, _engineDir, logLines.Add);

        MessageBox.Show(string.Join("\n", logLines), "Dépendances resynchronisées",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void LaunchWithLog(RecentProjectEntry entry)
    {
        var logWindow = new BuildLogWindow(entry.Name) { Owner = this };
        logWindow.Show();

        EditorLauncher.OpenProject(
            entry,
            _engineDir,
            onLogLine: line => Dispatcher.Invoke(() => logWindow.AppendLine(line)),
            onBuildFinished: success => Dispatcher.Invoke(() =>
            {
                logWindow.SetFinished(success);

                if (success)
                {
                    logWindow.Close();
                    RefreshRecentList();
                }
            }));
    }
}
