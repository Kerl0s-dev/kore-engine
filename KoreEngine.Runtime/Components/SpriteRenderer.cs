using KoreEngine.Core;
using KoreEngine.Engine;
using SDL3;

namespace KoreEngine.Components;

public enum FilteringMode
{
    Nearest = 0,
    PixelArt = 1,
    Linear = 2,
}

public class SpriteRenderer : Component
{
    public AnimationStateMachine? StateMachine = new();

    public IntPtr Texture;
    public string TexturePath = "";
    public Vector2 Size = new Vector2(16,16);
    public Vector2 FrameSize = new Vector2(16, 16);
    public bool FlipX = false;
    public bool FlipY = false;
    public FilteringMode Filtering = FilteringMode.Nearest;

    static readonly string[] FilteringNames =
        Enum.GetNames<FilteringMode>();

    static SDL.ScaleMode ToSdlScaleMode(FilteringMode mode) => mode switch
    {
        FilteringMode.Nearest => SDL.ScaleMode.Nearest,
        FilteringMode.PixelArt => SDL.ScaleMode.PixelArt,
        _ => SDL.ScaleMode.Linear
    };

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new TextureField
        {
            Label = "Texture",
            Get = () => (Texture, TexturePath),
            Set = (tex, path) => { Texture = tex; TexturePath = path; }
        };

        yield return new IntField
        {
            Label = "Width", Get = () => (int)Size.X, Set = v => Size.X = v,
            Min = 1, Max = 4096
        };
        yield return new IntField
        {
            Label = "Height", Get = () => (int)Size.Y, Set = v => Size.Y = v,
            Min = 1, Max = 4096
        };

        yield return new IntField
        {
            Label = "Texture Width", Get = () => (int)FrameSize.X, Set = v => FrameSize.X = v,
            Min = 1, Max = 4096
        };
        yield return new IntField
        {
            Label = "Texture Height", Get = () => (int)FrameSize.Y, Set = v => FrameSize.Y = v,
            Min = 1, Max = 4096
        };

        yield return new BoolField { Label = "Flip X", Get = () => FlipX, Set = v => FlipX = v };
        yield return new BoolField { Label = "Flip Y", Get = () => FlipY, Set = v => FlipY = v };

        yield return new EnumField
        {
            Label = "Filtering",
            Names = FilteringNames,
            Get = () => (int)Filtering,
            Set = idx =>
            {
                Filtering = (FilteringMode)idx;
                // Applique immédiatement si la texture est déjà chargée
                if (Texture != IntPtr.Zero)
                    SDL.SetTextureScaleMode(Texture, ToSdlScaleMode(Filtering));
            }
        };
    }

    public override void Update(float dt)
    {
        StateMachine?.Update(dt);
    }

    public override void Render(Renderer renderer, Camera? camera)
    {
        if (Texture == IntPtr.Zero) return;

        SDL.SetTextureScaleMode(Texture, ToSdlScaleMode(Filtering));

        var pos = Owner.WorldPosition;
        var scale = Owner.WorldScale;
        int w = (int)(Size.X * scale.X);
        int h = (int)(Size.Y * scale.Y);
        int x = (int)(pos.X - w / 2f);
        int y = (int)(pos.Y + h / 2f);
        double angle = Owner.WorldRotation;

        if (StateMachine != null)
        {
            var frame = StateMachine.CurrentFrame;
            frame.SourceRect = new Rectangle(0, 0, (int)FrameSize.X, (int)FrameSize.Y);

            if (camera != null)
                renderer.DrawTexturePart(Texture, frame.SourceRect, x, y, w, h, FlipX, FlipY, angle, camera);
            else
                renderer.DrawTexturePart(Texture, frame.SourceRect, x, y, w, h, FlipX, FlipY, angle);
        }
        else
        {
            Logger.Warning("State Machine not assigned.");
            if (camera != null)
                renderer.DrawTexture(Texture, x, y, w, h, FlipX, FlipY, angle, camera);
            else
                renderer.DrawTexture(Texture, x, y, w, h, FlipX, FlipY, angle);
        }
    }
}