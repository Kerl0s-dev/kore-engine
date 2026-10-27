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
        public IntPtr Texture;
        public string TexturePath = "";
        public Color Color = new Color(255, 255, 255, 255);

        // Taille d'une frame sur la texture (en PIXELS)
        public Vector2 FrameSize = new Vector2(16, 16);

        public int Frames, Columns;

        Vector2 Size = Vector2.One; // Taille en METRES (pour le rendu)

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

            yield return new ColorField
            {
                Label = "Color",
                Get = () => Color,
                Set = c => Color = c
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

            if (camera != null)
                renderer.DrawTexture(Texture, x, y, w, h, Color, FlipX, FlipY, angle, camera);
            else
                renderer.DrawTexture(Texture, x, y, w, h, Color, FlipX, FlipY, angle);
        }
    }
}