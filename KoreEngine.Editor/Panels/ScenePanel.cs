using ImGuiNET;
using KoreEngine.UI;

namespace KoreEngine;

public class ScenePanel
{
    private readonly Renderer renderer;
    private readonly RenderTexture renderTexture;

    private int pendingWidth;
    private int pendingHeight;
    private bool resizeRequested;

    // Caméra éditeur — indépendante de Scene.Camera (caméra de jeu).
    // EditorWindow passe EditorCamera à RenderScene quand Playing == false,
    // et scene.Camera quand Playing == true.
    public EditorCamera EditorCamera { get; private set; }

    // Vitesse de zoom (facteur multiplicatif par cran de molette).
    const float ZoomStep = 0.1f;
    const float ZoomMin = 0.05f;
    const float ZoomMax = 20f;

    enum DragMode { None, MoveX, MoveY, MoveFree, ScaleX, ScaleY, ScaleFree, Rotate }

    DragMode activeDrag = DragMode.None;
    Vector2 dragStartWorldMouse;
    Vector2 dragStartPosition;
    Vector2 dragStartScale;
    float dragStartRotationValue;
    float dragStartMouseAngle;

    const float ArmLength = 50f;   // en unités monde — l'écran suit automatiquement via WorldToScreen (zoom pris en compte)
    const float HandleSize = 14f;   // idem, en unités monde
    const float HitPadding = 10f;   // marge de tolérance de clic, en pixels écran

    public ScenePanel(Renderer renderer, RenderTexture renderTexture)
    {
        this.renderer = renderer;
        this.renderTexture = renderTexture;
        pendingWidth = renderTexture.Width;
        pendingHeight = renderTexture.Height;

        EditorCamera = new EditorCamera(renderTexture.Width, renderTexture.Height);

        cameraTargetZoom = EditorCamera.Zoom;
        cameraTargetPosition = EditorCamera.Position;
    }

    // ---------------------------------------------------------------
    // Resize
    // ---------------------------------------------------------------

