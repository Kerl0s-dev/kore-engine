using SDL3;

namespace KoreEngine
{
    public class Renderer
    {
        public IntPtr Handle { get; private set; }

        public bool AutoFlush = true;

        public static float PixelsPerUnit = 100f;   // 1 unité = 100 pixels

        public Renderer(IntPtr window)
        {
            Handle = SDL.CreateRenderer(window, null);
        }

        public void Clear(Color? color)
        {
            if (color != null)
                SDL.SetRenderDrawColor(Handle, (byte)color.R, (byte)color.G, (byte)color.B, 255);
            else
                SDL.SetRenderDrawColor(Handle, 0, 0, 0, 255);
            SDL.RenderClear(Handle);
        }

        public void Present()
        {
            SDL.FlushRenderer(Handle);
            SDL.RenderPresent(Handle);
        }

        public void Flush() => SDL.FlushRenderer(Handle);

        public void ResetClipRect() => SDL.SetRenderClipRect(Handle, IntPtr.Zero);

        private void AutoFlushIfEnabled()
        {
            if (AutoFlush) SDL.FlushRenderer(Handle);
        }

        // ---------------------------------------------------------------
        // Rectangles
        // ---------------------------------------------------------------

        public void DrawRectOutline(float x, float y, float w, float h, byte r, byte g, byte b, byte a)
        {
            SDL.SetRenderDrawBlendMode(Handle, SDL.BlendMode.Blend);
            SDL.SetRenderDrawColor(Handle, r, g, b, a);
            var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
            SDL.RenderRect(Handle, rect);
            AutoFlushIfEnabled();
        }

        public void DrawRectOutline(float x, float y, float w, float h, byte r, byte g, byte b, byte a, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawRectOutline(
                screen.X, screen.Y,
                w * PixelsPerUnit * camera.Zoom,
                h * PixelsPerUnit * camera.Zoom,
                r, g, b, a);
        }

        public void DrawRect(float x, float y, float w, float h, Color color, byte a)
        {
            SDL.SetRenderDrawBlendMode(Handle, SDL.BlendMode.Blend);
            SDL.SetRenderDrawColor(Handle, (byte)color.R, (byte)color.G, (byte)color.B, a);
            var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
            SDL.RenderFillRect(Handle, rect);
            AutoFlushIfEnabled();
        }

        public void DrawRect(float x, float y, float w, float h, Color color, byte a, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawRect(
                screen.X, screen.Y,
                w * PixelsPerUnit * camera.Zoom,
                h * PixelsPerUnit * camera.Zoom,
                color, a);
        }

        // ---------------------------------------------------------------
        // Textures
        // ---------------------------------------------------------------

        public IntPtr LoadTexture(string path)
        {
            var surface = nint.Zero;

            switch (Path.GetExtension(path))
            {
                case ".png":
                    surface = SDL.LoadPNG(path);
                    break;

                case ".bmp":
                    surface = SDL.LoadBMP(path);
                    break;
            }

            if (surface == IntPtr.Zero)
                throw new Exception($"Impossible de charger la texture: {path} ({SDL.GetError()})");

            var texture = SDL.CreateTextureFromSurface(Handle, surface);
            SDL.DestroySurface(surface);
            SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
            return texture;
        }

        public void DrawTexture(IntPtr texture, float x, float y, float w, float h, Color color, bool flipX = false, bool flipY = false, double angle = 0.0, Camera? camera = null)
        {
            SDL.SetTextureColorMod(texture, (byte)color.R, (byte)color.G, (byte)color.B);
            SDL.SetTextureAlphaMod(texture, (byte)color.A);

            double finalAngle = angle;
            float destW = w;
            float destH = h;
            float centerX = x;
            float centerY = y;

            if (camera != null)
            {
                // 1. Transforme le centre du monde vers l'écran
                var screenCenter = camera.WorldToScreen(new Vector2(centerX, centerY));
                centerX = screenCenter.X;
                centerY = screenCenter.Y;

                // 2. Met à l'échelle en pixels
                destW = w * PixelsPerUnit * camera.Zoom;
                destH = h * PixelsPerUnit * camera.Zoom;

                // 3. Combine l'angle avec la rotation de la caméra
                finalAngle += camera.Rotation;
            }

            // 4. Calcule le rectangle destination en centrant autour du point converti
            var dest = new SDL.FRect
            {
                X = centerX - destW * 0.5f,
                Y = centerY - destH * 0.5f,
                W = destW,
                H = destH
            };

            var flip = (flipX ? SDL.FlipMode.Horizontal : SDL.FlipMode.None)
                     | (flipY ? SDL.FlipMode.Vertical : SDL.FlipMode.None);

            SDL.RenderTextureRotated(Handle, texture, IntPtr.Zero, dest, finalAngle, IntPtr.Zero, flip);

            SDL.SetTextureColorMod(texture, 255, 255, 255);
            SDL.SetTextureAlphaMod(texture, 255);

            AutoFlushIfEnabled();
            if (!AutoFlush) Flush();
        }

        public void DrawTexturePart(IntPtr texture, Rectangle src, float x, float y, float w, float h, Color color, bool flipX = false, bool flipY = false, double angle = 0.0, Camera? camera = null)
        {
            SDL.SetTextureColorMod(texture, (byte)color.R, (byte)color.G, (byte)color.B);
            SDL.SetTextureAlphaMod(texture, (byte)color.A);

            double finalAngle = angle;
            float destW = w;
            float destH = h;
            float centerX = x;
            float centerY = y;

            if (camera != null)
            {
                var screenCenter = camera.WorldToScreen(new Vector2(centerX, centerY));
                centerX = screenCenter.X;
                centerY = screenCenter.Y;

                destW = w * PixelsPerUnit * camera.Zoom;
                destH = h * PixelsPerUnit * camera.Zoom;
                finalAngle += camera.Rotation;
            }

            var srcRect = new SDL.FRect { X = src.X, Y = src.Y, W = src.Width, H = src.Height };
            var dest = new SDL.FRect
            {
                X = centerX - destW * 0.5f,
                Y = centerY - destH * 0.5f,
                W = destW,
                H = destH
            };

            var flip = (flipX ? SDL.FlipMode.Horizontal : SDL.FlipMode.None)
                     | (flipY ? SDL.FlipMode.Vertical : SDL.FlipMode.None);

            SDL.RenderTextureRotated(Handle, texture, srcRect, dest, finalAngle, IntPtr.Zero, flip);

            SDL.SetTextureColorMod(texture, 255, 255, 255);
            SDL.SetTextureAlphaMod(texture, 255);

            AutoFlushIfEnabled();
            if (!AutoFlush) Flush();
        }

        // ---------------------------------------------------------------
        // Lines
        // ---------------------------------------------------------------

        public void DrawLine(float x1, float y1, float x2, float y2, byte r, byte g, byte b, byte a)
        {
            SDL.SetRenderDrawBlendMode(Handle, SDL.BlendMode.Blend);
            SDL.SetRenderDrawColor(Handle, r, g, b, a);
            SDL.RenderLine(Handle, x1, y1, x2, y2);
            AutoFlushIfEnabled();
        }

        public void WorldLine(Camera cam, Vector2 p, Vector2 q, byte r, byte g, byte b, byte a)
        {
            var s0 = cam.WorldToScreen(p);
            var s1 = cam.WorldToScreen(q);
            DrawLine(s0.X, s0.Y, s1.X, s1.Y, r, g, b, a);
        }
    }
}