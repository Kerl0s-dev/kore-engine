using SDL3;

namespace KoreEngine.Input;

public class InputAction
{
    public string Name;

    List<SDL.Keycode> keys = new();
    List<SDL.Keycode> keysPressed = new(); // pour IsPressed (une seule frame)

    public InputAction(string name) { Name = name; }

    public InputAction BindKey(SDL.Keycode key)
    {
        keys.Add(key);
        return this;
    }

    public InputAction BindKeyPressed(SDL.Keycode key)
    {
        keysPressed.Add(key);
        return this;
    }

    public bool IsDown() => keys.Any(InputManager.IsKeyDown);
    public bool IsPressed() => keysPressed.Any(InputManager.IsKeyPressed);
}