    /// <summary>
    /// À appeler en tout DÉBUT de frame, AVANT RenderScene().
    /// Applique la taille mesurée par Draw() la frame précédente,
    /// sur la RenderTexture ET sur la caméra fournie (jeu ou éditeur).
    /// </summary>
    public void ApplyPendingResize(Camera? camera)
    {
        if (resizeRequested)
        {
            renderTexture.Resize(pendingWidth, pendingHeight);
            EditorCamera.ViewWidth = renderTexture.Width;
            EditorCamera.ViewHeight = renderTexture.Height;
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

    // ---------------------------------------------------------------
    // Rendu
    // ---------------------------------------------------------------
    public void RenderScene(Scene? scene, Camera? camera, bool editorMode = false)
    {
        Camera? previous = null;
        if (scene != null && camera != null)
        {
            previous = scene.Camera;
            scene.Camera = camera;
        }

        renderTexture.BeginRender();
        UIContext.HideUI = true;
        try
        {
            scene?.Render(renderer);
            if (editorMode && camera != null)
                RenderColliderOverlay(camera);

            RenderGizmos();

            Gizmo.CurrentRenderer = renderer;
            Gizmo.CurrentCamera = camera;
        }
        finally { UIContext.HideUI = false; }   // le Game panel et l'UI Editor rendent juste après
        renderTexture.EndRender();

        if (scene != null && previous != null)
            scene.Camera = previous;
    }

    void WorldArc(Camera cam, Vector2 c, float radius, float start, float sweep, byte r, byte g, byte b, byte a)
    {
        int n = Math.Max(8, (int)(MathF.Abs(sweep) / (2f * MathF.PI) * 32f));
        var prev = new Vector2(c.X + MathF.Cos(start) * radius, c.Y + MathF.Sin(start) * radius);
        for (int i = 1; i <= n; i++)
        {
            float ang = start + sweep * i / n;
            var cur = new Vector2(c.X + MathF.Cos(ang) * radius, c.Y + MathF.Sin(ang) * radius);
            renderer.WorldLine(cam, prev, cur, r, g, b, a);
            prev = cur;
        }
    }

    void RenderColliderOverlay(Camera camera)
    {
        var selected = EditorSelection.Selected;
        if (selected == null) return;

        var collider = selected.GetComponent<Collider>();
        if (collider == null) return;

        byte r = 0, g = 255, b = 0;
        var s = collider.GetWorldShape();

        if (s.Type == ColliderShapeType.Box)
        {
            var ex = s.AxisX * s.HalfExtents.X;
            var ey = s.AxisY * s.HalfExtents.Y;
            var p0 = s.Center - ex + ey; var p1 = s.Center + ex + ey;
            var p2 = s.Center + ex - ey; var p3 = s.Center - ex - ey;
            renderer.WorldLine(camera, p0, p1, r, g, b, 255); renderer.WorldLine(camera, p1, p2, r, g, b, 255);
            renderer.WorldLine(camera, p2, p3, r, g, b, 255); renderer.WorldLine(camera, p3, p0, r, g, b, 255);
        }
        else
        {
            var d = s.SegB - s.SegA;
            if (d.LengthSquared() < 1e-8f)
            {
                WorldArc(camera, s.SegA, s.Radius, 0f, 2f * MathF.PI, r, g, b, 255);
            }
            else
            {
                var u = d.Normalize();
                var v = new KoreEngine.Vector2(-u.Y, u.X);
                float th = MathF.Atan2(u.Y, u.X);
                renderer.WorldLine(camera, s.SegA + v * s.Radius, s.SegB + v * s.Radius, r, g, b, 255);
                renderer.WorldLine(camera, s.SegA - v * s.Radius, s.SegB - v * s.Radius, r, g, b, 255);
                WorldArc(camera, s.SegB, s.Radius, th + MathF.PI / 2f, -MathF.PI, r, g, b, 255);
                WorldArc(camera, s.SegA, s.Radius, th - MathF.PI / 2f, -MathF.PI, r, g, b, 255);
            }
        }

        // AABB englobant (phase large / CheckGrounded), en transparence si la forme n'y est pas confondue
        if (!s.IsAxisAlignedBox)
        {
            var bb = s.Bounds;
            renderer.DrawRectOutline(bb.X, bb.Y + bb.Height, bb.Width, bb.Height, r, g, b, 90, camera);
        }
    }

    void RenderGizmos()
    {
        // Sécurité si la scène ou la liste est null
        var activeObjects = SceneManager.Current?.ActiveObjects;
        if (activeObjects == null) return;

        GameObject[] gameObjects = activeObjects.ToArray();
        if (gameObjects.Length == 0) return;

        foreach (var gameObject in gameObjects)
        {
            if (gameObject == null) continue;

            gameObject.DispatchGizmo();
        }
    }

    // ---------------------------------------------------------------
    // Gizmo caméra : cadre de ce que voit chaque Camera de la scène
    // ---------------------------------------------------------------

    static uint Col(float r, float g, float b, float a) =>
        ImGui.GetColorU32(new System.Numerics.Vector4(r, g, b, a));

    void DrawCameraGizmos(System.Numerics.Vector2 imageScreenPos, System.Numerics.Vector2 imageSize)
    {
        var scene = SceneManager.Current;
        if (scene == null) return;

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(imageScreenPos, imageScreenPos + imageSize, true);

        foreach (var obj in scene.AllObjects)
        {
            var cam = obj.GetComponent<Camera>();
            if (cam == null || cam.ViewWidth <= 0 || cam.ViewHeight <= 0) continue;

            bool selected = obj == EditorSelection.Selected;
            uint color = selected ? Col(1f, 1f, 1f, 1f) : Col(0.75f, 0.75f, 0.75f, 0.6f);
            float thickness = selected ? 2f : 1.5f;

            // 1. Récupère les 4 coins de la caméra dans l'espace monde, puis vers l'écran de l'éditeur
            var pTL = WorldToAbsScreen(cam.ScreenToWorld(new Vector2(0, 0)), imageScreenPos);                         // Top-Left
            var pTR = WorldToAbsScreen(cam.ScreenToWorld(new Vector2(cam.ViewWidth, 0)), imageScreenPos);             // Top-Right
            var pBR = WorldToAbsScreen(cam.ScreenToWorld(new Vector2(cam.ViewWidth, cam.ViewHeight)), imageScreenPos); // Bottom-Right
            var pBL = WorldToAbsScreen(cam.ScreenToWorld(new Vector2(0, cam.ViewHeight)), imageScreenPos);             // Bottom-Left

            // 2. Dessine le rectangle orienté de la caméra
            dl.AddQuad(pTL, pTR, pBR, pBL, color, thickness);

            // 3. Position de l'icône / croix au centre de la caméra
            var c = WorldToAbsScreen(cam.Position, imageScreenPos);
            IntPtr icon = EditorIcons.GetExact("camera");

            if (icon != IntPtr.Zero)
            {
                const float iconSize = 24f;   // taille écran fixe, indépendante du zoom
                float h = iconSize * 0.5f;
                dl.AddImage(icon,
                    new System.Numerics.Vector2(c.X - h, c.Y - h),
                    new System.Numerics.Vector2(c.X + h, c.Y + h),
                    new System.Numerics.Vector2(0, 0),
                    new System.Numerics.Vector2(1, 1),
                    color);
            }
            else
            {
                // Repli si camera.png est absent : croix
                dl.AddLine(new(c.X - 6, c.Y), new(c.X + 6, c.Y), color, 1.5f);
                dl.AddLine(new(c.X, c.Y - 6), new(c.X, c.Y + 6), color, 1.5f);
            }
        }

        dl.PopClipRect();
    }

    // ---------------------------------------------------------------
    // Sélection par clic dans le viewport
    // ---------------------------------------------------------------

    Rectangle? GetClickBounds(GameObject obj)
    {
        if (obj.GetComponent<UIElement>() != null) return null;
        if (!obj.ActiveInHierarchy) return null;

        var collider = obj.GetComponent<Collider>();
        if (collider != null) return collider.Bounds;

        var sprite = obj.GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            var scale = obj.transform.WorldScale;
            float w = scale.X;
            float h = scale.Y;
            var pos = obj.transform.WorldPosition;
            return new Rectangle(pos.X - w / 2f, pos.Y - h / 2f, w, h);
        }

        return null;
    }

    static bool Contains(Rectangle b, Vector2 p)
        => p.X >= b.X && p.X <= b.X + b.Width && p.Y >= b.Y && p.Y <= b.Y + b.Height;

    /// <summary>
    /// Sélectionne l'objet visuellement le plus "au-dessus" sous le curseur
    /// (dernier dessiné = dernier de AllObjects, donc on parcourt à l'envers).
    /// Clic sur du vide → désélectionne, comme dans Unity.
    /// </summary>
    void TryPickObject(ImGuiIOPtr io, System.Numerics.Vector2 imageScreenPos)
    {
        var scene = SceneManager.Current;
        if (scene == null) return;

        Vector2 worldPoint = AbsScreenToWorld(io.MousePos, imageScreenPos);

        GameObject? hit = null;
        foreach (var obj in scene.AllObjects.Reverse())
        {
            var b = GetClickBounds(obj);
            if (b == null) continue;
            if (Contains(b.Value, worldPoint)) { hit = obj; break; }
        }

        EditorSelection.Selected = hit;
    }

    // ---------------------------------------------------------------
    // ImGui
    // ---------------------------------------------------------------

    /// <summary>
    /// Dessine le panneau ImGui, mesure la taille pour la frame suivante,
    /// convertit la souris en coordonnées viewport, et met à jour l'EditorCamera
    /// (pan / zoom) quand la souris survole le panneau.
    /// Appelé DANS le bloc ImGui, après RenderScene().
    /// </summary>
    public void Draw(bool editorMode)
    {
        ImGui.Begin("Scene");

        System.Numerics.Vector2 avail = ImGui.GetContentRegionAvail();
        int availW = (int)avail.X;
        int availH = (int)avail.Y;

        if (availW > 0 && availH > 0 && (availW != pendingWidth || availH != pendingHeight))
        {
            pendingWidth = availW;
            pendingHeight = availH;
            resizeRequested = true;
        }

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
        if (editorMode && overViewport) HandleCameraInput(io, localMouse);
        if (editorMode) StepEditorCamera(io.DeltaTime);

        if (editorMode)
        {
            DrawCameraGizmos(imageScreenPos, avail);

            var dragBefore = activeDrag;
            DrawGizmo(imageScreenPos, io, overViewport);

            bool gizmoConsumedClick = dragBefore == DragMode.None && activeDrag != DragMode.None;

            if (!gizmoConsumedClick && overViewport && io.MouseClicked[0])
                TryPickObject(io, imageScreenPos);
        }

        ImGui.End();
    }

    // ---------------------------------------------------------------
    // Caméra éditeur
    // ---------------------------------------------------------------
    private float cameraTargetZoom;
    private Vector2 cameraTargetPosition;
    private Vector2 zoomAnchor;   // point sous lequel on zoome (coordonnées viewport)

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
            var delta = new Vector2(-io.MouseDelta.X, io.MouseDelta.Y) / EditorCamera.Zoom / Renderer.PixelsPerUnit;
            EditorCamera.Position += delta;
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
            float step = ScrollPanPixels / (EditorCamera.Zoom * Renderer.PixelsPerUnit);

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
        float diff = cameraTargetZoom - EditorCamera.Zoom;
        if (MathF.Abs(diff) > cameraTargetZoom * 0.0005f)
        {
            Vector2 worldBefore = EditorCamera.ScreenToWorld(zoomAnchor);
            EditorCamera.Zoom += diff * Damp(ZoomSmoothing * 1000, dt);
            Vector2 worldAfter = EditorCamera.ScreenToWorld(zoomAnchor);

            Vector2 correction = worldBefore - worldAfter;
            EditorCamera.Position += correction;
            cameraTargetPosition += correction;
        }
        else EditorCamera.Zoom = cameraTargetZoom;

        EditorCamera.Position = Vector2.Lerp(EditorCamera.Position, cameraTargetPosition, Damp(PanSmoothing * 1000, dt));
    }

