using ImGuiNET;
using KoreEngine.UI;

namespace KoreEngine;

public class UIEditorPanel
{
    readonly Renderer renderer;
    readonly RenderTexture renderTexture;
    public EditorCamera View { get; }

    int pendingW, pendingH;
    bool resizeRequested;
    bool needsFit = true;
    UICanvas? canvas;

    enum Handle { None, Body, TL, T, TR, R, BR, B, BL, L }
    Handle active = Handle.None;
    RectTransform? dragTarget;
    Vector2 dragStartMouse, dragStartMin, dragStartMax, dragStartPos;

    const float HandlePx = 8f;
    const float HitPx = 10f;

    public UIEditorPanel(Renderer renderer, int width, int height)
    {
        this.renderer = renderer;
        renderTexture = new RenderTexture(renderer, width, height);
        pendingW = renderTexture.Width;
        pendingH = renderTexture.Height;
        View = new EditorCamera(renderTexture.Width, renderTexture.Height);
    }

    // ---------------------------------------------------------------
    // Frame : resize puis rendu (hors bloc ImGui)
    // ---------------------------------------------------------------

    public void ApplyPendingResize()
    {
        if (!resizeRequested) return;
        renderTexture.Resize(pendingW, pendingH);
        View.ViewWidth = renderTexture.Width;
        View.ViewHeight = renderTexture.Height;
        resizeRequested = false;
        needsFit = true;   // recadre le canvas quand le panel change de taille
    }

    void ResolveCanvas(Scene? scene)
    {
        var fromSelection = UIContext.FindCanvas(EditorSelection.Selected);
        if (fromSelection != null)
        {
            if (fromSelection != canvas) { canvas = fromSelection; needsFit = true; }
            return;
        }

        // Rien de sélectionné côté UI : on garde le dernier canvas s'il existe encore.
        if (canvas != null && scene != null && scene.AllObjects.Contains(canvas.gameObject)) return;

        canvas = scene?.AllObjects.Select(o => o.GetComponent<UICanvas>()).FirstOrDefault(c => c != null);
        needsFit = true;
    }

    void FitToCanvas()
    {
        var r = canvas!.RectTransform.Rect;
        View.Position = new Vector2((r.Min.X + r.Max.X) * 0.5f, (r.Min.Y + r.Max.Y) * 0.5f);
        View.Zoom = MathF.Min(View.ViewWidth / (r.Width * 1.15f), View.ViewHeight / (r.Height * 1.15f));
    }

    public void RenderScene(Scene? scene)
    {
        ResolveCanvas(scene);

        renderTexture.BeginRender();
        renderer.Clear(new Color(36, 36, 36));

        if (canvas != null)
        {
            canvas.SyncSize();
            if (needsFit) { FitToCanvas(); needsFit = false; }

            var r = canvas.RectTransform.Rect;
            int x = (int)r.Min.X, y = (int)r.Max.Y, w = (int)r.Width, h = (int)r.Height;

            renderer.DrawRect(x, y, w, h, scene?.Camera?.Color ?? Color.Black, 255, View);
            UIContext.RenderCanvas(canvas, renderer, View);
            renderer.DrawRectOutline(x, y, w, h, 255, 255, 255, 120, View);
        }

        renderTexture.EndRender();
    }

    // ---------------------------------------------------------------
    // ImGui (dans le bloc ImGui, après RenderScene)
    // ---------------------------------------------------------------

    public void Draw()
    {
        if (!ImGui.Begin("UI Editor")) { ImGui.End(); return; }

        var avail = ImGui.GetContentRegionAvail();
        int w = (int)avail.X, h = (int)avail.Y;
        if (w > 0 && h > 0 && (w != pendingW || h != pendingH))
        {
            pendingW = w; pendingH = h; resizeRequested = true;
        }

        var imagePos = ImGui.GetCursorScreenPos();
        ImGui.Image(renderTexture.Texture, avail);
        bool hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();

        if (hovered)  UpdateCamera(io, new Vector2(io.MousePos.X - imagePos.X, io.MousePos.Y - imagePos.Y));

        if (canvas == null)
            ImGui.GetWindowDrawList().AddText(imagePos + new System.Numerics.Vector2(12, 12),
                ImGui.GetColorU32(new System.Numerics.Vector4(1, 1, 1, 0.6f)), "No UICanvas selected in the hierarchy");
        else
            DrawRectTool(imagePos, avail, io, hovered);

        ImGui.End();
    }

    void UpdateCamera(ImGuiIOPtr io, Vector2 mouseLocal)
    {
        if (io.MouseDown[2])   // pan : clic molette
            View.Position = new Vector2(View.Position.X - io.MouseDelta.X / View.Zoom,
                                        View.Position.Y + io.MouseDelta.Y / View.Zoom);

        if (io.MouseWheel != 0f)   // zoom centré sur la souris
        {
            var before = View.ScreenToWorld(mouseLocal);
            View.Zoom = Math.Clamp(View.Zoom * (1f + 0.1f * io.MouseWheel), 0.05f, 20f);
            var after = View.ScreenToWorld(mouseLocal);
            View.Position = new Vector2(View.Position.X + (before.X - after.X),
                                        View.Position.Y + (before.Y - after.Y));
        }
    }

    // ---------------------------------------------------------------
    // Outil rectangle : sélection, déplacement, 8 poignées
    // ---------------------------------------------------------------

    System.Numerics.Vector2 ToScreen(Vector2 world, System.Numerics.Vector2 imagePos)
    {
        var l = View.WorldToScreen(world);
        return new System.Numerics.Vector2(imagePos.X + l.X, imagePos.Y + l.Y);
    }

