using KoreEngine.Core;

namespace KoreEngine.Input;

public class InputMap
{
    Dictionary<string, InputAction> actions = new();
    Dictionary<string, InputAxis> axes = new();

    public InputMap Add(InputAction action)
    {
        actions[action.Name] = action;
        return this;
    }

    public InputMap Add(InputAxis axis)
    {
        axes[axis.Name] = axis;
        return this;
    }

    public bool IsDown(string action) => actions.TryGetValue(action, out var a) && a.IsDown();
    public bool IsPressed(string action) => actions.TryGetValue(action, out var a) && a.IsPressed();
    public Vector2 GetAxis(string axis, bool normalize = true) => axes.TryGetValue(axis, out var a) ? a.GetAxis(normalize) : Vector2.Zero;
}