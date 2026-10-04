using SDL3;

namespace KoreEngine.Hub.Ui;

/// <summary>Codes de touches SDL3 (valeurs SDLK_*), pour ne pas dépendre des noms d'enum du binding.</summary>
public static class UiKeys
{
    public const uint Backspace = 0x08;
    public const uint Tab = 0x09;
    public const uint Enter = 0x0D;
    public const uint Escape = 0x1B;
    public const uint Delete = 0x7F;
    public const uint A = 0x61;
    public const uint V = 0x76;
    public const uint Home = 0x4000004A;
    public const uint End = 0x4000004D;
    public const uint Right = 0x4000004F;
    public const uint Left = 0x40000050;
}

public readonly record struct KeyPress(uint Key, bool Ctrl);

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Text = new(235, 235, 235);
    public static readonly Rgba TextDim = new(130, 130, 130);
    public static readonly Rgba Accent = new(52, 120, 246);
    #region Buttons
    /* Primary */
    public static readonly Rgba PrimaryButton = new(46, 108, 222);
    public static readonly Rgba PrimaryButtonHover = new(64, 128, 245);
    public static readonly Rgba PrimaryButtonPressed = new(36, 88, 185);
    public static readonly Rgba PrimaryButtonDisabled = new(38, 52, 80);
    public static readonly Rgba PrimaryBorder = new(36, 88, 185);
    /* Secondary */
    public static readonly Rgba SecondaryButton = new(58, 58, 58);
    public static readonly Rgba SecondaryButtonHover = new(76, 76, 76);
    public static readonly Rgba SecondaryButtonPressed = new(44, 44, 44);
    public static readonly Rgba SecondaryButtonDisabled = new(42, 42, 42);
    public static readonly Rgba SecondaryBorder = new(80, 80, 80);
    #endregion
}

/// <summary>Souris interrogée une fois par frame (pas besoin de structs d'événements).</summary>
public sealed class UiInput
{
    bool previousDown;

    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public bool Down { get; private set; }

    /// <summary>Défilement vertical de la molette cette frame (positif = vers le haut).</summary>
    public float WheelY { get; private set; }
    float pendingWheel;

    // Texte tapé et touches pressées depuis la dernière frame (alimentés par HubWindow).
    readonly System.Text.StringBuilder pendingText = new();
    readonly List<KeyPress> pendingKeys = new();
    readonly List<KeyPress> keys = new();

    public string TypedText { get; private set; } = "";
    public IReadOnlyList<KeyPress> Keys => keys;

    public void AddText(string text) => pendingText.Append(text);
    public void AddKey(uint key, bool ctrl) => pendingKeys.Add(new KeyPress(key, ctrl));

    /// <summary>Appelé depuis la boucle d'événements (MouseWheel).</summary>
    public void AddWheel(float y) => pendingWheel += y;

    /// <summary>Le bouton gauche vient d'être enfoncé (cette frame).</summary>
    public bool Pressed => Down && !previousDown;

    /// <summary>Le bouton gauche vient d'être relâché (cette frame).</summary>
    public bool Released => !Down && previousDown;

    public void Update()
    {
        previousDown = Down;

        var flags = SDL.GetMouseState(out float x, out float y);
        MouseX = x;
        MouseY = y;
        Down = ((uint)flags & 1u) != 0; // bit 0 = bouton gauche

        WheelY = pendingWheel;
        pendingWheel = 0;

        TypedText = pendingText.ToString();
        pendingText.Clear();
        keys.Clear();
        keys.AddRange(pendingKeys);
        pendingKeys.Clear();
    }
}

/// <summary>Tout ce dont un widget a besoin pour se mettre à jour et se dessiner.</summary>
public sealed class UiContext
{
    public IntPtr Renderer { get; }
    public TextCache Text { get; }
    public ImageCache Images { get; }
    public UiInput Input { get; } = new();

    /// <summary>Widget qui reçoit le clavier (un champ de texte), ou null.</summary>
    public Widget? Focus { get; set; }

    /// <summary>Taille de la fenêtre, mise à jour par HubWindow à chaque frame.</summary>
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }

    public UiContext(IntPtr renderer, TextCache text, ImageCache images)
    {
        Renderer = renderer;
        Text = text;
        Images = images;
    }

    public void FillRect(float x, float y, float w, float h, Rgba c)
    {
        SDL.SetRenderDrawBlendMode(Renderer, SDL.BlendMode.Blend);
        SDL.SetRenderDrawColor(Renderer, c.R, c.G, c.B, c.A);
        var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
        SDL.RenderFillRect(Renderer, rect);
    }

    public void OutlineRect(float x, float y, float w, float h, Rgba c)
    {
        SDL.SetRenderDrawBlendMode(Renderer, SDL.BlendMode.Blend);
        SDL.SetRenderDrawColor(Renderer, c.R, c.G, c.B, c.A);
        var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
        SDL.RenderRect(Renderer, rect);
    }

    /// <summary>Limite le dessin à ce rectangle (jusqu'au prochain PopClip).</summary>
    public void PushClip(float x, float y, float w, float h)
    {
        var rect = new SDL.Rect { X = (int)x, Y = (int)y, W = (int)w, H = (int)h };
        SDL.SetRenderClipRect(Renderer, in rect);
    }

    /// <summary>Retire le clip (on le remet à la fenêtre entière).</summary>
    public void PopClip()
    {
        var rect = new SDL.Rect { X = 0, Y = 0, W = WindowWidth, H = WindowHeight };
        SDL.SetRenderClipRect(Renderer, in rect);
    }
}