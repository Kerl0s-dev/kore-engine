using KoreEngine.Components;
using SDL3;

namespace KoreEngine.Engine;

/// <summary>
/// Boucle de jeu minimale pour un jeu buildé — zéro dépendance à ImGui ou à
/// l'Editor. Pas de mode édition, pas de caméra éditeur : la scène utilise
/// toujours sa propre Camera, et le rendu se fait directement à l'écran
/// (pas de RenderTexture intermédiaire comme dans l'éditeur). C'est ce que
/// consomme le Program.cs généré par ProjectCreator pour un jeu exporté.
/// </summary>
public class GameLoop
{
    IntPtr window;
    public IntPtr WindowHandle => window;

    public Renderer Renderer;

    public bool Running { get; set; } = true;

    public static string Title { get; set; } = "";
    public int Width { get; private set; }
    public int Height { get; private set; }

    ulong lastTicks;

    public GameLoop(string title, int width, int height)
    {
        Title = title;
        Width = width;
        Height = height;

        SDL.Init(SDL.InitFlags.Video);
        window = SDL.CreateWindow(title, width, height, SDL.WindowFlags.Resizable);
        SDL.StartTextInput(window);

        Renderer = new Renderer(window);
        TextureCache.Init(Renderer);

        lastTicks = SDL.GetTicks();
    }

    public void Run()
    {
        // Applique la scène de démarrage (chargée via SceneManager.LoadScene
        // AVANT Run(), donc encore en attente dans "next") avant d'appeler
        // Start() — sinon SceneManager.Current serait encore null ici et
        // Start() ne ferait rien du tout sur aucun composant.
        SceneManager.ApplyPendingScene();

        // Équivalent du premier "Play" en éditeur — ici la simulation
        // démarre immédiatement, il n'y a pas de mode édition à quitter.
        SceneManager.NotifyStart();

        while (Running)
        {
            // 1. Input
            InputManager.NewFrame();
            while (SDL.PollEvent(out var e))
            {
                switch (e.Type)
                {
                    case (uint)SDL.EventType.Quit:
                    case (uint)SDL.EventType.WindowCloseRequested:
                        Running = false;
                        break;
                }

                InputManager.HandleEvent(e);
            }

            // 2. Resize de la fenêtre réelle
            UpdateWindowSizeIfNeeded();

            // 3. dt
            float dt = ComputeDeltaTime();

            // 4. Applique la scène en attente, puis logique de jeu
            SceneManager.ApplyPendingScene();
            SceneManager.Update(dt);

            // 5. Rendu direct à l'écran, avec la caméra de la scène
            Renderer.Clear();
            SceneManager.Render(Renderer);
            Renderer.Present();
        }
    }

    /// <summary>Resynchronise Width/Height sur la taille réelle de la fenêtre SDL,
    /// et répercute le nouveau format sur la caméra active de la scène.</summary>
    void UpdateWindowSizeIfNeeded()
    {
        SDL.GetWindowSize(window, out int w, out int h);
        if (w != Width || h != Height)
        {
            Width = w;
            Height = h;

            if (SceneManager.Current?.Camera is Camera cam)
            {
                cam.ViewWidth = w;
                cam.ViewHeight = h;
            }
        }
    }

    /// <summary>Delta time en secondes, clampé entre 0.0001f et 1/30f.</summary>
    float ComputeDeltaTime()
    {
        ulong now = SDL.GetTicks();
        float dt = (now - lastTicks) / 1000f;
        lastTicks = now;

        return MathF.Min(MathF.Max(dt, 0.0001f), 1f / 30f);
    }
}
