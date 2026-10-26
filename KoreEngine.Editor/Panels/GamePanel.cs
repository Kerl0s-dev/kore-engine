using ImGuiNET;
using SDL3;

namespace KoreEngine;

public class GamePanel
{
    readonly Renderer renderer;
    readonly RenderTexture renderTexture;

    // (0,0) = "Free" : la texture suit la taille du panneau (comportement d'un jeu buildé redimensionnable).
    static readonly (string Name, int W, int H)[] Presets =
    {
        ("Free", 0, 0),
        ("2560x1440 (16:9)", 2560, 1440),
        ("1920x1080 (16:9)", 1920, 1080),
        ("1280x720 (16:9)", 1280, 720),
        ("1024x768 (4:3)", 1024, 768)
    };
    static readonly string[] PresetNames = Presets.Select(p => p.Name).ToArray();
    int presetIndex = 0;

    int pendingW, pendingH;

    public GamePanel(Renderer renderer, int width, int height)
    {
        this.renderer = renderer;
        renderTexture = new RenderTexture(renderer, width, height);
        pendingW = renderTexture.Width;
        pendingH = renderTexture.Height;
    }

    /// <summary>Début de frame, avant RenderScene (Resize est sans effet si la taille est identique).</summary>
    public void ApplyPendingResize() => renderTexture.Resize(pendingW, pendingH);

    /// <summary>
    /// L'UI du jeu (UICanvas.ScaleX/Y) lit SceneManager.ViewportWidth/Height :
    /// à appeler avant SceneManager.Update ET avant le rendu de ce panneau.
    /// </summary>
    const float LogicalH = 1080f;

    float LogicalScale => renderTexture.Height / LogicalH;           // pixels texture par unité logique
    int LogicalW => (int)MathF.Round(renderTexture.Width / LogicalScale);

    public void BindUiSize()
    {
        SceneManager.ViewportWidth = LogicalW;
        SceneManager.ViewportHeight = (int)LogicalH;
    }

    public void RenderScene(Scene? scene)
    {
        BindUiSize();
        renderTexture.BeginRender();

        if (scene != null)
        {
            var cam = scene.Camera;
            cam.ViewWidth = LogicalW;          // 1920 en 16:9, plus large/étroit sinon : pas de distorsion
            cam.ViewHeight = (int)LogicalH;

            float s = LogicalScale;
            SDL.SetRenderScale(renderer.Handle, s, s);

            renderer.Clear(cam.Color);
            scene.Render(renderer);

            renderer.Flush();                  // soumettre avec l'échelle active
            SDL.SetRenderScale(renderer.Handle, 1f, 1f);   // sinon ImGui est dessiné à la mauvaise échelle
        }

        renderTexture.EndRender();
    }

    public void Draw()
    {
        // Par défaut, aucun survol : couvre le cas où le panneau est replié/caché.
        SceneManager.ViewportMouseInBounds = false;

        if (!ImGui.Begin("Game")) { ImGui.End(); return; }

        ImGui.SetNextItemWidth(180f);
        ImGui.Combo("##resolution", ref presetIndex, PresetNames, PresetNames.Length);

        var avail = ImGui.GetContentRegionAvail();
        int availW = Math.Max(1, (int)avail.X);
        int availH = Math.Max(1, (int)avail.Y);

        var (_, pw, ph) = Presets[presetIndex];
        bool free = pw == 0;
        pendingW = free ? availW : pw;
        pendingH = free ? availH : ph;

        // Free : on remplit le panneau. Preset : on garde le ratio (bandes noires).
        float texW = renderTexture.Width, texH = renderTexture.Height;
        float scale = free ? 1f : MathF.Min(avail.X / texW, avail.Y / texH);

        var size = free
            ? new System.Numerics.Vector2(availW, availH)
            : new System.Numerics.Vector2(texW * scale, texH * scale);

        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new System.Numerics.Vector2(
            cursor.X + (avail.X - size.X) * 0.5f,
            cursor.Y + (avail.Y - size.Y) * 0.5f));

        var imagePos = ImGui.GetCursorScreenPos();
        ImGui.Image(renderTexture.Texture, size);

        // Souris en coordonnées "écran du jeu" (pixels de la texture), pas du panneau.
        var mouse = ImGui.GetIO().MousePos;
        SceneManager.ViewportMouseX = (mouse.X - imagePos.X) / size.X * SceneManager.ViewportWidth;
        SceneManager.ViewportMouseY = (mouse.Y - imagePos.Y) / size.Y * SceneManager.ViewportHeight;
        SceneManager.ViewportMouseInBounds = ImGui.IsItemHovered();

        ImGui.End();
    }

    public void Destroy() => renderTexture.Destroy();
}