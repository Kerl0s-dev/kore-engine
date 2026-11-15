using System.Runtime.InteropServices;
using SDL3;

namespace KoreEngine;

/// <summary>
/// Fragment shader HLSL compilé à l'exécution (SDL_shadercross) et enveloppé dans un
/// SDL_GPURenderState, utilisable avec Renderer.SetShader(). Exige le driver de rendu "gpu".
///
/// Convention (celle de SDL pour les shaders de SDL_Renderer) : point d'entrée "main",
///   Texture2D u_texture : register(t0, space2);   // texture dessinée
///   SamplerState u_sampler : register(s0, space2);
///   cbuffer Params : register(b0, space3) { ... }  // uniforms (optionnel)
///   entrée : float4 color : COLOR0, float2 uv : TEXCOORD0 ; sortie : float4 SV_Target
/// </summary>
public sealed class Shader : IDisposable
{
    // SDL3-CS 3.4.10.2 n'expose pas SDL.GPURenderStateCreateInfo (CreateGPURenderState prend un IntPtr) :
    // on déclare ici la struct, avec la disposition exacte de SDL_GPURenderStateCreateInfo.
    [StructLayout(LayoutKind.Sequential)]
    struct RenderStateCreateInfo
    {
        public IntPtr FragmentShader;
        public int NumSamplerBindings;
        public IntPtr SamplerBindings;
        public int NumStorageTextures;
        public IntPtr StorageTextures;
        public int NumStorageBuffers;
        public IntPtr StorageBuffers;
        public uint Props;
    }

    static bool crossInitialized;
    static bool loggedDriver;

    /// <summary>Compile un fragment shader HLSL (point d'entrée "main") en SPIR-V ou en DXIL. Buffer à libérer avec SDL.Free.</summary>
    static IntPtr CompileHlsl(string source, string includeDir, bool dxil, out UIntPtr size)
    {
        IntPtr srcPtr = Marshal.StringToCoTaskMemUTF8(source);
        IntPtr entryPtr = Marshal.StringToCoTaskMemUTF8("main");
        IntPtr dirPtr = Marshal.StringToCoTaskMemUTF8(includeDir);
        try
        {
            var info = new ShaderCross.HLSLInfo
            {
                Source = srcPtr,
                Entrypoint = entryPtr,
                IncludeDir = dirPtr,
                Defines = IntPtr.Zero,
                ShaderStage = ShaderCross.ShaderStage.Fragment,
                Props = 0
            };
            return dxil
                ? ShaderCross.CompileDXILFromHLSL(in info, out size)
                : ShaderCross.CompileSPIRVFromHLSL(ref info, out size);
        }
        finally
        {
            Marshal.FreeCoTaskMem(srcPtr);
            Marshal.FreeCoTaskMem(entryPtr);
            Marshal.FreeCoTaskMem(dirPtr);
        }
    }

    public string SourcePath { get; }
    public IntPtr State { get; private set; }

    /// <summary>Nombre de uniform buffers déclarés par le shader (réflexion SPIR-V).</summary>
    public int NumUniformBuffers { get; }

    readonly IntPtr device;
    IntPtr gpuShader;

    Shader(string sourcePath, IntPtr device, IntPtr gpuShader, IntPtr state, int numUniformBuffers)
    {
        SourcePath = sourcePath;
        this.device = device;
        this.gpuShader = gpuShader;
        State = state;
        NumUniformBuffers = numUniformBuffers;
    }

