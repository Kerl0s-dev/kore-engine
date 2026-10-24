using SDL3;

namespace KoreEngine;

public static class Input
{
    static readonly HashSet<KeyCode> held = new();                // maintenue enfoncée
    static readonly HashSet<KeyCode> pressedThisFrame = new();    // enfoncée cette frame
    static readonly HashSet<KeyCode> releasedThisFrame = new();   // relâchée cette frame

    /// À appeler UNE fois par frame, avant PollEvent.
    public static void NewFrame()
    {
        pressedThisFrame.Clear();
        releasedThisFrame.Clear();
    }

    public static void HandleEvent(SDL.Event e)
    {
        switch (e.Type)
        {
            case (uint)SDL.EventType.KeyDown:
                if (e.Key.Repeat) break;
                var down = (KeyCode)e.Key.Key;
                if (held.Add(down)) pressedThisFrame.Add(down);
                break;

            case (uint)SDL.EventType.KeyUp:
                var up = (KeyCode)e.Key.Key;
                if (held.Remove(up)) releasedThisFrame.Add(up);
                break;

            case (uint)SDL.EventType.WindowFocusLost:
                // Les KeyUp tombés pendant la perte de focus n'arrivent jamais : on évite les touches coincées.
                foreach (var k in held) releasedThisFrame.Add(k);
                held.Clear();
                break;
        }
    }

    public static bool GetKey(KeyCode key) => held.Contains(key);                    // maintenue
    public static bool GetKeyDown(KeyCode key) => pressedThisFrame.Contains(key);    // vient d'être enfoncée
    public static bool GetKeyUp(KeyCode key) => releasedThisFrame.Contains(key);     // vient d'être relâchée

    public static bool GetAnyKey() => held.Count > 0;
    public static bool GetAnyKeyDown() => pressedThisFrame.Count > 0;
}