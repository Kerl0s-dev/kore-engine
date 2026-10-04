namespace KoreEngine.Hub.Ui;

public abstract class Widget
{
    public float X, Y, Width, Height;
    public bool Visible = true;
    public bool Enabled = true;

    public bool Contains(float px, float py)
        => px >= X && px < X + Width && py >= Y && py < Y + Height;

    public virtual void Update(UiContext ui) { }
    public abstract void Draw(UiContext ui);
}