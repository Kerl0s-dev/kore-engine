using KoreEngine.Core;
using KoreEngine.Engine;
using SDL3;

namespace KoreEngine.Components.UI;

public enum ButtonActionType
{
    None,
    LoadScene,
    Quit
}

public class UIButton : UIElement
{
    public IntPtr TextureNormal;
    public IntPtr TextureHover;
    public IntPtr TexturePressed;

    public string TextureNormalPath = "";
    public string TextureHoverPath = "";
    public string TexturePressedPath = "";

    // Action prédéfinie — sérialisable, éditable dans l'inspector
    public ButtonActionType ActionType = ButtonActionType.None;
    
    public string ActionParam = "";

    // Callback arbitraire — assigné par code, non sérialisable
    public delegate void OnClickDelegate();

    [HideInInspector]
    public OnClickDelegate? OnClick;

    bool hovered, pressed;

    static readonly string[] ActionNames = Enum.GetNames<ButtonActionType>();

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
            if (mouseDown && !pressed) { pressed = true; FireAction(); }
            else if (!mouseDown) pressed = false;
        }
        else pressed = false;
    }

    void FireAction()
    {
        // Callback arbitraire d'abord (assigné par code)
        OnClick?.Invoke();

        // Puis action prédéfinie (définie dans l'inspector)
        switch (ActionType)
        {
            case ButtonActionType.LoadScene:
                if (!string.IsNullOrEmpty(ActionParam))
                    SceneManager.LoadSceneFromFile(ActionParam);
                break;
            case ButtonActionType.Quit:
                Environment.Exit(0);
                break;
        }
    }

    // ---------------------------------------------------------------
    // Draw
    // ---------------------------------------------------------------

    public override void Draw(Renderer renderer)
    {
        IntPtr tex = hovered ? (pressed ? TexturePressed : TextureHover) : TextureNormal;
        if (tex == IntPtr.Zero) tex = TextureNormal;
        if (tex != IntPtr.Zero)
            renderer.DrawTexture(tex, (int)ScreenX, (int)ScreenY,
                                 (int)ScreenWidth, (int)ScreenHeight);
    }
}