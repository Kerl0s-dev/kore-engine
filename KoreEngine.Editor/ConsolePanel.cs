using ImGuiNET;
using KoreEngine.Engine;
using System.Numerics;

namespace KoreEngine.Editor;

public class ConsolePanel
{
    bool showInfo = true;
    bool showWarning = true;
    bool showError = true;
    bool showSucess = true;
    bool autoScroll = true;
    bool scrollToBottom = false;

    string filter = "";

    static readonly Vector4 ColorInfo = new(0.85f, 0.85f, 0.85f, 1f);
    static readonly Vector4 ColorWarning = new(1f, 0.85f, 0f, 1f);
    static readonly Vector4 ColorError = new(1f, 0.3f, 0.3f, 1f);
    static readonly Vector4 ColorSucess = new(0f, 1f, 0f, 1f);

    public ConsolePanel()
    {
        Logger.OnLog += _ => { if (autoScroll) scrollToBottom = true; };
    }

    public void Draw()
    {
        ImGui.Begin("Console");

        // --- Barre d'outils ---
        if (ImGui.Button("Clear")) Logger.Clear();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorInfo);
        ImGui.Checkbox("Info##f", ref showInfo);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorWarning);
        ImGui.Checkbox("Warning##f", ref showWarning);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorError);
        ImGui.Checkbox("Error##f", ref showError);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorSucess);
        ImGui.Checkbox("Sucess##f", ref showSucess);
        ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.Checkbox("Auto-scroll", ref autoScroll);
        ImGui.SameLine();

        ImGui.SetNextItemWidth(150f);
        ImGui.InputText("##filter", ref filter, 128);

        ImGui.Separator();

        // --- Messages ---
        ImGui.BeginChild("##log", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        string filterLower = filter.Trim().ToLowerInvariant();

        lock (Logger.Entries)
        {
            for (int i = 0; i < Logger.Entries.Count; i++)
            {
                var entry = Logger.Entries[i];

                if (entry.Level == LogLevel.Info && !showInfo) continue;
                if (entry.Level == LogLevel.Warning && !showWarning) continue;
                if (entry.Level == LogLevel.Error && !showError) continue;
                if (entry.Level == LogLevel.Sucess && !showSucess) continue;

                if (filterLower.Length > 0 &&
                    !entry.Message.ToLowerInvariant().Contains(filterLower)) continue;

                Vector4 color = entry.Level switch
                {
                    LogLevel.Warning => ColorWarning,
                    LogLevel.Error => ColorError,
                    LogLevel.Sucess => ColorSucess,
                    _ => ColorInfo
                };

                string prefix = entry.Level switch
                {
                    LogLevel.Warning => "[WARNING]",
                    LogLevel.Error => "[ERROR] ",
                    LogLevel.Sucess => "[SUCESS]",
                    _ => "[INFO]"
                };

                string line = $"{entry.Time:HH:mm:ss}  {prefix}  {entry.Message}";

                ImGui.PushStyleColor(ImGuiCol.Text, color);

                if (entry.FilePath != null && entry.Line.HasValue)
                {
                    ImGui.Selectable($"{line}##log_{i}");

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Double-clic pour ouvrir dans Visual Studio\n{entry.FilePath}:{entry.Line}");
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            ExternalEditor.OpenFileAtLine(entry.FilePath, entry.Line.Value);
                    }
                }
                else
                {
                    ImGui.TextUnformatted(line);
                }

                ImGui.PopStyleColor();
            }
        }

        if (scrollToBottom && autoScroll)
        {
            ImGui.SetScrollHereY(1f);
            scrollToBottom = false;
        }

        ImGui.EndChild();
        ImGui.End();
    }
}