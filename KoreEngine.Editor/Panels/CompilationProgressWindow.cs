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
            ImGui.OpenPopup("Compiling scripts");
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

        if (ImGui.BeginPopupModal("Compiling scripts", ref pOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar))
        {
            // Affichage du statut dynamique (ex: "Compilation en cours...")
            ImGui.Text(ScriptCompiler.Status);
            ImGui.Spacing();

            // Barre de progression animée (va-et-vient)
            float progress = (float)(Math.Sin(ImGui.GetTime() * 6.0) * 0.5 + 0.5);
            ImGui.ProgressBar(progress, new Vector2(-1, 0), "Compilation in progress...");

            ImGui.EndPopup();
        }
    }
}