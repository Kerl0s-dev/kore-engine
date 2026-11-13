using ImGuiNET;
using KoreEngine.UI;

namespace KoreEngine;

public class UIEditorPanel
{
    readonly Renderer renderer;
    readonly RenderTexture renderTexture;
    public EditorCamera View { get; }

    private int pendingWidth;
    private int pendingHeight;
    bool resizeRequested;
    UICanvas? canvas;

    enum Handle { None, Body, TL, T, TR, R, BR, B, BL, L }
    Handle active = Handle.None;
    RectTransform? dragTarget;
    Vector2 dragStartMouse, dragStartMin, dragStartMax, dragStartPos;

    public bool SnapEnabled = true;
    const float SnapPx = 0.1f;

    readonly List<RectF> snapTargets = new();
    readonly List<(bool vertical, float pos)> guides = new();

    const float HandlePx = 8f;
    const float HitPx = 10f;

    public UIEditorPanel(Renderer renderer, int width, int height)
    {
        this.renderer = renderer;
        renderTexture = new RenderTexture(renderer, width, height);
        pendingWidth = renderTexture.Width;
        pendingHeight = renderTexture.Height;
        View = new EditorCamera(renderTexture.Width, renderTexture.Height);

        cameraTargetZoom = View.Zoom;
        cameraTargetPosition = View.Position;
    }

    // ---------------------------------------------------------------
    // Frame : resize puis rendu (hors bloc ImGui)
    // ---------------------------------------------------------------

    public void ApplyPendingResize(Camera? camera)
    {
        if (resizeRequested)
        {
            renderTexture.Resize(pendingWidth, pendingHeight);
            View.ViewWidth = renderTexture.Width;
            View.ViewHeight = renderTexture.Height;
            resizeRequested = false;
        }

        // Synchronise toujours la caméra active.
        if (camera != null)
        {
            camera.ViewWidth = renderTexture.Width;
            camera.ViewHeight = renderTexture.Height;
        }

        SceneManager.ViewportWidth = renderTexture.Width;
        SceneManager.ViewportHeight = renderTexture.Height;
    }

    void ResolveCanvas(Scene? scene)
    {
        var fromSelection = UIContext.FindCanvas(EditorSelection.Selected);
        if (fromSelection != null)
        {
            if (fromSelection != canvas) { canvas = fromSelection; }
            return;
        }

        // Rien de sélectionné côté UI : on garde le dernier canvas s'il existe encore.
        if (canvas != null && scene != null && scene.AllObjects.Contains(canvas.gameObject)) return;

        canvas = scene?.AllObjects.Select(o => o.GetComponent<UICanvas>()).FirstOrDefault(c => c != null);
    }

