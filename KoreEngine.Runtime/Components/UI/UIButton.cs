using SDL3;

namespace KoreEngine.UI;
public class UIButton : UIElement
{
    public IntPtr TextureNormal;
    public IntPtr TextureHover;
    public IntPtr TexturePressed;

    public string TextureNormalPath = "";
    public string TextureHoverPath = "";
    public string TexturePressedPath = "";

    public KoreEvent OnClick = new();

    bool hovered, pressed;

    // ---------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------

    public override void Update(float dt)
    {
        float mx = SceneManager.ViewportMouseX;
        float my = SceneManager.ViewportMouseY;
        bool overViewport = SceneManager.ViewportMouseInBounds;

        hovered = overViewport &&
                  mx >= ScreenX && mx <= ScreenX + ScreenWidth &&
                  my >= ScreenY && my <= ScreenY + ScreenHeight;

        if (hovered)
        {
            bool mouseDown = (SDL.GetMouseState(out _, out _) & SDL.MouseButtonFlags.Left) != 0;
            if (mouseDown && !pressed) { pressed = true; OnClick.Invoke(); }
            else if (!mouseDown) pressed = false;
        }
        else pressed = false;
    }

    // ---------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------

    public override void Draw(Renderer renderer)
    {
        IntPtr tex = hovered ? (pressed ? TexturePressed : TextureHover) : TextureNormal;
        if (tex == IntPtr.Zero) tex = TextureNormal;
        if (tex != IntPtr.Zero) renderer.DrawTexture(tex, (int)ScreenX, (int)ScreenY, (int)ScreenWidth, (int)ScreenHeight);
    }
}