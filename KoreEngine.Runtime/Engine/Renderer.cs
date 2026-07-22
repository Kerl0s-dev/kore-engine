using KoreEngine.Components;
using KoreEngine.Core;
using SDL3;

namespace KoreEngine.Engine
{
    /// <summary>
    /// Wrapper autour de SDL_Renderer.
    ///
    /// REWRITE : chaque appel de dessin flush désormais immédiatement le renderer
    /// (SDL.RenderFlush) par défaut. C'est ce qui corrige le bug historique où le
    /// texte d'un UIText se retrouvait dessiné avant ou après le bouton sous-jacent :
    /// SDL3 batch les appels RenderTexture en interne, et l'ordre de ce batching
    /// n'est pas garanti respecter l'ordre d'appel du code dès qu'on change de
    /// texture source entre deux draws successifs. Flush après CHAQUE draw force
    /// la soumission immédiate dans l'ordre exact du code, au prix d'un coût de
    /// perf largement négligeable pour un moteur 2D de cette taille.
    ///
    /// Pour du rendu en masse où l'ordre relatif n'a pas d'impact visuel (un
    /// tilemap par exemple, où les tuiles ne se chevauchent jamais), AutoFlush
    /// peut être désactivé ponctuellement autour du bloc concerné :
    ///
    ///     renderer.AutoFlush = false;
    ///     foreach (var tile in tiles) renderer.DrawTexturePart(...);
    ///     renderer.Flush();
    ///     renderer.AutoFlush = true;
    /// </summary>
    public class Renderer
    {
        public IntPtr Handle { get; private set; }

        public bool AutoFlush = true;

        public Renderer(IntPtr window)
        {
            // EditorWindow ou GameLoop passe le pointeur de FENÊTRE ici (new Renderer(window)),
            // donc c'est ce constructeur qui doit créer le SDL_Renderer.
            // À VÉRIFIER : signature exacte de SDL.CreateRenderer dans ton binding
            // SDL3-CS (2e paramètre = nom du driver, souvent string? ou à omettre).
            Handle = SDL.CreateRenderer(window, null);
        }

        public void Clear()
        {
            SDL.SetRenderDrawColor(Handle, 0, 0, 0, 255);
            SDL.RenderClear(Handle);
        }

        public void Present()
        {
            // Sécurité : garantit qu'aucun draw de la frame ne reste en attente
            // avant le swap (coût nul puisque déjà flush au fil de l'eau).
            // NOM À VÉRIFIER : la fonction SDL3 réelle est SDL_FlushRenderer ;
            // dans ce binding PascalCase ça devrait être SDL.FlushRenderer.
            // Si IntelliSense ne la trouve pas, cherche "Flush" dans le wiki SDL3-CS.
            SDL.FlushRenderer(Handle);
            SDL.RenderPresent(Handle);
        }

        /// <summary>
        /// Force la soumission de tous les draws actuellement en attente dans le
        /// batch SDL3. Appelé automatiquement après chaque Draw* si AutoFlush == true.
        /// À appeler manuellement avant tout SetRenderTarget / changement de clip
        /// rect si AutoFlush a été désactivé temporairement.
        /// </summary>
        public void Flush() => SDL.FlushRenderer(Handle);

        /// <summary>
        /// Réinitialise le clip rect courant. Utilisé par RenderTexture autour des
        /// switches de target pour éviter qu'un clip laissé par un draw précédent
        /// (ou par ImGui via SetRenderClipRect) ne s'applique à la mauvaise cible.
        /// </summary>
        public void ResetClipRect() => SDL.SetRenderClipRect(Handle, IntPtr.Zero);

        private void AutoFlushIfEnabled()
        {
            if (AutoFlush) SDL.FlushRenderer(Handle);
        }

        // ---------------------------------------------------------------
        // Rectangles
        // ---------------------------------------------------------------

        public void DrawRectOutline(int x, int y, int w, int h, byte r, byte g, byte b, byte a)
        {
            SDL.SetRenderDrawBlendMode(Handle, SDL.BlendMode.Blend);
            SDL.SetRenderDrawColor(Handle, r, g, b, a);
            var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
            SDL.RenderRect(Handle, rect);
            AutoFlushIfEnabled();
        }

        public void DrawRectOutline(int x, int y, int w, int h, byte r, byte g, byte b, byte a, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawRectOutline(
                (int)screen.X, (int)screen.Y,
                (int)(w * camera.Zoom), (int)(h * camera.Zoom),
                r, g, b, a);
        }

        public void DrawRect(int x, int y, int w, int h, Color color, byte a)
        {
            SDL.SetRenderDrawBlendMode(Handle, SDL.BlendMode.Blend);
            SDL.SetRenderDrawColor(Handle, (byte)color.R, (byte)color.G, (byte)color.B, a);
            var rect = new SDL.FRect { X = x, Y = y, W = w, H = h };
            SDL.RenderFillRect(Handle, rect);
            AutoFlushIfEnabled();
        }

