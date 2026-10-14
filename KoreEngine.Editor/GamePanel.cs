using ImGuiNET;

namespace KoreEngine.Editor;

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
        ("1024x768 (4:3)", 1024, 768),
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
    public void BindUiSize()
    {
        SceneManager.ViewportWidth = renderTexture.Width;
        SceneManager.ViewportHeight = renderTexture.Height;
    }

    public void RenderScene(Scene? scene)
    {
        BindUiSize();
        renderTexture.BeginRender();

        if (scene != null)
        {
            var cam = scene.Camera;
            cam.ViewWidth = renderTexture.Width;
            cam.ViewHeight = renderTexture.Height;
            renderer.Clear(cam.Color);      // fond = ce que verra le joueur
            scene.Render(renderer);
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
        SceneManager.ViewportMouseX = (mouse.X - imagePos.X) / scale;
        SceneManager.ViewportMouseY = (mouse.Y - imagePos.Y) / scale;
        SceneManager.ViewportMouseInBounds = ImGui.IsItemHovered();

        ImGui.End();
    }

    public void Destroy() => renderTexture.Destroy();
}