    // ---------------------------------------------------------------
    // Gizmos de transform (Move / Rotate / Scale)
    // ---------------------------------------------------------------

    System.Numerics.Vector2 WorldToAbsScreen(Vector2 worldPos, System.Numerics.Vector2 imageScreenPos)
    {
        var local = EditorCamera.WorldToScreen(worldPos);
        return new System.Numerics.Vector2(imageScreenPos.X + local.X, imageScreenPos.Y + local.Y);
    }

    Vector2 AbsScreenToWorld(System.Numerics.Vector2 absScreen, System.Numerics.Vector2 imageScreenPos)
    {
        var local = new Vector2(absScreen.X - imageScreenPos.X, absScreen.Y - imageScreenPos.Y);
        return EditorCamera.ScreenToWorld(local);
    }

    static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;

    static float AngleTo(Vector2 from, Vector2 to)
        => MathF.Atan2(to.Y - from.Y, to.X - from.X) * 180f / MathF.PI;

    static bool IsNear(System.Numerics.Vector2 a, System.Numerics.Vector2 b, float threshold)
        => (a - b).Length() <= threshold;

    static void DrawSquare(ImDrawListPtr dl, System.Numerics.Vector2 center, float size, uint color)
    {
        float h = size * 0.5f;
        dl.AddRectFilled(
            new System.Numerics.Vector2(center.X - h, center.Y - h),
            new System.Numerics.Vector2(center.X + h, center.Y + h),
            color);
    }