    public void RenderScene(Scene? scene)
    {
        renderTexture.BeginRender();
        renderer.Clear(new Color(36, 36, 36));

        if (canvas != null)
        {
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

    public void Draw() // Pass the scene into Draw so we can resolve canvas here if needed
    {
        if (!ImGui.Begin("UI Editor")) { ImGui.End(); return; }

        ImGui.Checkbox("Snap", ref SnapEnabled);
        ImGui.SameLine();
        ImGui.TextDisabled("(Ctrl : désactiver)");

        System.Numerics.Vector2 avail = ImGui.GetContentRegionAvail();
        int availW = (int)avail.X;
        int availH = (int)avail.Y;

        if (availW > 0 && availH > 0 && (availW != pendingWidth || availH != pendingHeight))
        {
            pendingWidth = availW;
            pendingHeight = availH;
            resizeRequested = true;
        }
        
        ResolveCanvas(SceneManager.Current!);

        // Position écran du coin haut-gauche de l'image (avant ImGui.Image).
        System.Numerics.Vector2 imageScreenPos = ImGui.GetCursorScreenPos();

        ImGui.Image(renderTexture.Texture, avail);

        var io = ImGui.GetIO();
        bool overViewport = ImGui.IsItemHovered();
        System.Numerics.Vector2 mouseScreen = io.MousePos;

        // Souris en coordonnées locales au viewport (texture).
        var localMouse = new Vector2(mouseScreen.X - imageScreenPos.X, mouseScreen.Y - imageScreenPos.Y);

        // Contrôles caméra éditeur — uniquement en mode édition et quand
        // la souris est au-dessus du viewport.
        if (overViewport) HandleCameraInput(io, localMouse);
        StepEditorCamera(Time.deltaTime);

        if (canvas == null)
            ImGui.GetWindowDrawList().AddText(imageScreenPos + new System.Numerics.Vector2(12, 12),
                ImGui.GetColorU32(new System.Numerics.Vector4(1, 1, 1, 0.6f)), "No UICanvas selected in the hierarchy");
        else
            DrawRectTool(imageScreenPos, avail, io, overViewport);

        ImGui.End();
    }

    // ---------------------------------------------------------------
    // Caméra éditeur
    // ---------------------------------------------------------------
    private float cameraTargetZoom;
    private Vector2 cameraTargetPosition;
    private Vector2 zoomAnchor;   // point sous lequel on zoome (coordonnées viewport)

    // Vitesse de zoom (facteur multiplicatif par cran de molette).
    const float ZoomStep = 0.1f;
    const float ZoomMin = 0.0005f;
    const float ZoomMax = 20f;

    const float ZoomSmoothing = 0.25f;
    const float PanSmoothing = 0.25f;
    static float Damp(float smoothing, float dt) => 1f - MathF.Exp(-smoothing * dt);

    /// <summary>Recentre la caméra éditeur sur un point du monde (touche F, « Frame Selected »...).</summary>
    public void FocusOn(Vector2 worldPos) => cameraTargetPosition = worldPos;

    // Entrées : seulement quand la souris survole le viewport
    const float ScrollPanPixels = 80f;   // distance à l'écran par cran de molette

    void HandleCameraInput(ImGuiIOPtr io, Vector2 mouseViewport)
    {
        if (io.MouseDown[2])
        {
            var delta = new Vector2(-io.MouseDelta.X, io.MouseDelta.Y) / View.Zoom / Renderer.PixelsPerUnit;
            View.Position += delta;
            cameraTargetPosition += delta;
        }

        if (io.MouseWheel == 0f && io.MouseWheelH == 0f) return;

        if (io.KeyCtrl)
        {
            float factor = MathF.Pow(1f + ZoomStep, io.MouseWheel);
            cameraTargetZoom = System.Math.Clamp(cameraTargetZoom * factor, ZoomMin, ZoomMax);
            zoomAnchor = mouseViewport;
        }
        else
        {
            // Pas constant à l'écran, converti en mètres
            float step = ScrollPanPixels / (View.Zoom * Renderer.PixelsPerUnit);

            float dx = io.MouseWheelH;      // molette horizontale ou pavé tactile
            float dy = io.MouseWheel;
            if (io.KeyShift) { dx -= dy; dy = 0f; }   // Shift + molette : défilement horizontal

            // On déplace la cible : StepEditorCamera lisse le mouvement
            cameraTargetPosition += new Vector2(dx * step, dy * step);
        }
    }

    // Animation : à CHAQUE frame en mode édition, même hors du viewport
    void StepEditorCamera(float dt)
    {
        float diff = cameraTargetZoom - View.Zoom;
        if (MathF.Abs(diff) > cameraTargetZoom * 0.0005f)
        {
            Vector2 worldBefore = View.ScreenToWorld(zoomAnchor);
            View.Zoom += diff * Damp(ZoomSmoothing * 1000, dt);
            Vector2 worldAfter = View.ScreenToWorld(zoomAnchor);

            Vector2 correction = worldBefore - worldAfter;
            View.Position += correction;
            cameraTargetPosition += correction;
        }
        else View.Zoom = cameraTargetZoom;

        View.Position = Vector2.Lerp(View.Position, cameraTargetPosition, Damp(PanSmoothing * 1000, dt));
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
            foreach (var c in o.children)
            {
                if (c.ActiveInHierarchy && c.transform is RectTransform rt && c.GetComponent<UIElement>() is { Visible: true })
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

        GatherSnapTargets(rt);
        guides.Clear();
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
            if (!io.MouseDown[0]) { active = Handle.None; dragTarget = null; guides.Clear(); }
            else
            {
                var d = ToWorld(io.MousePos, imagePos);
                float dx = d.X - dragStartMouse.X, dy = d.Y - dragStartMouse.Y;

                bool snap = SnapEnabled && !io.KeyCtrl;
                float thr = SnapPx / View.Zoom;   // 6 px écran, converti en unités canvas
                guides.Clear();

                if (active == Handle.Body)
                {
                    float w = dragStartMax.X - dragStartMin.X, h = dragStartMax.Y - dragStartMin.Y;
                    float minX = dragStartMin.X + dx, minY = dragStartMin.Y + dy;
                    float ndx = dx, ndy = dy;
                    bool snappedX = false, snappedY = false;

                    if (snap)
                    {
                        if (SnapAxis(new[] { minX, minX + w * 0.5f, minX + w }, true, thr, out var ox, out var tx))
                        { ndx += ox; snappedX = true; guides.Add((true, tx)); }

                        if (SnapAxis(new[] { minY, minY + h * 0.5f, minY + h }, false, thr, out var oy, out var ty))
                        { ndy += oy; snappedY = true; guides.Add((false, ty)); }
                    }

                    float px = dragStartPos.X + ndx, py = dragStartPos.Y + ndy;
                    // Quand on est aimanté, on ne re-arrondit pas : sinon un centre à .5 décalerait l'élément du guide.
                    dragTarget.Position = new Vector2(snappedX ? px : MathF.Round(px), snappedY ? py : MathF.Round(py));
                }
                else
                {
                    float minX = dragStartMin.X, minY = dragStartMin.Y, maxX = dragStartMax.X, maxY = dragStartMax.Y;

                    if (LeftEdge(active)) minX = MathF.Min(SnapOrRound(minX + dx, true, snap, thr), maxX - 1f);
                    if (RightEdge(active)) maxX = MathF.Max(SnapOrRound(maxX + dx, true, snap, thr), minX + 1f);
                    if (BottomEdge(active)) minY = MathF.Min(SnapOrRound(minY + dy, false, snap, thr), maxY - 1f);
                    if (TopEdge(active)) maxY = MathF.Max(SnapOrRound(maxY + dy, false, snap, thr), minY + 1f);

                    dragTarget.SetWorldRect(new Vector2(minX, minY), new Vector2(maxX, maxY));
                }
            }
        }

        if (active != Handle.None && guides.Count > 0)
        {
            var cr = canvas!.RectTransform.Rect;
            uint guideColor = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.25f, 0.85f, 1f));

            foreach (var (vertical, pos) in guides)
            {
                var a = vertical ? ToScreen(new Vector2(pos, cr.Max.Y), imagePos) : ToScreen(new Vector2(cr.Min.X, pos), imagePos);
                var b = vertical ? ToScreen(new Vector2(pos, cr.Min.Y), imagePos) : ToScreen(new Vector2(cr.Max.X, pos), imagePos);
                dl.AddLine(a, b, guideColor, 1f);
            }
        }

        dl.PopClipRect();
    }

