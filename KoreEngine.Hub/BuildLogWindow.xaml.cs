using System.Windows;
using System.Windows.Interop;
using KoreEngine.Hub.Services;

namespace KoreEngine.Hub;

public partial class BuildLogWindow : Window
{
    public BuildLogWindow(string projectName, string? titleOverride = null)
    {
        InitializeComponent();
        Title = titleOverride ?? $"Build de {projectName}...";
        TitleBarText.Text = Title;
    }

    void Window_SourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowCornerHelper.EnableRoundedCorners(hwnd);
    }

    public void AppendLine(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogScroll.ScrollToBottom();
    }

    public void SetFinished(bool success, string? successMessage = null)
    {
        StatusText.Text = success ? (successMessage ?? "Build réussi — lancement du projet.") : "Échec du build.";
        StatusText.Foreground = success
            ? (System.Windows.Media.Brush)FindResource("TextBrush")
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x55, 0x55));
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
