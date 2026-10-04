namespace KoreEngine.Hub.Ui;

/// <summary>Affiche une image (chemin de fichier) centrée, proportions conservées.</summary>
public sealed class ImageBox : Widget
{
    public string? Path;

    public ImageBox(float width, float height)
    {
        Width = width;
        Height = height;
    }

    public override void Draw(UiContext ui)
    {
        ui.FillRect(X, Y, Width, Height, Rgba.SecondaryButtonDisabled);

        if (!string.IsNullOrEmpty(Path))
        {
            var image = ui.Images.Get(Path);
            ui.Images.DrawFit(image, X, Y, Width, Height);
        }

        ui.OutlineRect(X, Y, Width, Height, Rgba.SecondaryBorder);
    }
}