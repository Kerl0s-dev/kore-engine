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

        public int Frame = -1;                    // -1 = texture entière ; >= 0 : frame de la grille FrameSize (ligne par ligne)
        public Color Color = Color.White;         // teinte et opacité (0-255)

        /// <summary>Nombre de colonnes de la grille de frames (largeur de texture / FrameSize.X).</summary>
        public int Columns
        {
            get
            {
                if (Texture == IntPtr.Zero) return 1;
                SDL.GetTextureSize(Texture, out float tw, out _);
                return Math.Max(1, (int)(tw / Math.Max(1f, FrameSize.X)));
            }
        }


        // Taille d'une frame sur la texture (en pixels)
        public Vector2 FrameSize = new Vector2(16, 16);

        Vector2 Size = Vector2.One; // Taille en mètres (pour le rendu)

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
            yield return new TextureField { Label = "Texture", Get = () => (Texture, TexturePath), Set = (tex, path) => { Texture = tex; TexturePath = path; } };

            yield return new ColorField { Label = "Color", Get = () => Color, Set = c => Color = c };

            yield return new Bool2Field { Label = "Flip", GetFirst = () => FlipX, GetSecond = () => FlipY, Set =  (x, y) =>  { FlipX = x; FlipY = y; } };

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

            yield return new Vector2Field { Label = "Frame Size (px)", Get = () => FrameSize, Set = v => FrameSize = v, Min = 1, Max = float.MaxValue };

            yield return new IntField { Label = "Frame", Get = () => Frame, Set = v => Frame = v, Min = -1, Max = int.MaxValue };
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

            // Calcul du centre en mètres
            float x = pos.X;
            float y = pos.Y;

            double angle = -gameObject.transform.WorldRotation;

            if (Frame >= 0)
            {
                int fw = Math.Max(1, (int)FrameSize.X), fh = Math.Max(1, (int)FrameSize.Y);
                int cols = Columns;
                var src = new Rectangle((Frame % cols) * fw, (Frame / cols) * fh, fw, fh);

                if (camera != null) renderer.DrawTexturePart(Texture, src, x, y, w, h, Color, FlipX, FlipY, angle, camera);
                else renderer.DrawTexturePart(Texture, src, x, y, w, h, Color, FlipX, FlipY, angle);
            }
            else {
                if (camera != null)
                    renderer.DrawTexture(Texture, x, y, w, h, Color, FlipX, FlipY, angle, camera);
                else
                    renderer.DrawTexture(Texture, x, y, w, h, Color, FlipX, FlipY, angle);
            }
        }
    }
}