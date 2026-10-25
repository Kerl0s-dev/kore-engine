using SDL3;

namespace KoreEngine
{
    public enum FilteringMode
    {
        Nearest = 0,
        PixelArt = 1,
        Linear = 2
    }

    public class SpriteRenderer : Component
    {
        public AnimationStateMachine? StateMachine = new();

        public IntPtr Texture;
        public string TexturePath = "";

        // Taille du sprite dans le monde (en MÈTRES)
        public Vector2 Size = new Vector2(1.0f, 1.0f);

        // Taille d'une frame sur la texture (en PIXELS)
        public Vector2 FrameSize = new Vector2(16, 16);

        public bool FlipX = false;
        public bool FlipY = false;
        public FilteringMode Filtering = FilteringMode.Linear;

        static readonly string[] FilteringNames = Enum.GetNames<FilteringMode>();

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

            // Taille en mètres (FloatField au lieu d'IntField)
            yield return new FloatField
            {
                Label = "Width (m)",
                Get = () => Size.X,
                Set = v => Size.X = v,
                Value = 0.01f,
                Min = 0.01f,
                Max = 100f
            };
            yield return new FloatField
            {
                Label = "Height (m)",
                Get = () => Size.Y,
                Set = v => Size.Y = v,
                Value = 0.01f,
                Min = 0.01f,
                Max = 100f
            };

            // Frame size reste en pixels (pour découper la texture)
            yield return new IntField
            {
                Label = "Frame Width (px)",
                Get = () => (int)FrameSize.X,
                Set = v => FrameSize.X = v,
                Min = 1,
                Max = 4096
            };
            yield return new IntField
            {
                Label = "Frame Height (px)",
                Get = () => (int)FrameSize.Y,
                Set = v => FrameSize.Y = v,
                Min = 1,
                Max = 4096
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

            // Position et échelle monde (en MÈTRES)
            var pos = gameObject.transform.WorldPosition;
            var scale = gameObject.transform.WorldScale;

            // Dimensions finales en mètres
            float w = Size.X * scale.X;
            float h = Size.Y * scale.Y;

            // Calcul du coin supérieur/gauche en mètres (si l'origine du sprite est au centre)
            float x = pos.X - w * 0.5f;
            float y = pos.Y + h * 0.5f; // Ajuste selon ton orientation Y (haut/bas)

            double angle = -gameObject.transform.WorldRotation;

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
}