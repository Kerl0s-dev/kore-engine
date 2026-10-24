using ImGuiNET;
using SDL3;

namespace KoreEngine;

/// Inspector d'un asset du Project Panel : un petit sous-inspecteur par type de fichier.
public static class AssetInspector
{
    static string? loadedPath;
    static TextureImportSettings? textureSettings;

    public static void Draw(string path)
    {
        // Fichier supprimé ou dossier renommé entre-temps : on désélectionne.
        if (!File.Exists(path)) { EditorSelection.SelectedAssetPath = null; return; }

        ImGui.TextUnformatted(Path.GetFileName(path));
        ImGui.TextDisabled($"{FormatSize(new FileInfo(path).Length)}  -  {path}");
        ImGui.Separator();

        string ext = Path.GetExtension(path).ToLowerInvariant();
        bool readOnly = ProjectPanel.IsPathReadOnly(path);

        if (TextureCache.SupportedExtensions.Contains(ext))
            DrawTexture(path, readOnly);
        else
            ImGui.TextDisabled("Aucun réglage d'import pour ce type de fichier.");
    }

    static void DrawTexture(string path, bool readOnly)
    {
        if (loadedPath != path)
        {
            textureSettings = TextureImportSettings.Load(path);
            loadedPath = path;
        }
        var settings = textureSettings!;

        ImGui.TextDisabled("Texture Import Settings");

        ImGui.BeginDisabled(readOnly);
        foreach (var f in settings.GetInspectorFields(path))
            InspectorPanel.DrawFieldDescriptor(settings, f);
        ImGui.EndDisabled();

        if (readOnly)
            ImGui.TextDisabled("Directory in read only mode : settings not editable.");

        ImGui.Separator();

        // Aperçu : agrandi pour que l'effet du filtrage (pixels nets ou lissés) soit visible.
        IntPtr tex = TextureCache.Get(path);
        if (tex == IntPtr.Zero) { ImGui.TextDisabled("No texture to display."); return; }

        SDL.GetTextureSize(tex, out float tw, out float th);
        ImGui.TextDisabled($"{(int)tw} x {(int)th}");

        float scale = MathF.Min(ImGui.GetContentRegionAvail().X / tw, 256f / th);
        ImGui.Image(tex, new System.Numerics.Vector2(tw * scale, th * scale));
    }

    static string FormatSize(long b) =>
        b < 1024 ? $"{b} B" :
        b < 1024 * 1024 ? $"{b / 1024f:0.#} KB" :
        $"{b / (1024f * 1024f):0.##} MB";
}