    /// <summary>Compile le fichier .hlsl. Retourne null (erreur loguée) en cas d'échec ou sans driver "gpu".</summary>
    public static Shader? Load(Renderer renderer, string path)
    {
        if (!renderer.IsGpu)
        {
            Logger.Warning($"[Shader] '{path}' ignoré : le driver de rendu actif est '{renderer.DriverName}' (il faut 'gpu').");
            return null;
        }

        string file = TextureCache.Resolve(path);
        if (!File.Exists(file))
        {
            Logger.Error($"[Shader] Fichier introuvable : {path}");
            return null;
        }

        if (!crossInitialized)
        {
            if (!ShaderCross.Init())
            {
                Logger.Error($"[Shader] ShaderCross.Init a échoué : {SDL.GetError()}");
                return null;
            }
            crossInitialized = true;
        }

        IntPtr device = SDL.GetGPURendererDevice(renderer.Handle);
        if (device == IntPtr.Zero)
        {
            Logger.Error($"[Shader] Pas de device GPU sur le renderer : {SDL.GetError()}");
            return null;
        }

        string hlslSource = File.ReadAllText(file);
        string includeDir = Path.GetDirectoryName(file) ?? "";

        // 1. HLSL -> SPIR-V (réflexion des ressources, et bytecode du backend Vulkan)
        IntPtr spirv = CompileHlsl(hlslSource, includeDir, dxil: false, out UIntPtr size);
        if (spirv == IntPtr.Zero)
        {
            Logger.Error($"[Shader] Compilation HLSL de '{path}' : {SDL.GetError()}", file);
            return null;
        }

        try
        {
            // 2. Réflexion : nombre de samplers / uniform buffers déclarés par le shader
            IntPtr metaPtr = ShaderCross.ReflectGraphicsSPIRV(spirv, size, 0);
            if (metaPtr == IntPtr.Zero)
            {
                Logger.Error($"[Shader] Réflexion de '{path}' : {SDL.GetError()}", file);
                return null;
            }
            var resources = Marshal.PtrToStructure<ShaderCross.GraphicsShaderMetadata>(metaPtr).ResourceInfo;
            SDL.Free(metaPtr);

            // 3. Shader GPU selon le format du backend.
            //    D3D12 (DXIL) : on compile le HLSL directement en DXIL, comme les shaders natifs de SDL.
            //    Passer par SPIR-V -> HLSL -> DXIL renomme les sémantiques d'interface (TEXCOORD0/1 au lieu
            //    de COLOR0/TEXCOORD0) et ne correspond plus à la sortie du vertex shader de SDL.
            var formats = SDL.GetGPUShaderFormats(device);
            if (!loggedDriver)
            {
                loggedDriver = true;
                Logger.Log($"[Shader] Device GPU : {SDL.GetGPUDeviceDriver(device)} (formats : {formats})");
            }

            IntPtr gpuShader;
            if ((formats & SDL.GPUShaderFormat.DXIL) != 0)
            {
                IntPtr dxil = CompileHlsl(hlslSource, includeDir, dxil: true, out UIntPtr dxilSize);
                if (dxil == IntPtr.Zero)
                {
                    Logger.Error($"[Shader] Compilation DXIL de '{path}' : {SDL.GetError()}", file);
                    return null;
                }

                IntPtr entry = Marshal.StringToCoTaskMemUTF8("main");
                try
                {
                    var ci = new SDL.GPUShaderCreateInfo
                    {
                        CodeSize = dxilSize,
                        Code = dxil,
                        _entrypoint = entry,
                        Format = SDL.GPUShaderFormat.DXIL,
                        Stage = SDL.GPUShaderStage.Fragment,
                        NumSamplers = resources.NumSamplers,
                        NumStorageTextures = resources.NumStorageTextures,
                        NumStorageBuffers = resources.NumStorageBuffers,
                        NumUniformBuffers = resources.NumUniformBuffers,
                        Props = 0
                    };
                    gpuShader = SDL.CreateGPUShader(device, in ci);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(entry);
                    SDL.Free(dxil);
                }
            }
            else
            {
                IntPtr spvEntry = Marshal.StringToCoTaskMemUTF8("main");
                try
                {
                    var spvInfo = new ShaderCross.SPIRVInfo
                    {
                        ByteCode = spirv,
                        ByteCodeSize = size,
                        Entrypoint = spvEntry,
                        ShaderStage = ShaderCross.ShaderStage.Fragment,
                        Props = 0
                    };
                    gpuShader = ShaderCross.CompileGraphicsShaderFromSPIRV(device, ref spvInfo, ref resources, 0);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(spvEntry);
                }
            }

            if (gpuShader == IntPtr.Zero)
            {
                Logger.Error($"[Shader] Création du shader GPU '{path}' : {SDL.GetError()}", file);
                return null;
            }

            // 4. Render state utilisable avec SDL_SetGPURenderState
            var createInfo = new RenderStateCreateInfo { FragmentShader = gpuShader };
            IntPtr infoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RenderStateCreateInfo>());
            IntPtr state;
            try
            {
                Marshal.StructureToPtr(createInfo, infoPtr, false);
                state = SDL.CreateGPURenderState(renderer.Handle, infoPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }

            if (state == IntPtr.Zero)
            {
                Logger.Error($"[Shader] Création du render state '{path}' : {SDL.GetError()}", file);
                SDL.ReleaseGPUShader(device, gpuShader);
                return null;
            }

            Logger.Log($"[Shader] '{path}' prêt : {resources.NumSamplers} sampler(s), {resources.NumUniformBuffers} uniform buffer(s), format {((formats & SDL.GPUShaderFormat.DXIL) != 0 ? "DXIL" : "SPIR-V/MSL")}");
            return new Shader(path, device, gpuShader, state, (int)resources.NumUniformBuffers);
        }
        finally
        {
            SDL.Free(spirv);
        }
    }

    public void Dispose()
    {
        if (State != IntPtr.Zero) { SDL.DestroyGPURenderState(State); State = IntPtr.Zero; }
        if (gpuShader != IntPtr.Zero) { SDL.ReleaseGPUShader(device, gpuShader); gpuShader = IntPtr.Zero; }
    }
}

/// <summary>Cache de shaders compilés, indexé par chemin d'asset (".hlsl").</summary>
public static class ShaderCache
{
    static Renderer? renderer;
    static readonly Dictionary<string, Shader?> cache = new();

    public static void Init(Renderer r) => renderer = r;

    public static Shader? Get(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (cache.TryGetValue(path, out var shader)) return shader;

        if (renderer == null)
        {
            Logger.Error($"[ShaderCache] Init() n'a pas été appelé avant de charger '{path}'.");
            return null;
        }

        shader = Shader.Load(renderer, path);
        cache[path] = shader;   // y compris null : on ne recompile pas à chaque frame un shader en erreur
        return shader;
    }

    /// <summary>Détruit tous les shaders (à appeler avant de recréer le renderer ou pour forcer une recompilation).</summary>
    public static void Clear()
    {
        foreach (var s in cache.Values) s?.Dispose();
        cache.Clear();
    }
}