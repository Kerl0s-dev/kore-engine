using SDL3;

namespace KoreEngine.UI;

public enum ButtonType
{
    Color,
    Image
}

public class UIButton : UIElement
{
    public ButtonType ButtonType = ButtonType.Image;

    public IntPtr TextureNormal;
    public IntPtr TextureHover;
    public IntPtr TexturePressed;

    public string TextureNormalPath = "";
    public string TextureHoverPath = "";
    public string TexturePressedPath = "";

    public Color NormalColor = new Color(255, 255, 255, 255);
    public Color HoverColor = new Color(200, 200, 200, 255);
    public Color PressedColor = new Color(150, 150, 150, 255);

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
        //if (tex != IntPtr.Zero) renderer.DrawTexture(tex, (int)ScreenX, (int)ScreenY, (int)ScreenWidth, (int)ScreenHeight);

        if (ButtonType == ButtonType.Color)
        {
            Color color = hovered ? (pressed ? PressedColor : HoverColor) : NormalColor;
            renderer.DrawTexture(tex, (int)ScreenX, (int)ScreenY, (int)ScreenWidth, (int)ScreenHeight, color);
        }
        else if (ButtonType == ButtonType.Image)
        {
            if (tex != IntPtr.Zero)
                renderer.DrawTexture(tex, (int)ScreenX, (int)ScreenY, (int)ScreenWidth, (int)ScreenHeight, Color.White);
        }
    }

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new EnumField
        {
            Label = "Button Type",
            Names = Enum.GetNames<ButtonType>(),
            Get = () => (int)ButtonType,
            Set = v => { ButtonType = (ButtonType)v; }
        };

        if (ButtonType == ButtonType.Image)
        {
            yield return new TextureField
            {
                Label = "Normal Texture",
                Get = () => (TextureNormal, TextureNormalPath),
                Set = (tex, path) => { TextureNormal = tex; TextureNormalPath = path; }
            };
            yield return new TextureField
            {
                Label = "Hover Texture",
                Get = () => (TextureHover, TextureHoverPath),
                Set = (tex, path) => { TextureHover = tex; TextureHoverPath = path; }
            };
            yield return new TextureField
            {
                Label = "Pressed Texture",
                Get = () => (TexturePressed, TexturePressedPath),
                Set = (tex, path) => { TexturePressed = tex; TexturePressedPath = path; }
            };
        }
        else if (ButtonType == ButtonType.Color)
        {
            yield return new TextureField
            {
                Label = "Normal Texture",
                Get = () => (TextureNormal, TextureNormalPath),
                Set = (tex, path) => { TextureNormal = tex; TextureNormalPath = path; }
            };
            yield return new ColorField
            {
                Label = "Normal Color",
                Get = () => NormalColor,
                Set = v => NormalColor = v
            };
            yield return new ColorField
            {
                Label = "Hover Color",
                Get = () => HoverColor,
                Set = v => HoverColor = v
            };
            yield return new ColorField
            {
                Label = "Pressed Color",
                Get = () => PressedColor,
                Set = v => PressedColor = v
            };
        }

        yield return new KoreEventField
        {
            Label = "On Click",
            Get = () => OnClick
        };
    }
}