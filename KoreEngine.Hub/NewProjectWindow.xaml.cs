using System.IO;
using System.Windows;
using System.Windows.Interop;
using KoreEngine.Hub.Models;
using KoreEngine.Hub.Services;
using Microsoft.Win32;

namespace KoreEngine.Hub;

public partial class NewProjectWindow : Window
{
    readonly string _engineDir;

    public RecentProjectEntry? CreatedEntry { get; private set; }

    public NewProjectWindow(string engineDir)
    {
        InitializeComponent();
        _engineDir = engineDir;
        LocationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    void Window_SourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowCornerHelper.EnableRoundedCorners(hwnd);
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choisis l'emplacement du projet" };
        if (dlg.ShowDialog() == true)
            LocationBox.Text = dlg.FolderName;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    void Create_Click(object sender, RoutedEventArgs e)
    {
        string projectName = ProjectNameBox.Text.Trim();
        string targetDir = LocationBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(projectName))
        {
            ShowError("Le nom du projet ne peut pas être vide.");
            return;
        }

        if (projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowError("Le nom du projet contient des caractères invalides.");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
        {
            ShowError("Sélectionne un emplacement valide.");
            return;
        }

        try
        {
            ProjectScaffolder.Create(_engineDir, targetDir, projectName);

            CreatedEntry = new RecentProjectEntry(
                projectName,
                Path.Combine(targetDir, projectName),
                DateTime.Now);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