    /// Cibles : le canvas + tous les éléments visibles, sauf l'élément déplacé et ses descendants
    /// (ils bougent avec lui).
    void GatherSnapTargets(RectTransform exclude)
    {
        snapTargets.Clear();
        snapTargets.Add(canvas!.RectTransform.Rect);

        void Walk(GameObject o)
        {
            foreach (var c in o.children)
            {
                bool inDragged = c == exclude.gameObject || c.transform.IsDescendantOf(exclude.gameObject);
                if (!inDragged && c.ActiveInHierarchy && c.transform is RectTransform rt && c.GetComponent<UIElement>() is { Visible: true })
                    snapTargets.Add(rt.Rect);
                Walk(c);
            }
        }
        Walk(canvas.gameObject);
    }

    /// Cherche le meilleur alignement entre les valeurs mobiles (bords/centre) et les cibles
    /// (bords/centre) sur un axe. offset = décalage à appliquer, target = position du guide.
    bool SnapAxis(float[] moving, bool xAxis, float threshold, out float offset, out float target)
    {
        offset = 0f; target = 0f;
        float best = threshold;
        bool found = false;

        foreach (var r in snapTargets)
        {
            float lo = xAxis ? r.Min.X : r.Min.Y;
            float hi = xAxis ? r.Max.X : r.Max.Y;

            for (int k = 0; k < 3; k++)
            {
                float t = k == 0 ? lo : k == 1 ? (lo + hi) * 0.5f : hi;
                foreach (var m in moving)
                {
                    float d = t - m;
                    if (MathF.Abs(d) <= best) { best = MathF.Abs(d); offset = d; target = t; found = true; }
                }
            }
        }
        return found;
    }

    /// Pour un bord déplacé : s'aimante si possible (et enregistre le guide), sinon arrondit au pixel.
    float SnapOrRound(float v, bool xAxis, bool snap, float threshold)
    {
        if (snap && SnapAxis(new[] { v }, xAxis, threshold, out _, out var t))
        {
            guides.Add((xAxis, t));
            return t;
        }
        return MathF.Round(v);
    }

    public void Destroy() => renderTexture.Destroy();
}