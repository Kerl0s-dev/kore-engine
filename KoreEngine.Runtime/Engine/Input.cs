using SDL3;

namespace KoreEngine;

public static class Input
{
    static HashSet<KeyCode> keysDown = new();
    static HashSet<KeyCode> keysPressed = new();

    public static void NewFrame() => keysPressed.Clear();

    public static void HandleEvent(SDL.Event e)
    {
        if (e.Type == (uint)SDL.EventType.KeyDown)
        {
            if (!keysPressed.Contains((KeyCode)e.Key.Key))
                keysDown.Add((KeyCode)e.Key.Key);
            keysPressed.Add((KeyCode)e.Key.Key);
        }
        else if (e.Type == (uint)SDL.EventType.KeyUp)
        {
            keysPressed.Remove((KeyCode)e.Key.Key);
        }
    }

    public static bool GetKeyDown(KeyCode key) => keysDown.Contains(key);
    public static bool GetKeyUp(KeyCode key) => !keysDown.Contains(key);
    public static bool GetAnyKeyDown() => keysDown.Any();

    public static bool GetKey(KeyCode key) => keysPressed.Contains(key);
    public static bool GetAnyKey() => keysPressed.Any();
}