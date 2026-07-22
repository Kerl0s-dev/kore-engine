using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Components.UI;

public class UICanvas : Component
{
    public float ReferenceWidth = 1920f;
    public float ReferenceHeight = 1080f;

    public float ScaleX => SceneManager.ViewportWidth / ReferenceWidth;
    public float ScaleY => SceneManager.ViewportHeight / ReferenceHeight;

    /// <summary>
    /// Crée un GameObject enfant du Owner, y attache le composant UIElement,
    /// et l'ajoute à la scène — exactement comme Unity où chaque élément UI
    /// est un GameObject enfant du Canvas.
    /// IMPORTANT : scene.Add(ownerGameObject) doit être appelé avant Add().
    /// </summary>
    public T Add<T>(T element) where T : UIElement
    {
        if (Owner?.Scene == null)
            throw new InvalidOperationException(
                "UICanvas.Add() : appelle scene.Add(canvasObject) avant canvas.Add(...).");

        // Crée le GameObject qui portera le composant
        var go = new GameObject(element.GetType().Name);
        Owner.Scene.Add(go);          // ajoute en racine…
        go.SetParent(Owner, Owner.Scene); // …puis enfant du Canvas owner

        // Attache le composant et configure la référence Canvas
        element.Canvas = this;
        go.AddComponent(element);

        return element;
    }

    // Update et Render : les enfants sont des GameObjects, traversés
    // automatiquement par la scène — rien à faire ici.
    public override void Update(float dt) { }
    public override void Render(Renderer renderer, Camera? camera) { }

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new IntField
        {
            Label = "Ref Width",
            Get = () => (int)ReferenceWidth,
            Set = v => ReferenceWidth = v,
            Min = 1,
            Max = 7680
        };

        yield return new IntField
        {
            Label = "Ref Height",
            Get = () => (int)ReferenceHeight,
            Set = v => ReferenceHeight = v,
            Min = 1,
            Max = 4320
        };

        yield return new TextField { Label = "Scale X", Get = () => $"{ScaleX:F3}" };
        yield return new TextField { Label = "Scale Y", Get = () => $"{ScaleY:F3}" };
    }
}