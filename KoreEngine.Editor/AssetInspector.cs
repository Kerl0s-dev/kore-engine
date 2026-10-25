using ImGuiNET;
using SDL3;

namespace KoreEngine;

/// <summary>
/// Inspector d'un asset du Project Panel : un petit sous-inspecteur par type de fichier.
/// Les réglages sont stockés dans un .kmeta à côté de l'asset (voir AssetMeta).
/// </summary>
public static class AssetInspector
{
    // Textures
    static string? loadedPath;
    static TextureImportSettings? textureSettings;

    // Audio
    static string? loadedAudioPath;
    static AudioImportSettings? audioSettings;
    static IntPtr previewTrack, previewAudio;
    static bool previewActive;

    public static void Draw(string path)
    {
        // Fichier supprimé ou dossier renommé entre-temps : on désélectionne.
        if (!File.Exists(path))
        {
            StopPreview();
            EditorSelection.SelectedAssetPath = null;
            return;
        }

        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (!AudioImportSettings.SupportedExtensions.Contains(ext)) StopPreview();

        ImGui.TextUnformatted(Path.GetFileName(path));
        ImGui.TextDisabled($"{FormatSize(new FileInfo(path).Length)}  -  {path}");
        ImGui.Separator();

        bool readOnly = ProjectPanel.IsPathReadOnly(path);

        if (TextureCache.SupportedExtensions.Contains(ext))
            DrawTexture(path, readOnly);
        else if (AudioImportSettings.SupportedExtensions.Contains(ext))
            DrawAudio(path, readOnly);
        else
            ImGui.TextDisabled("Aucun réglage d'import pour ce type de fichier.");
    }

    // ---------------------------------------------------------------
    // Textures
    // ---------------------------------------------------------------

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
            ImGui.TextDisabled("Dossier en lecture seule : réglages non modifiables.");

        ImGui.Separator();

        // Aperçu : agrandi pour que l'effet du filtrage (pixels nets ou lissés) soit visible.
        IntPtr tex = TextureCache.Get(path);
        if (tex == IntPtr.Zero) { ImGui.TextDisabled("Impossible de charger la texture."); return; }

        SDL.GetTextureSize(tex, out float tw, out float th);
        ImGui.TextDisabled($"{(int)tw} x {(int)th}");

        float scale = MathF.Min(ImGui.GetContentRegionAvail().X / tw, 256f / th);
        ImGui.Image(tex, new System.Numerics.Vector2(tw * scale, th * scale));
    }

    // ---------------------------------------------------------------
    // Audio
    // ---------------------------------------------------------------

    static void DrawAudio(string path, bool readOnly)
    {
        if (loadedAudioPath != path)
        {
            audioSettings = AudioImportSettings.Load(path);
            loadedAudioPath = path;
        }
        var settings = audioSettings!;

        ImGui.TextDisabled("Audio Import Settings");

        ImGui.BeginDisabled(readOnly);
        foreach (var f in settings.GetInspectorFields(path))
            InspectorPanel.DrawFieldDescriptor(settings, f);
        ImGui.EndDisabled();

        if (readOnly)
            ImGui.TextDisabled("Dossier en lecture seule : réglages non modifiables.");

        ImGui.Separator();

        if (ImGui.Button("Preview")) Preview(path);
        ImGui.SameLine();
        if (ImGui.Button("Stop")) StopPreview();

        ImGui.Spacing();
        ImGui.TextDisabled(settings.LoadMode == AudioLoadMode.Streaming
            ? "Streaming : lu depuis le disque pendant la lecture (faible mémoire, adapté aux musiques)."
            : "En mémoire : décodé entièrement au chargement (latence minimale, adapté aux effets sonores).");
        ImGui.TextDisabled("Les réglages s'appliquent au chargement du clip (prochain Play ou prochaine scène).");
    }

    static void Preview(string path)
    {
        if (previewTrack == IntPtr.Zero) previewTrack = AudioManager.CreateTrack();
        else AudioManager.StopTrack(previewTrack);

        if (previewAudio != IntPtr.Zero) AudioManager.DestroyAudio(previewAudio);

        // Rechargé à chaque clic : l'aperçu reflète donc les réglages courants (mode et volume).
        previewAudio = AudioManager.LoadAudioAsset(path);
        if (previewAudio == IntPtr.Zero) return;

        AudioManager.PlayTrack(previewTrack, previewAudio);
        previewActive = true;
    }

    /// <summary>Public : InspectorPanel l'appelle aussi quand aucun asset n'est sélectionné.</summary>
    public static void StopPreview()
    {
        if (!previewActive) return;
        if (previewTrack != IntPtr.Zero) AudioManager.StopTrack(previewTrack);
        previewActive = false;
    }

    static string FormatSize(long b) =>
        b < 1024 ? $"{b} B" :
        b < 1024 * 1024 ? $"{b / 1024f:0.#} KB" :
        $"{b / (1024f * 1024f):0.##} MB";
}