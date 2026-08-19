using KoreEngine.Components;
using KoreEngine.Engine;

namespace KoreEngine.Core;

public partial class GameObject
{
    static int nameCounter = 0;

    public string Name { get; set; } = "";

    // ---------------------------------------------------------------
    // Transform hiérarchique
    // ---------------------------------------------------------------

    // Position LOCALE : relative au parent (ou absolue si pas de parent).
    // C'est cette valeur qu'on édite dans l'Inspector et qu'on stocke.
    public Vector2 LocalPosition;
    public Vector2 PreviousPosition;

    // Position MONDE : remonte la chaîne de parents pour calculer la
    // position absolue. Utilisée par le rendu et la physique.
    // IMPORTANT : tous les composants qui dessinaient via Owner.Position
    // doivent maintenant utiliser Owner.WorldPosition.
    public Vector2 WorldPosition
    {
        get => Parent != null
            ? Parent.WorldPosition + LocalPosition
            : LocalPosition;
    }

    // Alias rétrocompatible — pointe sur LocalPosition pour que l'ancien
    // code qui écrit Position continue de compiler. À migrer vers
    // LocalPosition/WorldPosition selon le contexte au fil du temps.
    public Vector2 Position
    {
        get => LocalPosition;
        set => LocalPosition = value;
    }

    // Rotation locale en degrés.
    public float LocalRotation;

    public float WorldRotation
    {
        get => Parent != null ? Parent.WorldRotation + LocalRotation : LocalRotation;
    }

    // Scale locale — multiplicatif le long de la hiérarchie.
    public Vector2 LocalScale = new Vector2(1f, 1f);

    public Vector2 WorldScale
    {
        get => Parent != null
            ? new Vector2(Parent.WorldScale.X * LocalScale.X, Parent.WorldScale.Y * LocalScale.Y)
            : LocalScale;
    }

    // ---------------------------------------------------------------
    // Hiérarchie parent / enfants
    // ---------------------------------------------------------------

    public GameObject? Parent { get; private set; }

    readonly List<GameObject> children = new();
    public IReadOnlyList<GameObject> Children => children;

    /// <summary>
    /// Rattache cet objet à un nouveau parent (ou le passe en racine si null).
    /// Préserve la position monde : LocalPosition est recalculée pour que
    /// l'objet ne "saute" pas visuellement au moment du reparentage.
    /// Protège contre les cycles (on ne peut pas devenir enfant de soi-même
    /// ni d'un de ses propres descendants).
    /// </summary>
    public void SetParent(GameObject? newParent, Scene? scene = null)
    {
        if (newParent == this) return;
        if (newParent != null && newParent.IsDescendantOf(this)) return;

        // Sauvegarde la position monde avant de changer de parent.
        var worldPos = WorldPosition;

        // Détache de l'ancien parent (ou de la racine de la scène).
        if (Parent != null)
            Parent.children.Remove(this);
        else
            scene?.RootObjects.Remove(this);

        Parent = newParent;

        // Rattache au nouveau parent (ou à la racine de la scène).
        if (newParent != null)
            newParent.children.Add(this);
        else
            scene?.RootObjects.Add(this);

        // Recalcule LocalPosition pour conserver la position monde.
        LocalPosition = newParent != null
            ? worldPos - newParent.WorldPosition
            : worldPos;
    }

    public bool IsDescendantOf(GameObject ancestor)
    {
        var p = Parent;
        while (p != null)
        {
            if (p == ancestor) return true;
            p = p.Parent;
        }
        return false;
    }

    // ---------------------------------------------------------------
    // Composants
    // ---------------------------------------------------------------

    public List<Component> Components = new();
    public Scene? Scene;

    public GameObject(string? name = null)
    {
        Name = name ?? $"GameObject ({++nameCounter})";
    }

    public GameObject() { }

    public T AddComponent<T>(T component) where T : Component
    {
        component.Owner = this;
        Components.Add(component);
        return component;
    }

    public void RemoveComponent<T>(T component) where T : Component
    {
        component.OnDestroy();
        Components.Remove(component);
        component.Owner = null!;
    }

    public void RemoveComponent(Component component)
    {
        component.OnDestroy();
        Components.Remove(component);
        component.Owner = null!;
    }

    public T? GetComponent<T>() where T : Component
        => Components.OfType<T>().FirstOrDefault();

    // Surcharge non générique — utilisée par le picker de l'inspector
    // pour filtrer les objets par type de composant sans generics.
    public Component? GetComponent(Type type)
        => Components.FirstOrDefault(c => type.IsAssignableFrom(c.GetType()));

    // ---------------------------------------------------------------
    // Update / Render (récursifs)
    // ---------------------------------------------------------------

    public virtual void Update(float dt)
    {
        foreach (var c in Components) c.Update(dt);
        foreach (var child in children) child.Update(dt);
    }

    public virtual void Render(Renderer renderer, Camera? camera)
    {
        foreach (var c in Components) c.Render(renderer, camera);
        foreach (var child in children) child.Render(renderer, camera);
    }

    // ---------------------------------------------------------------
    // Clonage / instanciation runtime
    // ---------------------------------------------------------------

    /// <summary>
    /// Duplique cet objet et toute sa hiérarchie d'enfants (composants
    /// compris) dans targetScene, en tant que nouvel objet racine — via un
    /// aller-retour à travers le même format texte que la sérialisation de
    /// scène (SceneSerializer.SerializeObjectTree/DeserializeObjectTree),
    /// pour ne pas dupliquer la logique de clonage des composants.
    ///
    /// Équivalent d'un Object.Instantiate(prefab) à la Unity : sert à faire
    /// apparaître dynamiquement des balles, ennemis, objets ramassables, etc.
    /// depuis un script — chose impossible jusqu'ici sans reconstruire
    /// l'objet à la main composant par composant.
    ///
    /// Les champs qui référencent un objet HORS de cette hiérarchie (ex: un
    /// script qui garde une référence vers la Camera de la scène) ne sont
    /// PAS copiés sur le clone — à réassigner toi-même après coup si besoin.
    /// </summary>
    public GameObject Instantiate(Scene targetScene, Vector2? position = null)
    {
        string data = SceneSerializer.SerializeObjectTree(this);
        var clone = SceneSerializer.DeserializeObjectTree(data, targetScene)
            ?? throw new InvalidOperationException($"Échec de l'instanciation de '{Name}'.");

        clone.LocalPosition = position ?? LocalPosition;
        return clone;
    }
}