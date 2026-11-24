namespace KoreEngine.UI;

public class UICanvas : UIElement
{
    public float ReferenceWidth = 1920f;
    public float ReferenceHeight = 1080f;

    readonly Camera overlayCamera = new();

    public void SyncSize() => RectTransform.SizeDelta = new Vector2(ReferenceWidth, ReferenceHeight);

    /// Vue Scene : la caméra éditeur. Vue Game : caméra virtuelle qui cadre le canvas (match hauteur).
    public Camera ViewCamera
    {
        get
        {
            SyncSize();
            if (UIContext.WorldCamera != null) return UIContext.WorldCamera;

            var r = RectTransform.Rect;
            overlayCamera.ViewWidth = SceneManager.ViewportWidth;
            overlayCamera.ViewHeight = SceneManager.ViewportHeight;
            overlayCamera.Zoom = ReferenceHeight > 0 ? SceneManager.ViewportHeight / ReferenceHeight : 1f;
            overlayCamera.Position = new Vector2((r.Min.X + r.Max.X) * 0.5f, (r.Min.Y + r.Max.Y) * 0.5f);
            return overlayCamera;
        }
    }

    // Compat : UIText & co utilisent peut-être encore ScaleX/ScaleY
    public float ScaleX => ViewCamera.Zoom;
    public float ScaleY => ViewCamera.Zoom;

    protected override Camera ResolveCamera() => ViewCamera;

    public T Add<T>(T element) where T : UIElement
    {
        if (gameObject?.Scene == null)
            throw new InvalidOperationException("UICanvas.Add() : appelle scene.Add(canvasObject) avant canvas.Add(...).");

        var go = GameObject.CreateUI(element.GetType().Name);
        gameObject.Scene.Add(go);
        go.transform.AttachToParentKeepLocal(gameObject, gameObject.Scene); // garde la position locale : centré dans le canvas
        element.Canvas = this;
        go.AddComponent(element);
        return element;
    }

    public override void Draw(Renderer renderer) => SyncSize();

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new IntField
        {
            Label = "Reference Width",
            Get = () => (int)ReferenceWidth,
            Set = v => ReferenceWidth = v,
            Min = 1,
            Max = 7680
        };

        yield return new IntField
        {
            Label = "Reference Height",
            Get = () => (int)ReferenceHeight,
            Set = v => ReferenceHeight = v,
            Min = 1,
            Max = 4320
        };
    }
}