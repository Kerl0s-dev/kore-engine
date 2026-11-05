using System.Drawing;

namespace KoreEngine
{
    public class Camera : Component
    {
        [Range(1, 10)] public float Zoom = 1f;

        protected float _rotation;
        public virtual float Rotation
        {
            get => gameObject != null ? gameObject.transform.WorldRotation : _rotation;
            set
            {
                if (gameObject != null) gameObject.transform.Rotation = value;
                else _rotation = value;
            }
        }

        [HideInInspector] public int ViewWidth;
        [HideInInspector] public int ViewHeight;

        public Color Color;

        protected Vector2 _position;

        public virtual Vector2 Position
        {
            get => gameObject != null ? gameObject.transform.WorldPosition : _position;
            set
            {
                if (gameObject != null) gameObject.transform.Position = value;
                else _position = value;
            }
        }

        static Camera? main;
        public static Camera Main
        {
            get => main!;
            internal set => main = value;
        }

        public Camera() : this(1920, 1080, Color.Black) { }
        public Camera(int viewWidth = 1920, int viewHeight = 1080, Color? backgroundColor = null)
        {
            ViewWidth = viewWidth;
            ViewHeight = viewHeight;
            Color = backgroundColor ?? Color.Black;
        }

        public Vector2 WorldToScreen(Vector2 worldPosMeters)
        {
            // 1. Convertit la distance en mètres par rapport à la caméra en pixels
            float deltaX = (worldPosMeters.X - Position.X) * Renderer.PixelsPerUnit;
            float deltaY = (Position.Y - worldPosMeters.Y) * Renderer.PixelsPerUnit; // Y inversé

            // 2. Applique la rotation de la caméra
            float rad = Rotation * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            float rotatedX = deltaX * cos - deltaY * sin;
            float rotatedY = deltaX * sin + deltaY * cos;

            // 3. Applique le zoom et centre sur l'écran
            return new Vector2(
                rotatedX * Zoom + ViewWidth * 0.5f,
                rotatedY * Zoom + ViewHeight * 0.5f
            );
        }

        public Vector2 ScreenToWorld(Vector2 screenPosPixels)
        {
            // 1. Retire le centrage et annule le zoom
            float pixelOffsetX = (screenPosPixels.X - ViewWidth * 0.5f) / Zoom;
            float pixelOffsetY = (screenPosPixels.Y - ViewHeight * 0.5f) / Zoom;

            // 2. Annule la rotation de la caméra (matrice inverse)
            float rad = Rotation * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            float worldPixelX = pixelOffsetX * cos + pixelOffsetY * sin;
            float worldPixelY = -pixelOffsetX * sin + pixelOffsetY * cos;

            // 3. Convertit les pixels en mètres et ajoute la position de la caméra
            float worldX = Position.X + (worldPixelX / Renderer.PixelsPerUnit);
            float worldY = Position.Y - (worldPixelY / Renderer.PixelsPerUnit);

            return new Vector2(worldX, worldY);
        }

        public RectangleF BoundsMeters
        {
            get
            {
                if (MathF.Abs(Rotation) < 0.0001f)
                {
                    return new RectangleF(
                        Position.X - (ViewWidth * 0.5f / Zoom) / Renderer.PixelsPerUnit,
                        Position.Y - (ViewHeight * 0.5f / Zoom) / Renderer.PixelsPerUnit,
                        (ViewWidth / Zoom) / Renderer.PixelsPerUnit,
                        (ViewHeight / Zoom) / Renderer.PixelsPerUnit
                    );
                }

                // Si la caméra est pivotée, projette les 4 coins
                Vector2 tl = ScreenToWorld(new Vector2(0, 0));
                Vector2 tr = ScreenToWorld(new Vector2(ViewWidth, 0));
                Vector2 bl = ScreenToWorld(new Vector2(0, ViewHeight));
                Vector2 br = ScreenToWorld(new Vector2(ViewWidth, ViewHeight));

                float minX = MathF.Min(MathF.Min(tl.X, tr.X), MathF.Min(bl.X, br.X));
                float maxX = MathF.Max(MathF.Max(tl.X, tr.X), MathF.Max(bl.X, br.X));
                float minY = MathF.Min(MathF.Min(tl.Y, tr.Y), MathF.Min(bl.Y, br.Y));
                float maxY = MathF.Max(MathF.Max(tl.Y, tr.Y), MathF.Max(bl.Y, br.Y));

                return new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
        }
    }
}