using ImGuiNET;
using KoreEngine.Components;
using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Editor
{
    public class ViewportPanel
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
        const float LineThickness = 6f;      // épaisseur des flèches Move/Scale
        const float RingThickness = 6f;      // épaisseur de l'anneau de rotation

        public ViewportPanel(Renderer renderer, RenderTexture renderTexture)
        {
            this.renderer = renderer;
            this.renderTexture = renderTexture;
            pendingWidth = renderTexture.Width;
            pendingHeight = renderTexture.Height;

            EditorCamera = new EditorCamera(renderTexture.Width, renderTexture.Height);
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

                // Toujours sync l'EditorCamera, quelle que soit la caméra active.
                EditorCamera.ViewWidth = renderTexture.Width;
                EditorCamera.ViewHeight = renderTexture.Height;

                if (camera != null)
                {
                    camera.ViewWidth = renderTexture.Width;
                    camera.ViewHeight = renderTexture.Height;
                }

                resizeRequested = false;
            }

            // Toujours synchronisé (UICanvas, etc.) même si aucun resize cette frame.
            SceneManager.ViewportWidth = renderTexture.Width;
            SceneManager.ViewportHeight = renderTexture.Height;
        }

        // ---------------------------------------------------------------
        // Rendu
        // ---------------------------------------------------------------

        /// <summary>
        /// Rend la scène DANS la RenderTexture avec la caméra indiquée.
        /// Appelé hors du bloc ImGui, avant Draw().
        ///   - mode édition : passer EditorCamera
        ///   - mode jeu     : passer scene.Camera
        /// </summary>
        public void RenderScene(Scene? scene, Camera? camera, bool editorMode = false)
        {
            Camera? previous = null;
            if (scene != null && camera != null)
            {
                previous = scene.Camera;
                scene.Camera = camera;
            }

            renderTexture.BeginRender();
            scene?.Render(renderer);

            // Overlay collider — uniquement en mode édition
            if (editorMode && camera != null)
                RenderColliderOverlay(camera);

            renderTexture.EndRender();

            if (scene != null && previous != null)
                scene.Camera = previous;
        }

        /// <summary>
        /// Dessine le contour wireframe du Collider de l'objet sélectionné.
        /// Vert pour un collider normal, jaune pour un trigger.
        /// </summary>
        void RenderColliderOverlay(Camera camera)
        {
            var selected = EditorSelection.Selected;
            if (selected == null) return;

            var collider = selected.GetComponent<Collider>();
            if (collider == null) return;

            var bounds = collider.Bounds;

            byte r = collider.IsTrigger ? (byte)255 : (byte)0;
            byte g = (byte)255;
            byte b = collider.IsTrigger ? (byte)0 : (byte)0;

            renderer.DrawRectOutline(
                bounds.X, bounds.Y + bounds.Height,
                bounds.Width, bounds.Height,
                r, g, b, 255,
                camera);
        }

        // ---------------------------------------------------------------
        // Sélection par clic dans le viewport
        // ---------------------------------------------------------------

        Rectangle? GetClickBounds(GameObject obj)
        {
            var collider = obj.GetComponent<Collider>();
            if (collider != null) return collider.Bounds;

            var rect = obj.GetComponent<RectRenderer>();
            if (rect != null)
            {
                var scale = obj.WorldScale;
                int w = (int)(rect.Size.X * scale.X);
                int h = (int)(rect.Size.Y * scale.Y);
                var pos = obj.WorldPosition;
                return new Rectangle((int)(pos.X - w / 2f), (int)(pos.Y - h / 2f), w, h);
            }

            var sprite = obj.GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                var scale = obj.WorldScale;
                int w = (int)(sprite.Size.X * scale.X);
                int h = (int)(sprite.Size.Y * scale.Y);
                var pos = obj.WorldPosition;
                return new Rectangle((int)(pos.X - w / 2f), (int)(pos.Y - h / 2f), w, h);
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
            ImGui.Begin("Viewport");

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
            SceneManager.ViewportMouseX = mouseScreen.X - imageScreenPos.X;
            SceneManager.ViewportMouseY = mouseScreen.Y - imageScreenPos.Y;
            SceneManager.ViewportMouseInBounds = overViewport;

            // Contrôles caméra éditeur — uniquement en mode édition et quand
            // la souris est au-dessus du viewport.
            if (editorMode && overViewport)
                UpdateEditorCamera(io);

            if (editorMode)
            {
                var dragBefore = activeDrag;
                DrawGizmo(imageScreenPos, io, overViewport);

                // Si le gizmo vient de démarrer un drag ce frame (clic sur une poignée),
                // ce même clic ne doit pas aussi être interprété comme une sélection.
                bool gizmoConsumedClick = dragBefore == DragMode.None && activeDrag != DragMode.None;

                if (!gizmoConsumedClick && overViewport && io.MouseClicked[0])
                    TryPickObject(io, imageScreenPos);
            }

            ImGui.End();
        }

        // ---------------------------------------------------------------
        // Caméra éditeur
        // ---------------------------------------------------------------

        void UpdateEditorCamera(ImGuiIOPtr io)
        {
            // --- PAN : clic molette (bouton 2) maintenu + drag ---
            // io.MouseDown[2] = bouton du milieu.
            // io.MouseDelta   = déplacement souris cette frame, en pixels écran.
            // On divise par Zoom pour que le pan soit cohérent quelle que soit
            // l'échelle : 1 pixel déplacé = 1 unité monde / zoom.
            if (io.MouseDown[2])
            {
                EditorCamera.Position = new Vector2(
                    EditorCamera.Position.X - io.MouseDelta.X / EditorCamera.Zoom,
                    EditorCamera.Position.Y + io.MouseDelta.Y / EditorCamera.Zoom  // signe inversé
                );
            }

            // --- ZOOM : Ctrl + molette ---
            // Sans Ctrl, ImGui scrolle le panneau normalement (comportement par
            // défaut non bloqué). Avec Ctrl on consomme l'événement pour le zoom.
            if (io.KeyCtrl && io.MouseWheel != 0f)
            {
                float factor = 1f + ZoomStep * io.MouseWheel;
                float newZoom = Math.Clamp(EditorCamera.Zoom * factor, ZoomMin, ZoomMax);

                // Zoom centré sur la position souris dans le monde :
                // on recalcule la position caméra pour que le point sous la souris
                // reste fixe à l'écran (comportement standard d'un éditeur 2D).
                Vector2 mouseViewport = new Vector2(
                    SceneManager.ViewportMouseX,
                    SceneManager.ViewportMouseY
                );
                Vector2 worldBefore = EditorCamera.ScreenToWorld(mouseViewport);

                EditorCamera.Zoom = newZoom;

                Vector2 worldAfter = EditorCamera.ScreenToWorld(mouseViewport);
                EditorCamera.Position = new Vector2(
                    EditorCamera.Position.X + (worldBefore.X - worldAfter.X),
                    EditorCamera.Position.Y + (worldBefore.Y - worldAfter.Y)
                );
            }
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

            float rad = -obj.WorldRotation * MathF.PI / 180f; // signe inversé
            float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
            return xAxis ? new Vector2(cos, sin) : new Vector2(-sin, cos);
        }

        void StartDrag(DragMode mode, GameObject obj, ImGuiIOPtr io, System.Numerics.Vector2 imageScreenPos)
        {
            activeDrag = mode;
            dragStartWorldMouse = AbsScreenToWorld(io.MousePos, imageScreenPos);
            dragStartPosition = obj.LocalPosition;
            dragStartScale = obj.LocalScale;
            dragStartRotationValue = obj.LocalRotation;
            dragStartMouseAngle = AngleTo(obj.WorldPosition, dragStartWorldMouse);
        }

        void DrawGizmo(System.Numerics.Vector2 imageScreenPos, ImGuiIOPtr io, bool overViewport)
        {
            var obj = EditorSelection.Selected;
            if (obj == null)
            {
                activeDrag = DragMode.None;
                return;
            }

            var dl = ImGui.GetWindowDrawList();
            Vector2 originWorld = obj.WorldPosition;
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

        void DrawMoveGizmo(GameObject obj, ImDrawListPtr dl, Vector2 originWorld,
            System.Numerics.Vector2 originScreen, System.Numerics.Vector2 imageScreenPos,
            ImGuiIOPtr io, bool overViewport)
        {
            uint red = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.25f, 0.25f, 1f));
            uint green = ImGui.GetColorU32(new System.Numerics.Vector4(0.25f, 1f, 0.25f, 1f));
            uint white = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 1f));

            Vector2 xDir = GetAxisDir(obj, true);
            Vector2 yDir = GetAxisDir(obj, false);

            Vector2 xTipWorld = new Vector2(originWorld.X + xDir.X * ArmLength, originWorld.Y + xDir.Y * ArmLength);
            Vector2 yTipWorld = new Vector2(originWorld.X + yDir.X * ArmLength, originWorld.Y + yDir.Y * ArmLength);

            var xTipScreen = WorldToAbsScreen(xTipWorld, imageScreenPos);
            var yTipScreen = WorldToAbsScreen(yTipWorld, imageScreenPos);

            dl.AddLine(originScreen, xTipScreen, red, LineThickness);
            dl.AddLine(originScreen, yTipScreen, green, LineThickness);

            float tipR = HandleSize * EditorCamera.Zoom * 0.5f;
            float centerR = tipR * 0.7f;
            dl.AddCircleFilled(xTipScreen, tipR, red);
            dl.AddCircleFilled(yTipScreen, tipR, green);
            dl.AddCircleFilled(originScreen, centerR, white);

            if (overViewport && activeDrag == DragMode.None && io.MouseClicked[0])
            {
                if (IsNear(io.MousePos, xTipScreen, tipR + HitPadding)) StartDrag(DragMode.MoveX, obj, io, imageScreenPos);
                else if (IsNear(io.MousePos, yTipScreen, tipR + HitPadding)) StartDrag(DragMode.MoveY, obj, io, imageScreenPos);
                else if (IsNear(io.MousePos, originScreen, centerR + HitPadding)) StartDrag(DragMode.MoveFree, obj, io, imageScreenPos);
            }

            if (activeDrag is DragMode.MoveX or DragMode.MoveY or DragMode.MoveFree)
            {
                Vector2 mouseWorldNow = AbsScreenToWorld(io.MousePos, imageScreenPos);
                Vector2 delta = new Vector2(mouseWorldNow.X - dragStartWorldMouse.X, mouseWorldNow.Y - dragStartWorldMouse.Y);

                Vector2 worldDelta = activeDrag switch
                {
                    DragMode.MoveX => new Vector2(xDir.X * Dot(delta, xDir), xDir.Y * Dot(delta, xDir)),
                    DragMode.MoveY => new Vector2(yDir.X * Dot(delta, yDir), yDir.Y * Dot(delta, yDir)),
                    _ => delta
                };

                // NOTE : suppose que le parent (s'il existe) n'a ni rotation ni scale
                // — cohérent avec le reste du moteur actuel (SetParent fait la même
                // hypothèse). À revoir si un jour les parents peuvent tourner/scaler.
                obj.LocalPosition = new Vector2(
                    dragStartPosition.X + worldDelta.X,
                    dragStartPosition.Y + worldDelta.Y);
            }
        }

        void DrawScaleGizmo(GameObject obj, ImDrawListPtr dl, Vector2 originWorld,
            System.Numerics.Vector2 originScreen, System.Numerics.Vector2 imageScreenPos,
            ImGuiIOPtr io, bool overViewport)
        {
            uint red = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.25f, 0.25f, 1f));
            uint green = ImGui.GetColorU32(new System.Numerics.Vector4(0.25f, 1f, 0.25f, 1f));
            uint white = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 1f));

            Vector2 xDir = GetAxisDir(obj, true);
            Vector2 yDir = GetAxisDir(obj, false);

            Vector2 xTipWorld = new Vector2(originWorld.X + xDir.X * ArmLength, originWorld.Y + xDir.Y * ArmLength);
            Vector2 yTipWorld = new Vector2(originWorld.X + yDir.X * ArmLength, originWorld.Y + yDir.Y * ArmLength);

            var xTipScreen = WorldToAbsScreen(xTipWorld, imageScreenPos);
            var yTipScreen = WorldToAbsScreen(yTipWorld, imageScreenPos);

            dl.AddLine(originScreen, xTipScreen, red, 2f);
            dl.AddLine(originScreen, yTipScreen, green, 2f);

            float handlePx = HandleSize * EditorCamera.Zoom;
            float centerPx = handlePx * 0.7f;
            DrawSquare(dl, xTipScreen, handlePx, red);
            DrawSquare(dl, yTipScreen, handlePx, green);
            DrawSquare(dl, originScreen, centerPx, white);

            if (overViewport && activeDrag == DragMode.None && io.MouseClicked[0])
            {
                if (IsNear(io.MousePos, xTipScreen, handlePx + HitPadding)) StartDrag(DragMode.ScaleX, obj, io, imageScreenPos);
                else if (IsNear(io.MousePos, yTipScreen, handlePx + HitPadding)) StartDrag(DragMode.ScaleY, obj, io, imageScreenPos);
                else if (IsNear(io.MousePos, originScreen, centerPx + HitPadding)) StartDrag(DragMode.ScaleFree, obj, io, imageScreenPos);
            }

            if (activeDrag is DragMode.ScaleX or DragMode.ScaleY or DragMode.ScaleFree)
            {
                Vector2 mouseWorldNow = AbsScreenToWorld(io.MousePos, imageScreenPos);
                Vector2 delta = new Vector2(mouseWorldNow.X - dragStartWorldMouse.X, mouseWorldNow.Y - dragStartWorldMouse.Y);

                float dx = Dot(delta, xDir) / ArmLength;
                float dy = Dot(delta, yDir) / ArmLength;

                Vector2 newScale = dragStartScale;
                if (activeDrag == DragMode.ScaleX)
                    newScale = new Vector2(MathF.Max(0.01f, dragStartScale.X + dx), dragStartScale.Y);
                else if (activeDrag == DragMode.ScaleY)
                    newScale = new Vector2(dragStartScale.X, MathF.Max(0.01f, dragStartScale.Y + dy));
                else // ScaleFree — uniforme, garde le ratio initial, piloté par dx
                {
                    float uniform = MathF.Max(0.01f, dragStartScale.X + dx);
                    float ratio = dragStartScale.X > 0.0001f ? dragStartScale.Y / dragStartScale.X : 1f;
                    newScale = new Vector2(uniform, uniform * ratio);
                }

                obj.LocalScale = newScale;
            }
        }

        void DrawRotateGizmo(GameObject obj, ImDrawListPtr dl, Vector2 originWorld,
            System.Numerics.Vector2 originScreen, System.Numerics.Vector2 imageScreenPos,
            ImGuiIOPtr io, bool overViewport)
        {
            uint orange = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 0.7f, 0f, 1f));

            Vector2 rimWorld = new Vector2(originWorld.X + ArmLength, originWorld.Y);
            var rimScreen = WorldToAbsScreen(rimWorld, imageScreenPos);
            float radiusScreen = (rimScreen - originScreen).Length();

            dl.AddCircle(originScreen, radiusScreen, orange, 48, RingThickness);

            if (overViewport && activeDrag == DragMode.None && io.MouseClicked[0])
            {
                float distToCenter = (io.MousePos - originScreen).Length();
                if (MathF.Abs(distToCenter - radiusScreen) <= HitPadding * 2f)
                    StartDrag(DragMode.Rotate, obj, io, imageScreenPos);
            }

            if (activeDrag == DragMode.Rotate)
            {
                Vector2 mouseWorldNow = AbsScreenToWorld(io.MousePos, imageScreenPos);
                float angleNow = AngleTo(obj.WorldPosition, mouseWorldNow);
                float deltaAngle = angleNow - dragStartMouseAngle;
                obj.LocalRotation = dragStartRotationValue - deltaAngle;
            }
        }

        public void Destroy() => renderTexture.Destroy();
    }
}