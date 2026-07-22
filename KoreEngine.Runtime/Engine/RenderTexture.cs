using SDL3;

namespace KoreEngine.Engine
{
    /// <summary>
    /// Render-to-texture pour le viewport éditeur.
    ///
    /// REWRITE : BeginRender/EndRender flush désormais le renderer ET réinitialisent
    /// le clip rect avant ET après chaque switch de target. Avant ce fix :
    /// - un clip rect laissé par un draw précédent (ou posé par ImGui via
    ///   SetRenderClipRect) pouvait "fuiter" sur la texture suivante,
    /// - un switch de target sans flush préalable pouvait faire atterrir des
    ///   commandes encore en attente dans le batch sur la mauvaise cible.
    /// C'était la source des glitches visuels observés au changement de RenderTarget.
    /// </summary>
    public class RenderTexture
    {
        private readonly Renderer renderer;
        public IntPtr Texture { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public RenderTexture(Renderer renderer, int width, int height)
        {
            this.renderer = renderer;
            Create(width, height);
        }

        private void Create(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);

            Texture = SDL.CreateTexture(renderer.Handle, SDL.PixelFormat.RGBA8888,
                                         SDL.TextureAccess.Target, Width, Height);
            SDL.SetTextureBlendMode(Texture, SDL.BlendMode.Blend);
            SDL.SetTextureScaleMode(Texture, SDL.ScaleMode.Nearest);
        }

        public void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            if (width == Width && height == Height) return;

            // Une texture ne doit jamais être détruite pendant qu'elle est encore
            // bound comme render target -> on flush et on débind avant.
            renderer.Flush();
            SDL.SetRenderTarget(renderer.Handle, IntPtr.Zero);

            SDL.DestroyTexture(Texture);
            Create(width, height);
        }

        public void BeginRender()
        {
            renderer.Flush();             // vide tout ce qui était en attente sur la cible précédente
            renderer.ResetClipRect();     // évite qu'un clip rect précédent s'applique à cette texture
            SDL.SetRenderTarget(renderer.Handle, Texture);
            SDL.SetRenderDrawColor(renderer.Handle, 0, 0, 0, 0);
            SDL.RenderClear(renderer.Handle); // fond transparent (pas noir opaque) pour composer proprement dans ImGui
        }

        public void EndRender()
        {
            renderer.Flush();             // soumet tous les draws faits sur la texture avant de la débinder
            renderer.ResetClipRect();
            SDL.SetRenderTarget(renderer.Handle, IntPtr.Zero); // retour à l'écran réel
        }

        public void Destroy()
        {
            if (Texture != IntPtr.Zero)
            {
                SDL.DestroyTexture(Texture);
                Texture = IntPtr.Zero;
            }
        }
    }
}
