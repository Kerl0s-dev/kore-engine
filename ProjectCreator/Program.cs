using KoreEngine.ProjectCreator;
using System.Diagnostics;

if (args.Length < 3)
{
    Console.WriteLine("Usage: ProjectCreator <templateDir> <targetDir> <projectName>");
    return;
}

string templateDir = Path.GetFullPath(args[0]);
string targetDir = Path.GetFullPath(args[1]);
string projectName = args[2];

try
{
    ProjectTemplate.Create(templateDir, targetDir, projectName);

    string projectDir = Path.Combine(targetDir, projectName);
    string slnPath = Path.Combine(projectDir, $"{projectName}.sln");

    Console.WriteLine("[ProjectCreator] Build du projet...");
    var buildProc = Process.Start(new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build \"{slnPath}\"",
        WorkingDirectory = projectDir,
        UseShellExecute = false
    });
    buildProc!.WaitForExit();

    Console.WriteLine("[ProjectCreator] Ouverture du projet...");
    string exeDir = Path.Combine(projectDir, "bin", "Debug", "net10.0");
    string exePath = Path.Combine(exeDir, $"{projectName}.exe");

    Process.Start(new ProcessStartInfo
    {
        FileName = exePath,
        WorkingDirectory = exeDir,
        UseShellExecute = true
    });
}
catch (Exception e)
{
    Console.WriteLine($"Échec : {e.Message}");
}