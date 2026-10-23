namespace KoreEngine.UI;

public static class UIContext
{
    /// Non-null pendant le rendu du panel UI Editor : le canvas est vu à travers cette caméra.
    public static Camera? WorldCamera;

    /// True pendant le rendu de la vue Scene : l'UI n'y apparaît pas.
    public static bool HideUI;

    public static UICanvas? FindCanvas(GameObject? obj)
    {
        for (var o = obj; o != null; o = o.transform.Parent)
            if (o.GetComponent<UICanvas>() is { } c) return c;
        return null;
    }

    /// Rend uniquement le canvas et ses enfants.
    public static void RenderCanvas(UICanvas canvas, Renderer renderer, Camera camera)
    {
        var prev = WorldCamera;
        WorldCamera = camera;
        try { canvas.gameObject.Render(renderer, camera); }
        finally { WorldCamera = prev; }
    }
}