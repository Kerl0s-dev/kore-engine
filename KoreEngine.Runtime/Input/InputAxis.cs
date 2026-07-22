using KoreEngine.Core;
using SDL3;

namespace KoreEngine.Input;

public class InputAxis
{
    public string Name;

    SDL.Keycode negative, positive;
    SDL.Keycode up, down;
    bool isAxis2D;

    public InputAxis(string name) { Name = name; }

    public InputAxis BindKeys(SDL.Keycode neg, SDL.Keycode pos)
    {
        negative = neg;
        positive = pos;
        isAxis2D = false;
        return this;
    }

    public InputAxis BindKeys(SDL.Keycode up, SDL.Keycode down, SDL.Keycode left, SDL.Keycode right)
    {
        negative = left;
        positive = right;
        this.up = up;
        this.down = down;
        isAxis2D = true;
        return this;
    }

    public Vector2 GetAxis(bool normalize = true)
    {
        if (isAxis2D)
        {
            var v = new Vector2(
                (InputManager.IsKeyDown(positive) ? 1 : 0) - (InputManager.IsKeyDown(negative) ? 1 : 0),
                (InputManager.IsKeyDown(down) ? 1 : 0) - (InputManager.IsKeyDown(up) ? 1 : 0)
            );
            return normalize ? v.Normalize() : v;
        }
        else
        {
            float x = 0;
            if (InputManager.IsKeyDown(negative)) x -= 1;
            if (InputManager.IsKeyDown(positive)) x += 1;
            return new Vector2(x, 0);
        }
    }
}