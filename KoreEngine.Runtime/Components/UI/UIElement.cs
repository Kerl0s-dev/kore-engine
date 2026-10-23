namespace KoreEngine.UI;

public abstract class UIElement : Component
{
    public bool Visible = true;

    UICanvas? canvas;
    public UICanvas? Canvas   // remonte la hiérarchie si non assigné (éléments créés dans l'éditeur)
    {
        get
        {
            if (canvas != null) return canvas;
            for (var p = gameObject.transform.Parent; p != null; p = p.transform.Parent)
                if (p.GetComponent<UICanvas>() is { } c) return canvas = c;
            return null;
        }
        set => canvas = value;
    }

    public RectTransform RectTransform => (RectTransform)gameObject.transform;
    public RectF WorldRect => RectTransform.Rect;

    protected virtual Camera ResolveCamera() => Canvas?.ViewCamera ?? Camera.Main;

    Vector2 TopLeftScreen { get { var r = WorldRect; return ResolveCamera().WorldToScreen(new Vector2(r.Min.X, r.Max.Y)); } }
    public float ScreenX => TopLeftScreen.X;
    public float ScreenY => TopLeftScreen.Y;
    public float ScreenWidth => WorldRect.Width * ResolveCamera().Zoom;
    public float ScreenHeight => WorldRect.Height * ResolveCamera().Zoom;

    public override void Render(Renderer renderer, Camera? camera)
    {
        if (!Visible || UIContext.HideUI) return;
        Draw(renderer);
    }

    public abstract void Draw(Renderer renderer);
}