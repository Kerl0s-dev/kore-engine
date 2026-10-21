using System.ComponentModel;

namespace KoreEngine;

public abstract class Component
{
    public GameObject gameObject { get; internal set; } = null!;
    public Transform transform => gameObject.transform;

    public virtual void Start() { }
    public virtual void Update(float dt) { }
    public virtual void OnDestroy() { }

    public virtual void OnCollision(Collider other) { }
    public virtual void OnTriggerEnter(Collider other) { }


    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual void Render(Renderer renderer, Camera? camera) { }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual IEnumerable<InspectorField> GetInspectorFields()
        => Enumerable.Empty<InspectorField>();

    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual IEnumerable<string> Serialize()
        => Enumerable.Empty<string>();

    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual void Deserialize(List<string> extraLines) { }
}