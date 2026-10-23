using KoreEngine.UI;
using System.ComponentModel;

namespace KoreEngine;

public partial class GameObject
{
    static int nameCounter = 0;

    public string Name { get; set; } = "";

    // ---------------------------------------------------------------
    // Transform hiérarchique
    // ---------------------------------------------------------------

    public Transform transform => GetComponent<Transform>() ?? throw new InvalidOperationException($"GameObject '{Name}' n'a pas de composant Transform.");

    // ---------------------------------------------------------------
    // Hiérarchie parent / enfants
    // ---------------------------------------------------------------

    public readonly List<GameObject> children = new();
    public IReadOnlyList<GameObject> Children => children;

    // ---------------------------------------------------------------
    // Composants
    // ---------------------------------------------------------------
    [EditorBrowsable(EditorBrowsableState.Never)]
    public List<Component> Components = new();

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Scene? Scene;

    public GameObject(string? name = null)
    {
        Name = name ?? $"GameObject ({++nameCounter})";
        // On ajoute le Transform par défaut SEULEMENT s'il n'existe pas déjà
        if (!Components.OfType<Transform>().Any())
        {
            Components.Add(new Transform { gameObject = this });
        }
    }

    public GameObject() { }

    public static GameObject CreateUI(string? name = null)
    {
        var go = new GameObject(name);
        go.SetTransform(new RectTransform());
        return go;
    }

    internal Transform SetTransform(Transform t)
    {
        var old = GetComponent<Transform>();
        if (ReferenceEquals(old, t)) return t;

        int index = old != null ? Components.IndexOf(old) : 0;
        if (old != null) { t.AdoptFrom(old); Components.RemoveAt(index); }

        t.gameObject = this;
        Components.Insert(index, t);
        return t;
    }

    public T AddComponent<T>(T component) where T : Component
    {
        if (component is Transform t)
        {
            var cur = GetComponent<Transform>();
            if (cur != null && cur.GetType() == t.GetType()) return (T)(object)cur;
            return (T)(object)SetTransform(t);   // Transform -> RectTransform
        }

        component.gameObject = this;
        Components.Add(component);

        // Comme Unity : un élément UI force un RectTransform
        if (component is UIElement && transform is not RectTransform)
            SetTransform(new RectTransform());

        return component;
    }

    public T AddComponent<T>() where T : Component => AddComponent(Activator.CreateInstance<T>());

    public void RemoveComponent<T>(T component) where T : Component
    {
        component.OnDestroy();
        Components.Remove(component);
        component.gameObject = null!;
    }

    public void RemoveComponent(Component component)
    {
        component.OnDestroy();
        Components.Remove(component);
        component.gameObject = null!;
    }

    public T? GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();

    // Surcharge non générique — utilisée par le picker de l'inspector
    // pour filtrer les objets par type de composant sans generics.
    public Component? GetComponent(Type type) => Components.FirstOrDefault(c => type.IsAssignableFrom(c.GetType()));

    // ---------------------------------------------------------------
    // Update / Render (récursifs)
    // ---------------------------------------------------------------

    public virtual void Update(float dt)
    {
        foreach (var c in Components) c.Update(dt);
        foreach (var child in children) child.Update(dt);
    }

    // Appelle directement la méthode virtuelle sans réflexion
    internal void DispatchCollision(Collider other)
    {
        for (int i = 0; i < Components.Count; i++)
        {
            if (Components[i] is Collider) continue;

            Components[i].OnCollision(other);
        }
    }

    internal void DispatchTrigger(Collider other)
    {
        for (int i = 0; i < Components.Count; i++)
        {
            if (Components[i] is Collider) continue;

            Components[i].OnTriggerEnter(other);
        }
    }

    internal virtual void Render(Renderer renderer, Camera? camera)
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

        clone.transform.Position = position ?? clone.transform.Position;
        return clone;
    }

    public void Destroy()
    {
        foreach (var c in Components) c.OnDestroy();
        foreach (var child in children.ToList()) child.Destroy();
        Scene?.Remove(this);
    }
}