    Vector2 GetAxisDir(GameObject obj, bool xAxis)
    {
        if (EditorSelection.ActiveGizmoSpace == GizmoSpace.World)
            return xAxis ? new Vector2(1, 0) : new Vector2(0, 1);

        float rad = -obj.transform.WorldRotation * MathF.PI / 180f; // signe inversé
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        return xAxis ? new Vector2(cos, sin) : new Vector2(-sin, cos);
    }

    void StartDrag(DragMode mode, GameObject obj, ImGuiIOPtr io, System.Numerics.Vector2 imageScreenPos)
    {
        activeDrag = mode;
        dragStartWorldMouse = AbsScreenToWorld(io.MousePos, imageScreenPos);
        dragStartPosition = obj.transform.Position;
        dragStartScale = obj.transform.Scale;
        dragStartRotationValue = obj.transform.Rotation;
        dragStartMouseAngle = AngleTo(obj.transform.WorldPosition, dragStartWorldMouse);
    }

    void DrawGizmo(System.Numerics.Vector2 imageScreenPos, ImGuiIOPtr io, bool overViewport)
    {
        var obj = EditorSelection.Selected;
        if (obj == null)
        {
            activeDrag = DragMode.None;
            return;
        }

        if (obj.transform is RectTransform) { activeDrag = DragMode.None; return; }

        var dl = ImGui.GetWindowDrawList();
        Vector2 originWorld = obj.transform.WorldPosition;
        System.Numerics.Vector2 originScreen = WorldToAbsScreen(originWorld, imageScreenPos);

        switch (EditorSelection.ActiveGizmoMode)
        {
            case GizmoMode.Move:
                DrawMoveGizmo(obj, dl, originWorld, originScreen, imageScreenPos, io, overViewport);
                break;
            case GizmoMode.Scale:
                DrawScaleGizmo(obj, dl, originWorld, originScreen, imageScreenPos, io, overViewport);
                break;
            case GizmoMode.Rotate:
                DrawRotateGizmo(obj, dl, originWorld, originScreen, imageScreenPos, io, overViewport);
                break;
        }

        if (activeDrag != DragMode.None && !io.MouseDown[0])
            activeDrag = DragMode.None;
    }

