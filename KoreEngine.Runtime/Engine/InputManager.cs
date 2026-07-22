using SDL3;

namespace KoreEngine;

public static class InputManager
{
    static HashSet<SDL.Keycode> keysDown = new();
    static HashSet<SDL.Keycode> keysPressed = new();

    public static void NewFrame() => keysPressed.Clear();

    public static void HandleEvent(SDL.Event e)
    {
        if (e.Type == (uint)SDL.EventType.KeyDown)
        {
            if (!keysDown.Contains(e.Key.Key))
                keysPressed.Add(e.Key.Key);
            keysDown.Add(e.Key.Key);
        }
        else if (e.Type == (uint)SDL.EventType.KeyUp)
        {
            keysDown.Remove(e.Key.Key);
        }
    }

    public static bool IsKeyDown(SDL.Keycode key) => keysDown.Contains(key);
    public static bool IsAnyKeyDown() => keysDown.Any();

    public static bool IsKeyPressed(SDL.Keycode key) => keysPressed.Contains(key);
    public static bool IsAnyKeyPressed() => keysPressed.Any();
}