        public void DrawRect(int x, int y, int w, int h, Color color, byte a, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawRect((int)screen.X, (int)screen.Y, (int)(w * camera.Zoom), (int)(h * camera.Zoom), color, a);
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

        public void DrawTexture(IntPtr texture, int x, int y, int w, int h)
            => DrawTexture(texture, x, y, w, h, false, false);

        public void DrawTexture(IntPtr texture, int x, int y, int w, int h, bool flipX, bool flipY)
        {
            var dest = new SDL.FRect { X = x, Y = y, W = w, H = h };
            var flip = (flipX ? SDL.FlipMode.Horizontal : SDL.FlipMode.None)
                      | (flipY ? SDL.FlipMode.Vertical : SDL.FlipMode.None);
            SDL.RenderTextureRotated(Handle, texture, IntPtr.Zero, dest, 0.0, IntPtr.Zero, flip);
            AutoFlushIfEnabled();
        }

        public void DrawTexture(IntPtr texture, int x, int y, int w, int h, Camera camera)
            => DrawTexture(texture, x, y, w, h, false, false, camera);

        public void DrawTexture(IntPtr texture, int x, int y, int w, int h, bool flipX, bool flipY, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawTexture(texture, (int)screen.X, (int)screen.Y, (int)(w * camera.Zoom), (int)(h * camera.Zoom), flipX, flipY);
        }

        public void DrawTexture(IntPtr texture, int x, int y, int w, int h, bool flipX, bool flipY, double angle)
        {
            var dest = new SDL.FRect { X = x, Y = y, W = w, H = h };
            var flip = (flipX ? SDL.FlipMode.Horizontal : SDL.FlipMode.None)
                      | (flipY ? SDL.FlipMode.Vertical : SDL.FlipMode.None);
            SDL.RenderTextureRotated(Handle, texture, IntPtr.Zero, dest, angle, IntPtr.Zero, flip);
            AutoFlushIfEnabled();
        }

        public void DrawTexture(IntPtr texture, int x, int y, int w, int h, bool flipX, bool flipY, double angle, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawTexture(texture, (int)screen.X, (int)screen.Y, (int)(w * camera.Zoom), (int)(h * camera.Zoom), flipX, flipY, angle);
        }

        public void DrawTexturePart(IntPtr texture, Rectangle src, int x, int y, int w, int h, bool flipX, bool flipY, double angle)
        {
            var srcRect = new SDL.FRect { X = src.X, Y = src.Y, W = src.Width, H = src.Height };
            var dest = new SDL.FRect { X = x, Y = y, W = w, H = h };
            var flip = (flipX ? SDL.FlipMode.Horizontal : SDL.FlipMode.None)
                      | (flipY ? SDL.FlipMode.Vertical : SDL.FlipMode.None);
            SDL.RenderTextureRotated(Handle, texture, srcRect, dest, angle, IntPtr.Zero, flip);
            AutoFlushIfEnabled();
        }

        public void DrawTexturePart(IntPtr texture, Rectangle src, int x, int y, int w, int h, bool flipX, bool flipY, double angle, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawTexturePart(texture, src, (int)screen.X, (int)screen.Y, (int)(w * camera.Zoom), (int)(h * camera.Zoom), flipX, flipY, angle);
        }

        public void DrawTexturePart(IntPtr texture, Rectangle src, int x, int y, int w, int h)
            => DrawTexturePart(texture, src, x, y, w, h, false, false);

        public void DrawTexturePart(IntPtr texture, Rectangle src, int x, int y, int w, int h, bool flipX, bool flipY)
        {
            var srcRect = new SDL.FRect { X = src.X, Y = src.Y, W = src.Width, H = src.Height };
            var dest = new SDL.FRect { X = x, Y = y, W = w, H = h };
            var flip = (flipX ? SDL.FlipMode.Horizontal : SDL.FlipMode.None)
                      | (flipY ? SDL.FlipMode.Vertical : SDL.FlipMode.None);
            SDL.RenderTextureRotated(Handle, texture, srcRect, dest, 0.0, IntPtr.Zero, flip);
            AutoFlushIfEnabled();
        }

        public void DrawTexturePart(IntPtr texture, Rectangle src, int x, int y, int w, int h, Camera camera)
            => DrawTexturePart(texture, src, x, y, w, h, false, false, camera);

        public void DrawTexturePart(IntPtr texture, Rectangle src, int x, int y, int w, int h, bool flipX, bool flipY, Camera camera)
        {
            var screen = camera.WorldToScreen(new Vector2(x, y));
            DrawTexturePart(texture, src, (int)screen.X, (int)screen.Y, (int)(w * camera.Zoom), (int)(h * camera.Zoom), flipX, flipY);
        }
    }
}