    // ---------------------------------------------------------------
    // Gizmos de transform (Style Moderne Unity/Unreal)
    // ---------------------------------------------------------------

    void DrawMoveGizmo(GameObject obj, ImDrawListPtr dl, Vector2 originWorld,
        System.Numerics.Vector2 originScreen, System.Numerics.Vector2 imageScreenPos,
        ImGuiIOPtr io, bool overViewport)
    {
        bool isDraggingX = activeDrag == DragMode.MoveX;
        bool isDraggingY = activeDrag == DragMode.MoveY;
        bool isDraggingFree = activeDrag == DragMode.MoveFree;

        uint colorX = isDraggingX ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f)) : ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
        uint colorY = isDraggingY ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f)) : ImGui.GetColorU32(new System.Numerics.Vector4(0.35f, 0.85f, 0.35f, 1f));
        uint colorCenter = isDraggingFree ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f)) : ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.9f));

        // Directions des axes converties à l'écran
        Vector2 xDirWorld = GetAxisDir(obj, true);
        Vector2 yDirWorld = GetAxisDir(obj, false);

        var xTipScreenTarget = WorldToAbsScreen(new Vector2(originWorld.X + xDirWorld.X, originWorld.Y + xDirWorld.Y), imageScreenPos);
        var yTipScreenTarget = WorldToAbsScreen(new Vector2(originWorld.X + yDirWorld.X, originWorld.Y + yDirWorld.Y), imageScreenPos);

        System.Numerics.Vector2 xScreenDir = System.Numerics.Vector2.Normalize(xTipScreenTarget - originScreen);
        System.Numerics.Vector2 yScreenDir = System.Numerics.Vector2.Normalize(yTipScreenTarget - originScreen);

        System.Numerics.Vector2 xTipScreen = originScreen + xScreenDir * ArmLength;
        System.Numerics.Vector2 yTipScreen = originScreen + yScreenDir * ArmLength;

        float arrowSize = 12f;
        System.Numerics.Vector2 xPerp = new System.Numerics.Vector2(-xScreenDir.Y, xScreenDir.X);
        System.Numerics.Vector2 yPerp = new System.Numerics.Vector2(-yScreenDir.Y, yScreenDir.X);

        // Lignes et triangles basés sur les pixels écran
        dl.AddLine(originScreen, xTipScreen - xScreenDir * arrowSize, colorX, 2.5f);
        dl.AddLine(originScreen, yTipScreen - yScreenDir * arrowSize, colorY, 2.5f);

        dl.AddTriangleFilled(xTipScreen, xTipScreen - xScreenDir * arrowSize + xPerp * (arrowSize * 0.4f), xTipScreen - xScreenDir * arrowSize - xPerp * (arrowSize * 0.4f), colorX);
        dl.AddTriangleFilled(yTipScreen, yTipScreen - yScreenDir * arrowSize + yPerp * (arrowSize * 0.4f), yTipScreen - yScreenDir * arrowSize - yPerp * (arrowSize * 0.4f), colorY);

        // Carré central
        float quadSize = 10f;
        System.Numerics.Vector2 q1 = originScreen + xScreenDir * quadSize;
        System.Numerics.Vector2 q2 = originScreen + xScreenDir * quadSize + yScreenDir * quadSize;
        System.Numerics.Vector2 q3 = originScreen + yScreenDir * quadSize;

        uint quadFill = isDraggingFree ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 0.5f)) : ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.2f));
        dl.AddQuadFilled(originScreen, q1, q2, q3, quadFill);
        dl.AddQuad(originScreen, q1, q2, q3, colorCenter, 1.5f);

        // Interactions et Drag (reste basé sur le déplacement monde via delta souris)
        if (overViewport && activeDrag == DragMode.None && io.MouseClicked[0])
        {
            if (IsNear(io.MousePos, xTipScreen, HandleSize + HitPadding)) StartDrag(DragMode.MoveX, obj, io, imageScreenPos);
            else if (IsNear(io.MousePos, yTipScreen, HandleSize + HitPadding)) StartDrag(DragMode.MoveY, obj, io, imageScreenPos);
            else if (IsNear(io.MousePos, originScreen + (q2 - originScreen) * 0.5f, quadSize + HitPadding)) StartDrag(DragMode.MoveFree, obj, io, imageScreenPos);
        }

        if (activeDrag is DragMode.MoveX or DragMode.MoveY or DragMode.MoveFree)
        {
            Vector2 mouseWorldNow = AbsScreenToWorld(io.MousePos, imageScreenPos);
            Vector2 delta = new Vector2(mouseWorldNow.X - dragStartWorldMouse.X, mouseWorldNow.Y - dragStartWorldMouse.Y);

            Vector2 worldDelta = activeDrag switch
            {
                DragMode.MoveX => new Vector2(xDirWorld.X * Dot(delta, xDirWorld), xDirWorld.Y * Dot(delta, xDirWorld)),
                DragMode.MoveY => new Vector2(yDirWorld.X * Dot(delta, yDirWorld), yDirWorld.Y * Dot(delta, yDirWorld)),
                _ => delta
            };

            obj.transform.Position = new Vector2(
                dragStartPosition.X + worldDelta.X,
                dragStartPosition.Y + worldDelta.Y);
        }
    }

    void DrawScaleGizmo(GameObject obj, ImDrawListPtr dl, Vector2 originWorld,
    System.Numerics.Vector2 originScreen, System.Numerics.Vector2 imageScreenPos,
    ImGuiIOPtr io, bool overViewport)
    {
        bool isDraggingX = activeDrag == DragMode.ScaleX;
        bool isDraggingY = activeDrag == DragMode.ScaleY;
        bool isDraggingFree = activeDrag == DragMode.ScaleFree;

        uint colorX = isDraggingX ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f)) : ImGui.GetColorU32(new System.Numerics.Vector4(0.95f, 0.25f, 0.25f, 1f));
        uint colorY = isDraggingY ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f)) : ImGui.GetColorU32(new System.Numerics.Vector4(0.35f, 0.85f, 0.35f, 1f));
        uint colorCenter = isDraggingFree ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f)) : ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.9f));

        // Directions des axes converties à l'écran
        Vector2 xDirWorld = GetAxisDir(obj, true);
        Vector2 yDirWorld = GetAxisDir(obj, false);

        var xTipScreenTarget = WorldToAbsScreen(new Vector2(originWorld.X + xDirWorld.X, originWorld.Y + xDirWorld.Y), imageScreenPos);
        var yTipScreenTarget = WorldToAbsScreen(new Vector2(originWorld.X + yDirWorld.X, originWorld.Y + yDirWorld.Y), imageScreenPos);

        System.Numerics.Vector2 xScreenDir = System.Numerics.Vector2.Normalize(xTipScreenTarget - originScreen);
        System.Numerics.Vector2 yScreenDir = System.Numerics.Vector2.Normalize(yTipScreenTarget - originScreen);

        System.Numerics.Vector2 xTipScreen = originScreen + xScreenDir * ArmLength;
        System.Numerics.Vector2 yTipScreen = originScreen + yScreenDir * ArmLength;

        System.Numerics.Vector2 xPerp = new System.Numerics.Vector2(-xScreenDir.Y, xScreenDir.X);
        System.Numerics.Vector2 yPerp = new System.Numerics.Vector2(-yScreenDir.Y, yScreenDir.X);

        float boxSize = 8f;

        // Lignes des axes
        dl.AddLine(originScreen, xTipScreen + xScreenDir * boxSize, colorX, 2.5f);
        dl.AddLine(originScreen, yTipScreen + yScreenDir * boxSize, colorY, 2.5f);

        // Formes au bout des axes (Carrés pour le Scale, ou Triangles si vous préférez)
        DrawSquare(dl, xTipScreen, boxSize, colorX);
        DrawSquare(dl, yTipScreen, boxSize, colorY);

        // Carré central
        float quadSize = 10f;
        System.Numerics.Vector2 q1 = originScreen + xScreenDir * quadSize;
        System.Numerics.Vector2 q2 = originScreen + xScreenDir * quadSize + yScreenDir * quadSize;
        System.Numerics.Vector2 q3 = originScreen + yScreenDir * quadSize;

        uint quadFill = isDraggingFree ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 0.5f)) : ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.2f));
        dl.AddQuadFilled(originScreen, q1, q2, q3, quadFill);
        dl.AddQuad(originScreen, q1, q2, q3, colorCenter, 1.5f);

        // Interactions et Drag
        if (overViewport && activeDrag == DragMode.None && io.MouseClicked[0])
        {
            if (IsNear(io.MousePos, xTipScreen, HandleSize + HitPadding)) StartDrag(DragMode.ScaleX, obj, io, imageScreenPos);
            else if (IsNear(io.MousePos, yTipScreen, HandleSize + HitPadding)) StartDrag(DragMode.ScaleY, obj, io, imageScreenPos);
            else if (IsNear(io.MousePos, originScreen + (q2 - originScreen) * 0.5f, quadSize + HitPadding)) StartDrag(DragMode.ScaleFree, obj, io, imageScreenPos);
        }

        if (activeDrag is DragMode.ScaleX or DragMode.ScaleY or DragMode.ScaleFree)
        {
            Vector2 mouseWorldNow = AbsScreenToWorld(io.MousePos, imageScreenPos);
            Vector2 delta = new Vector2(mouseWorldNow.X - dragStartWorldMouse.X, mouseWorldNow.Y - dragStartWorldMouse.Y);

            // Projection du delta souris sur les axes locaux (exactement comme le MoveGizmo)
            float armWorld = ArmLength / (EditorCamera.Zoom * Renderer.PixelsPerUnit);   // longueur du bras, en mètres
            float dx = Dot(delta, xDirWorld) / armWorld;
            float dy = Dot(delta, yDirWorld) / armWorld;

            Vector2 newScale = dragStartScale;

            if (activeDrag == DragMode.ScaleX)
            {
                // Modifie uniquement X
                newScale = new Vector2(MathF.Max(0.01f, dragStartScale.X + dx), dragStartScale.Y);
            }
            else if (activeDrag == DragMode.ScaleY)
            {
                // Modifie uniquement Y
                newScale = new Vector2(dragStartScale.X, MathF.Max(0.01f, dragStartScale.Y + dy));
            }
            else if (activeDrag == DragMode.ScaleFree)
            {
                // Échelle proportionnelle globale (uniforme)
                float uniform = MathF.Max(0.01f, dragStartScale.X + dx);
                float ratio = dragStartScale.X > 0.0001f ? dragStartScale.Y / dragStartScale.X : 1f;
                newScale = new Vector2(uniform, uniform * ratio);
            }

            obj.transform.Scale = newScale;
        }
    }

    void DrawRotateGizmo(GameObject obj, ImDrawListPtr dl, Vector2 originWorld,
    System.Numerics.Vector2 originScreen, System.Numerics.Vector2 imageScreenPos,
    ImGuiIOPtr io, bool overViewport)
    {
        bool isDragging = activeDrag == DragMode.Rotate;

        uint ringColor = isDragging
            ? ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.9f, 0.2f, 1f))
            : ImGui.GetColorU32(new System.Numerics.Vector4(0.2f, 0.6f, 1f, 0.9f));

        uint trackColor = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.15f));

        // Utilisation directe de ArmLength en pixels écran pour garantir un cercle stable
        float radiusScreen = ArmLength;

        // 1. Anneau principal
        dl.AddCircle(originScreen, radiusScreen, ringColor, 64, 2f);

        // 2. Petit repère au centre
        dl.AddCircleFilled(originScreen, 3f, ringColor);

        if (overViewport && activeDrag == DragMode.None && io.MouseClicked[0])
        {
            float distToCenter = (io.MousePos - originScreen).Length();
            if (MathF.Abs(distToCenter - radiusScreen) <= HitPadding * 2f)
                StartDrag(DragMode.Rotate, obj, io, imageScreenPos);
        }

        if (activeDrag == DragMode.Rotate)
        {
            Vector2 mouseWorldNow = AbsScreenToWorld(io.MousePos, imageScreenPos);
            float angleNow = AngleTo(obj.transform.WorldPosition, mouseWorldNow);
            float deltaAngle = angleNow - dragStartMouseAngle;
            obj.transform.Rotation = dragStartRotationValue + deltaAngle;

            // Visualisation de l'angle sur le cercle
            System.Numerics.Vector2 startMouseScreen = WorldToAbsScreen(dragStartWorldMouse, imageScreenPos);

            System.Numerics.Vector2 mouseDir = io.MousePos - originScreen;
            if (mouseDir.LengthSquared() > 0.001f)
            {
                mouseDir = System.Numerics.Vector2.Normalize(mouseDir);
            }
            System.Numerics.Vector2 snappedMouseScreen = originScreen + mouseDir * radiusScreen;

            dl.AddLine(originScreen, snappedMouseScreen, ringColor, 2f);
        }
    }

    public void Destroy() => renderTexture.Destroy();
}