    Vector2 ToWorld(System.Numerics.Vector2 abs, System.Numerics.Vector2 imagePos)
        => View.ScreenToWorld(new Vector2(abs.X - imagePos.X, abs.Y - imagePos.Y));

    static bool Near(System.Numerics.Vector2 a, System.Numerics.Vector2 b, float d) => (a - b).Length() <= d;
    static bool LeftEdge(Handle h) => h is Handle.TL or Handle.L or Handle.BL;
    static bool RightEdge(Handle h) => h is Handle.TR or Handle.R or Handle.BR;
    static bool TopEdge(Handle h) => h is Handle.TL or Handle.T or Handle.TR;
    static bool BottomEdge(Handle h) => h is Handle.BL or Handle.B or Handle.BR;

    GameObject? Pick(Vector2 p)
    {
        GameObject? hit = null;   // dernier dessiné = au-dessus
        void Walk(GameObject o)
        {
            foreach (var c in o.Children)
            {
                if (c.transform is RectTransform rt && c.GetComponent<UIElement>() is { Visible: true })
                {
                    var r = rt.Rect;
                    if (p.X >= r.Min.X && p.X <= r.Max.X && p.Y >= r.Min.Y && p.Y <= r.Max.Y) hit = c;
                }
                Walk(c);
            }
        }
        Walk(canvas!.gameObject);
        return hit;
    }

    void BeginDrag(Handle handle, RectTransform rt, Vector2 mouseWorld)
    {
        var r = rt.Rect;
        active = handle;
        dragTarget = rt;
        dragStartMouse = mouseWorld;
        dragStartMin = r.Min;
        dragStartMax = r.Max;
        dragStartPos = rt.Position;
    }

    void DrawRectTool(System.Numerics.Vector2 imagePos, System.Numerics.Vector2 size, ImGuiIOPtr io, bool hovered)
    {
        var sel = EditorSelection.Selected;
        var rt = sel?.transform as RectTransform;
        bool selectable = sel != null && rt != null && sel.GetComponent<UICanvas>() == null;

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(imagePos, imagePos + size, true);
        uint accent = ImGui.GetColorU32(new System.Numerics.Vector4(0.26f, 0.59f, 0.98f, 1f));

        // Positions des poignées (écran)
        var handles = new (Handle id, System.Numerics.Vector2 pos)[0];
        System.Numerics.Vector2 tl = default, br = default;
        if (selectable)
        {
            var r = rt!.Rect;
            tl = ToScreen(new Vector2(r.Min.X, r.Max.Y), imagePos);
            br = ToScreen(new Vector2(r.Max.X, r.Min.Y), imagePos);
            var m = (tl + br) * 0.5f;

            dl.AddRect(tl, br, accent, 0f, ImDrawFlags.None, 1.5f);
            handles = new[]
            {
                (Handle.TL, tl), (Handle.T, new System.Numerics.Vector2(m.X, tl.Y)), (Handle.TR, new System.Numerics.Vector2(br.X, tl.Y)),
                (Handle.R, new System.Numerics.Vector2(br.X, m.Y)), (Handle.BR, br),
                (Handle.B, new System.Numerics.Vector2(m.X, br.Y)), (Handle.BL, new System.Numerics.Vector2(tl.X, br.Y)),
                (Handle.L, new System.Numerics.Vector2(tl.X, m.Y)),
            };
            foreach (var (_, p) in handles)
            {
                var h = new System.Numerics.Vector2(HandlePx * 0.5f, HandlePx * 0.5f);
                dl.AddRectFilled(p - h, p + h, ImGui.GetColorU32(new System.Numerics.Vector4(1, 1, 1, 1)));
                dl.AddRect(p - h, p + h, accent);
            }
        }
        else if (active != Handle.None && dragTarget == null) active = Handle.None;

        // --- Début d'interaction ---
        if (hovered && active == Handle.None && io.MouseClicked[0])
        {
            var mouseWorld = ToWorld(io.MousePos, imagePos);
            Handle clicked = Handle.None;

            foreach (var (id, p) in handles)
                if (Near(io.MousePos, p, HitPx)) { clicked = id; break; }

            if (clicked != Handle.None)
                BeginDrag(clicked, rt!, mouseWorld);
            else
            {
                var hit = Pick(mouseWorld);
                EditorSelection.Selected = hit;   // null = clic dans le vide → désélection
                if (hit?.transform is RectTransform hitRt)
                    BeginDrag(Handle.Body, hitRt, mouseWorld);
            }
        }

        // --- Drag en cours ---
        if (active != Handle.None && dragTarget != null)
        {
            if (!io.MouseDown[0]) { active = Handle.None; dragTarget = null; }
            else
            {
                var d = ToWorld(io.MousePos, imagePos);
                float dx = d.X - dragStartMouse.X, dy = d.Y - dragStartMouse.Y;

                if (active == Handle.Body)
                    dragTarget.Position = new Vector2(MathF.Round(dragStartPos.X + dx), MathF.Round(dragStartPos.Y + dy));
                else
                {
                    float minX = dragStartMin.X, minY = dragStartMin.Y, maxX = dragStartMax.X, maxY = dragStartMax.Y;
                    if (LeftEdge(active)) minX = MathF.Min(MathF.Round(minX + dx), maxX - 1f);
                    if (RightEdge(active)) maxX = MathF.Max(MathF.Round(maxX + dx), minX + 1f);
                    if (BottomEdge(active)) minY = MathF.Min(MathF.Round(minY + dy), maxY - 1f);
                    if (TopEdge(active)) maxY = MathF.Max(MathF.Round(maxY + dy), minY + 1f);
                    dragTarget.SetWorldRect(new Vector2(minX, minY), new Vector2(maxX, maxY));
                }
            }
        }

        dl.PopClipRect();
    }

    public void Destroy() => renderTexture.Destroy();
}