using ImGuiNET;
using System.Numerics;

public static class CompilationProgressWindow
{
    private static bool _wasCompilingLastFrame = false;

    public static void Draw()
    {
        bool isCompiling = ScriptCompiler.IsCompiling;

        // Détecte le moment exact où la compilation démarre pour ouvrir la popup une seule fois
        if (isCompiling && !_wasCompilingLastFrame)
        {
            ImGui.OpenPopup("##compilationWindow");
        }
        _wasCompilingLastFrame = isCompiling;

        // Si on ne compile plus, on s'arrête net (la popup se fermera toute seule si elle était ouverte)
        if (!isCompiling)
            return;

        // Centrer la fenêtre par rapport à la vue principale
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(350, 110), ImGuiCond.Appearing);

        // Utilisation d'une variable booléenne pour la modale
        bool pOpen = true;

        if (ImGui.BeginPopupModal("##compilationWindow", ref pOpen, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar))
        {
            // Affichage du statut dynamique (ex: "Compilation en cours...")
            ImGui.Text("Compiling scripts...");
            ImGui.Text("Please wait while the scripts are being compiled.");
            ImGui.EndPopup();
        }
    }
}