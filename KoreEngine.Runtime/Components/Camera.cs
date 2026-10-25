using System.Drawing;

namespace KoreEngine
{
    public class Camera : Component
    {
        [Range(1, 10)] public float Zoom = 1f;

        [HideInInspector] public int ViewWidth;
        [HideInInspector] public int ViewHeight;

        public Color Color;

        // Position interne — utilisée uniquement quand Owner == null (EditorCamera).
        protected Vector2 _position;

        /// <summary>
        /// Position monde de la caméra (en MÈTRES).
        /// </summary>
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

            if (backgroundColor == null) backgroundColor = Color.Black;
            this.Color = backgroundColor;
        }

        public Vector2 WorldToScreen(Vector2 worldPosMeters)
        {
            // 1. Convertit la distance en mètres par rapport à la caméra en pixels
            float deltaX = (worldPosMeters.X - Position.X) * Renderer.PixelsPerUnit;
            float deltaY = (Position.Y - worldPosMeters.Y) * Renderer.PixelsPerUnit; // Y inversé (Monde Y-haut → Écran Y-bas)

            // 2. Applique le zoom et centre sur l'écran
            return new Vector2(
                deltaX * Zoom + ViewWidth * 0.5f,
                deltaY * Zoom + ViewHeight * 0.5f
            );
        }

        public Vector2 ScreenToWorld(Vector2 screenPosPixels)
        {
            // 1. Retire le centrage et annule le zoom pour revenir aux pixels relatifs
            float pixelOffsetX = (screenPosPixels.X - ViewWidth * 0.5f) / Zoom;
            float pixelOffsetY = (screenPosPixels.Y - ViewHeight * 0.5f) / Zoom;

            // 2. Convertit les pixels en mètres et ajoute la position de la caméra
            float worldX = Position.X + (pixelOffsetX / Renderer.PixelsPerUnit);
            float worldY = Position.Y - (pixelOffsetY / Renderer.PixelsPerUnit); // Inversion Y symétrique

            return new Vector2(worldX, worldY);
        }

        // Si ton moteur utilise des rectangles en float pour le monde (recommandé avec les mètres) :
        public RectangleF BoundsMeters => new RectangleF(
            Position.X - (ViewWidth * 0.5f / Zoom) / Renderer.PixelsPerUnit,
            Position.Y - (ViewHeight * 0.5f / Zoom) / Renderer.PixelsPerUnit,
            (ViewWidth / Zoom) / Renderer.PixelsPerUnit,
            (ViewHeight / Zoom) / Renderer.PixelsPerUnit
        );
    }
}