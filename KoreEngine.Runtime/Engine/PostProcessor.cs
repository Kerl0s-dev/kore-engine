using SDL3;

namespace KoreEngine;

/// <summary>
/// Rendu avec effets plein écran. La scène est dessinée dans une texture intermédiaire (même taille en pixels
/// que la cible courante), puis chaque effet est appliqué en redessinant la texture précédente avec son shader,
/// le dernier effet écrivant dans la cible d'origine (écran ou texture du panneau Game).
/// Sans effet actif (ou sans driver "gpu"), la scène est dessinée directement, comme avant.
/// </summary>
public sealed class PostProcessor
{
    readonly Renderer renderer;
    RenderTexture? bufferA, bufferB;
    bool running;

    public PostProcessor(Renderer renderer) => this.renderer = renderer;

    /// <summary>
    /// "drawScene" doit inclure l'effacement du fond (Renderer.Clear) : il est exécuté dans la texture
    /// intermédiaire pour que les effets voient aussi la couleur de fond de la caméra.
    /// </summary>
    public void Render(Camera? camera, Action drawScene)
    {
        List<(Material Material, MaterialParamSet Params)>? effects = null;
        if (!running && renderer.IsGpu && camera?.gameObject != null)
            effects = camera.gameObject.GetComponent<PostProcessing>()?.GetActiveEffects();

        if (effects == null || effects.Count == 0)
        {
            drawScene();
            return;
        }

        running = true;
        try { Run(effects, drawScene); }
        finally { running = false; }
    }

    void Run(List<(Material Material, MaterialParamSet Params)> effects, Action drawScene)
    {
        IntPtr handle = renderer.Handle;

        renderer.Flush();
        IntPtr finalTarget = SDL.GetRenderTarget(handle);
        SDL.GetCurrentRenderOutputSize(handle, out int width, out int height);
        SDL.GetRenderScale(handle, out float scaleX, out float scaleY);

        if (width <= 0 || height <= 0)
        {
            drawScene();
            return;
        }

        // Les textures intermédiaires (redimensionnées avant d'être liées, Resize débind la cible)
        bufferA ??= new RenderTexture(renderer, width, height);
        bufferA.Resize(width, height);
        if (effects.Count > 1)
        {
            bufferB ??= new RenderTexture(renderer, width, height);
            bufferB.Resize(width, height);
        }

        // 1. Scène -> bufferA.
        // L'échelle de rendu (comme le viewport et le clip rect) est propre à chaque cible dans SDL :
        // bufferA part à 1, il faut donc lui appliquer l'échelle que l'appelant avait posée sur la cible d'origine
        // (GamePanel : hauteur de la texture / 1080). Sans ça, la scène n'est correcte que si cette échelle vaut 1.
        SDL.SetRenderTarget(handle, bufferA.Texture);
        SDL.SetRenderScale(handle, scaleX, scaleY);
        renderer.ResetClipRect();
        SDL.SetRenderDrawColor(handle, 0, 0, 0, 0);
        SDL.RenderClear(handle);
        drawScene();
        renderer.Flush();

        // 2. Effets : pixels de la cible = pixels de la texture, donc échelle 1 sur chaque cible utilisée
        var source = bufferA;
        for (int i = 0; i < effects.Count; i++)
        {
            bool last = i == effects.Count - 1;
            var destination = source == bufferA ? bufferB : bufferA;

            SDL.SetRenderTarget(handle, last ? finalTarget : destination!.Texture);
            SDL.SetRenderScale(handle, 1f, 1f);
            renderer.ResetClipRect();

            var (material, parameters) = effects[i];
            var shader = material.Shader!;

            // Pas de mélange : le shader écrit directement chaque pixel de la cible (rien à effacer)
            SDL.SetTextureBlendMode(source.Texture, SDL.BlendMode.None);

            var data = parameters.BuildUniforms(material, width, height);
            if (data != null) shader.SetUniforms(data);

            renderer.SetShader(shader);
            renderer.ClearSdlError();
            SDL.RenderTexture(handle, source.Texture, IntPtr.Zero, IntPtr.Zero);
            renderer.Flush();   // SDL lit les uniforms au flush
            renderer.ReportSdlError($"Post-processing {material.AssetPath}");
            renderer.SetShader(null);

            if (!last) source = destination!;
        }

        // 3. La cible d'origine est déjà liée par le dernier effet ; on rétablit SON échelle (celle de l'appelant)
        SDL.SetRenderScale(handle, scaleX, scaleY);
    }
}