// Editor/ImGuiBackend.cs
using ImGuiNET;
using KoreEngine.Engine;
using SDL3;
using System.Runtime.InteropServices;

namespace KoreEngine.Editor;

public class ImGuiBackend
{
    IntPtr rendererHandle;
    IntPtr fontTexture;
    bool initialized = false;

    public ImGuiBackend(Renderer renderer)
    {
        rendererHandle = renderer.Handle;
    }

    public void Init(int windowWidth, int windowHeight)
    {
        ImGui.CreateContext();
        var io = ImGui.GetIO();

        io.DisplaySize = new System.Numerics.Vector2(windowWidth, windowHeight);
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        // Active le docking
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        ImGui.StyleColorsDark();
        BuildFontAtlas();
        initialized = true;
    }

    unsafe void BuildFontAtlas()
    {
        var io = ImGui.GetIO();

        io.Fonts.Clear();

        // Police principale — couvre le Latin étendu (accents français inclus)
        // que la police par défaut d'ImGui ne gère pas correctement.
        string mainFontPath = @"C:\Windows\Fonts\segoeui.ttf";
        if (File.Exists(mainFontPath))
            io.Fonts.AddFontFromFileTTF(mainFontPath, 16f, IntPtr.Zero, io.Fonts.GetGlyphRangesDefault());
        else
            io.Fonts.AddFontDefault(); // fallback si la police système est introuvable

        // Symboles de la toolbar (▶ ■ ⏭) — fusionnés dans le même atlas,
        // seulement les codepoints utilisés (pas toute la police Symbol,
        // pour ne pas gonfler inutilement la texture générée).
        string symbolFontPath = @"C:\Windows\Fonts\seguisym.ttf";
        if (File.Exists(symbolFontPath))
        {
            ushort[] iconRanges = { 0x25B6, 0x25B6, 0x25A0, 0x25A0, 0x2016, 0x2016, 0x23ED, 0x23ED, 0 };

            fixed (ushort* rangesPtr = iconRanges)
            {
                var config = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig())
                {
                    MergeMode = true,
                    PixelSnapH = true
                };

                io.Fonts.AddFontFromFileTTF(symbolFontPath, 16f, config, (IntPtr)rangesPtr);
            }
        }

        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        IntPtr texture = SDL.CreateTexture(
            rendererHandle,
            SDL.PixelFormat.ABGR8888,
            SDL.TextureAccess.Static,
            width, height
        );

        SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
        SDL.UpdateTexture(texture, IntPtr.Zero, pixels, width * bytesPerPixel);

