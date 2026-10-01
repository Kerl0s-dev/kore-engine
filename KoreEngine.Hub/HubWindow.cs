using SDL3;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Media;

namespace KoreEngine.Hub
{
    class HubWindow
    {
        IntPtr window;
        public IntPtr WindowHandle;

        public string Title = "";
        public int Width = 800;
        public int Height = 600;

        public static bool Running = false;

        public HubWindow(string title, int width, int height) {
            Title = title;
            Width = width;
            Height = height;

            SDL.Init(SDL.InitFlags.Video);
            window = SDL.CreateWindow(Title, Width, Height, SDL.WindowFlags.Resizable);
            SDL.StartTextInput(window);

            WindowHandle = SDL.CreateRenderer(window, null);
        }

        public void Clear()
        {
            SDL.SetRenderDrawColor(WindowHandle, 0, 0, 0, 255);
            SDL.RenderClear(WindowHandle);
        }

        public void Present()
        {
            // Sécurité : garantit qu'aucun draw de la frame ne reste en attente
            // avant le swap (coût nul puisque déjà flush au fil de l'eau).
            // NOM À VÉRIFIER : la fonction SDL3 réelle est SDL_FlushRenderer ;
            // dans ce binding PascalCase ça devrait être SDL.FlushRenderer.
            // Si IntelliSense ne la trouve pas, cherche "Flush" dans le wiki SDL3-CS.
            SDL.FlushRenderer(WindowHandle);
            SDL.RenderPresent(WindowHandle);
        }

        static void Main(string[] args) {
            HubWindow wnd = new HubWindow("Title", 1200, 600);

            Running = true;

            while (Running)
            {
                while (SDL.PollEvent(out var e))
                {
                    // Affiche un popup de confirmation de l'action avant de quitter
                    switch (e.Type)
                    {
                        case (uint)SDL.EventType.Quit:
                        case (uint)SDL.EventType.WindowCloseRequested:
                            Running = false; // Arrête l'application
                            break;

                        case (uint)SDL.EventType.KeyDown:
                            Debug.WriteLine("Key pressed: " + e.Key.Key);
                            break;
                    }
                }

                wnd.Clear();
                wnd.Present();
            }
        }
    }
}
