using KoreEngine.Components;
using KoreEngine.Engine;
using KoreEngine.Physics;

namespace KoreEngine.Core;

public class Scene
{
    public string Name { get; set; } = "New Scene";

    // Objets RACINE uniquement (sans parent).
    // Les enfants sont stockés sur leur parent, traversés récursivement.
    public List<GameObject> RootObjects = new();

    // Tous les objets de la scène, aplatis — utile pour la physique,
    // les lookups, etc.
    public IEnumerable<GameObject> AllObjects => Flatten(RootObjects);

    public CollisionSystem Collisions = new();

    Camera? camera;
    public Camera? Camera
    {
        get => camera;
        set => camera = value;
    }

    /// <summary>
    /// Cherche le premier composant Camera dans tous les objets de la scène
    /// et l'assigne à Scene.Camera. Appelé automatiquement après un chargement
    /// depuis fichier, où Camera n'est pas assigné explicitement.
    /// </summary>
    public void FindCamera()
    {
        foreach (var obj in AllObjects)
        {
            var cam = obj.GetComponent<Camera>();
            if (cam != null) { camera = cam; return; }
        }
    }

    // ---------------------------------------------------------------
    // Ajout / Suppression (niveau racine)
    // ---------------------------------------------------------------

    /// <summary>Ajoute un objet en tant qu'objet racine de la scène.</summary>
    public void Add(GameObject obj)
    {
        obj.Scene = this;
        RootObjects.Add(obj);
        RegisterColliders(obj);
    }

    /// <summary>
    /// Supprime un objet et TOUS ses descendants de la scène.
    /// Si l'objet est enfant d'un autre, il est aussi détaché de son parent.
    /// </summary>
    public void Remove(GameObject obj)
    {
        // Appelle OnDestroy() sur l'objet et tous ses descendants AVANT de les
        // détacher — pour que Owner et la hiérarchie restent valides pendant
        // que les composants nettoient leurs ressources (ex: un composant qui
        // aurait besoin de connaître Owner.WorldPosition dans OnDestroy).
        DestroyRecursive(obj);

        if (obj.Parent != null)
            obj.SetParent(null, this);
        else
            RootObjects.Remove(obj);

        UnregisterColliders(obj);
    }

    // ---------------------------------------------------------------
    // Update / Render
    // ---------------------------------------------------------------

    public void Start()
    {
        foreach (var obj in AllObjects)
            foreach (var c in obj.Components)
                c.Start();
    }

    public void Update(float dt)
    {
        var all = AllObjects.ToList();

        // Passe 1 : tous les composants sauf PhysicsBody
        // (input, controllers, animations, state machines...)
        // Doit tourner AVANT PhysicsBody pour que MovementController
        // calcule la vélocité que PhysicsBody va ensuite appliquer,
        // et que les transitions d'animation lisent un état cohérent.
        foreach (var obj in all)
            foreach (var c in obj.Components)
                if (c is not PhysicsBody)
                    c.Update(dt);

        // Passe 2 : PhysicsBody — applique la vélocité calculée en passe 1
        // et intègre la gravité. IsGrounded est reset ici à false,
        // rétabli par Collisions.Update en passe 3.
        foreach (var obj in all)
            obj.GetComponent<PhysicsBody>()?.Update(dt);

        // Passe 3 : résolution des collisions — met à jour IsGrounded,
        // corrige les positions après pénétration.
        Collisions.Update(dt);
    }

    public void Render(Renderer renderer)
    {
        foreach (var obj in RootObjects)
            obj.Render(renderer, Camera);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    static IEnumerable<GameObject> Flatten(IEnumerable<GameObject> objects)
    {
        foreach (var obj in objects)
        {
            yield return obj;
            foreach (var desc in Flatten(obj.Children))
                yield return desc;
        }
    }

    /// <summary>
    /// Rescanne tous les objets de la scène et enregistre leurs colliders.
    /// Nécessaire après un chargement depuis fichier, où les composants sont
    /// attachés APRÈS que les objets aient été ajoutés via Add() — donc le
    /// Collider n'existait pas encore au moment du premier enregistrement.
    /// </summary>
    public void RefreshColliders()
    {
        foreach (var obj in RootObjects)
            RegisterColliders(obj);
    }

    void RegisterColliders(GameObject obj)
    {
        var collider = obj.GetComponent<Collider>();
        if (collider != null) Collisions.Register(collider);
        foreach (var child in obj.Children) RegisterColliders(child);
    }

    void UnregisterColliders(GameObject obj)
    {
        var collider = obj.GetComponent<Collider>();
        if (collider != null) Collisions.Unregister(collider);
        foreach (var child in obj.Children) UnregisterColliders(child);
    }

    /// <summary>
    /// Appelle OnDestroy() sur tous les composants de cet objet et de ses
    /// descendants, récursivement. Utilisé par Remove() (suppression ciblée)
    /// et par SceneManager (remplacement/déchargement complet de la scène).
    /// </summary>
    static void DestroyRecursive(GameObject obj)
    {
        foreach (var c in obj.Components)
            c.OnDestroy();
        foreach (var child in obj.Children)
            DestroyRecursive(child);
    }

    /// <summary>
    /// Appelle OnDestroy() sur TOUS les composants de la scène. À appeler avant
    /// qu'une scène ne soit remplacée ou déchargée (voir SceneManager), pour
    /// que les composants libèrent leurs ressources natives (handles audio,
    /// textures allouées manuellement, etc.) avant de disparaître.
    /// </summary>
    public void DestroyAll()
    {
        foreach (var obj in RootObjects)
            DestroyRecursive(obj);
    }
}