        fontTexture = texture;
        io.Fonts.SetTexID(fontTexture);
        io.Fonts.ClearTexData();
    }

    public void NewFrame(float dt, int windowWidth, int windowHeight)
    {
        if (!initialized) return;

        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(windowWidth, windowHeight);
        io.DeltaTime = dt;

        ImGui.NewFrame();
    }

    public void HandleEvent(SDL.Event e)
    {
        if (!initialized) return;
        var io = ImGui.GetIO();

        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.MouseMotion:
                io.AddMousePosEvent(e.Motion.X, e.Motion.Y);
                break;

            case SDL.EventType.MouseButtonDown:
            case SDL.EventType.MouseButtonUp:
                bool down = e.Type == (uint)SDL.EventType.MouseButtonDown;
                int btn = e.Button.Button switch
                {
                    1 => 0, // gauche
                    2 => 2, // milieu
                    3 => 1, // droite
                    _ => -1
                };
                if (btn >= 0) io.AddMouseButtonEvent(btn, down);
                break;

            case SDL.EventType.MouseWheel:
                io.AddMouseWheelEvent(e.Wheel.X, e.Wheel.Y);
                break;

            case SDL.EventType.KeyDown:
            case SDL.EventType.KeyUp:
                bool keyDown = e.Type == (uint)SDL.EventType.KeyDown;
                io.AddKeyEvent(SDLKeycodeToImGui(e.Key.Key), keyDown);
                io.AddKeyEvent(ImGuiKey.ModCtrl, (e.Key.Mod & SDL.Keymod.Ctrl) != 0);
                io.AddKeyEvent(ImGuiKey.ModShift, (e.Key.Mod & SDL.Keymod.Shift) != 0);
                io.AddKeyEvent(ImGuiKey.ModAlt, (e.Key.Mod & SDL.Keymod.Alt) != 0);
                break;

            case SDL.EventType.TextInput:
                unsafe
                {
                    io.AddInputCharactersUTF8(Marshal.PtrToStringUTF8((IntPtr)e.Text.Text) ?? "");
                }
                break;
        }
    }

    public void Render()
    {
        if (!initialized) return;

        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        RenderDrawData(drawData);
    }

    unsafe void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0) return;

        float scaleX = 1f, scaleY = 1f;
        if (drawData.FramebufferScale.X != 0) scaleX = drawData.FramebufferScale.X;
        if (drawData.FramebufferScale.Y != 0) scaleY = drawData.FramebufferScale.Y;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var cmd = cmdList.CmdBuffer[i];

                if (cmd.UserCallback != IntPtr.Zero) continue;

                // Clip rect
                var clipMin = new System.Numerics.Vector2(
                    (cmd.ClipRect.X - drawData.DisplayPos.X) * scaleX,
                    (cmd.ClipRect.Y - drawData.DisplayPos.Y) * scaleY);
                var clipMax = new System.Numerics.Vector2(
                    (cmd.ClipRect.Z - drawData.DisplayPos.X) * scaleX,
                    (cmd.ClipRect.W - drawData.DisplayPos.Y) * scaleY);

                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) continue;

                var clipRect = new SDL.Rect
                {
                    X = (int)clipMin.X,
                    Y = (int)clipMin.Y,
                    W = (int)(clipMax.X - clipMin.X),
                    H = (int)(clipMax.Y - clipMin.Y)
                };
                SDL.SetRenderClipRect(rendererHandle, in clipRect);

                // Vertex et index buffers
                var vtxBuffer = (ImDrawVert*)cmdList.VtxBuffer.Data;
                var idxBuffer = (ushort*)cmdList.IdxBuffer.Data;

                int vtxCount = cmdList.VtxBuffer.Size;
                int idxCount = (int)cmd.ElemCount;

                var sdlVerts = new SDL.Vertex[vtxCount];
                var sdlIndices = new int[idxCount];

                // Convertit les vertices ImGui → SDL
                for (int v = 0; v < vtxCount; v++)
                {
                    var vtx = vtxBuffer[v];
                    sdlVerts[v] = new SDL.Vertex
                    {
                        Position = new SDL.FPoint { X = vtx.pos.X, Y = vtx.pos.Y },
                        Color = new SDL.FColor
                        {
                            R = ((vtx.col >> 0) & 0xFF) / 255f,
                            G = ((vtx.col >> 8) & 0xFF) / 255f,
                            B = ((vtx.col >> 16) & 0xFF) / 255f,
                            A = ((vtx.col >> 24) & 0xFF) / 255f
                        },
                        TexCoord = new SDL.FPoint { X = vtx.uv.X, Y = vtx.uv.Y }
                    };
                }

                // Convertit les indices
                for (int idx = 0; idx < idxCount; idx++)
                    sdlIndices[idx] = idxBuffer[cmd.IdxOffset + idx] + (int)cmd.VtxOffset;

                IntPtr texture = cmd.GetTexID();
                SDL.RenderGeometry(rendererHandle, texture, sdlVerts, vtxCount, sdlIndices, idxCount);
            }
        }

        // Reset clip
        SDL.SetRenderClipRect(rendererHandle, IntPtr.Zero);
    }

    public void Shutdown()
    {
        if (fontTexture != IntPtr.Zero)
            SDL.DestroyTexture(fontTexture);
        ImGui.DestroyContext();
    }

    ImGuiKey SDLKeycodeToImGui(SDL.Keycode key)
    {
        // Lettres A-Z : les deux enums (SDL.Keycode et ImGuiKey) sont contigus
        // et alphabétiques sur cette plage, donc un simple offset les fait
        // correspondre sans énumérer les 26 cas un par un.
        if (key >= SDL.Keycode.A && key <= SDL.Keycode.Z)
            return (ImGuiKey)((int)ImGuiKey.A + ((int)key - (int)SDL.Keycode.A));

        return key switch
        {
            SDL.Keycode.Tab => ImGuiKey.Tab,
            SDL.Keycode.Left => ImGuiKey.LeftArrow,
            SDL.Keycode.Right => ImGuiKey.RightArrow,
            SDL.Keycode.Up => ImGuiKey.UpArrow,
            SDL.Keycode.Down => ImGuiKey.DownArrow,
            SDL.Keycode.Return => ImGuiKey.Enter,
            SDL.Keycode.Escape => ImGuiKey.Escape,
            SDL.Keycode.Backspace => ImGuiKey.Backspace,
            SDL.Keycode.Delete => ImGuiKey.Delete,
            SDL.Keycode.Space => ImGuiKey.Space,
            _ => ImGuiKey.None
        };
    }
}