using System.Drawing;
using KoreEngine.UI;

namespace KoreEngine;

/// <summary>
/// Liste de rendu d'une caméra, comme le pipeline de Unity : rassembler les renderers, éliminer ceux qui sont
/// hors champ ou masqués par le Culling Mask, trier, puis dessiner.
/// Ordre : Sorting Layer, puis Order in Layer, puis ordre de la hiérarchie (départage les égalités).
/// </summary>
public sealed class RenderQueue
{
    struct Entry
    {
        public RenderableComponent Renderer;
        public int Layer;
        public int Order;
        public int Sequence;
    }

    readonly List<Entry> entries = new();

    /// <summary>Nombre de renderers dessinés lors du dernier rendu.</summary>
    public int DrawnCount { get; private set; }

    /// <summary>Nombre de renderers éliminés (hors champ) lors du dernier rendu.</summary>
    public int CulledCount { get; private set; }

    public void RenderWorld(Scene scene, Renderer renderer, Camera camera)
    {
        entries.Clear();
        CulledCount = 0;

        RectangleF view = camera.BoundsMeters;
        int sequence = 0;

        foreach (var root in scene.RootObjects)
            Gather(root, camera, view, ref sequence);

        entries.Sort(Compare);

        foreach (var entry in entries)
            entry.Renderer.Render(renderer, camera);

        DrawnCount = entries.Count;
    }

    void Gather(GameObject obj, Camera camera, RectangleF view, ref int sequence)
    {
        if (!obj.Enabled) return;

        // Le layer s'applique à l'objet lui-même : ses enfants peuvent être sur un autre layer
        bool layerVisible = (camera.CullingMask & (1 << (obj.Layer & 31))) != 0;
        if (layerVisible)
        {
            foreach (var component in obj.Components)
            {
                if (component is not RenderableComponent renderer) continue;

                if (renderer.TryGetWorldBounds(out var bounds) && !bounds.IntersectsWith(view))
                {
                    CulledCount++;
                    continue;
                }

                entries.Add(new Entry
                {
                    Renderer = renderer,
                    Layer = SortingLayers.IndexOf(renderer.SortingLayer),
                    Order = renderer.SortingOrder,
                    Sequence = sequence++
                });
            }
        }

        foreach (var child in obj.children)
            Gather(child, camera, view, ref sequence);
    }

    static int Compare(Entry a, Entry b)
    {
        int c = a.Layer.CompareTo(b.Layer);
        if (c != 0) return c;
        c = a.Order.CompareTo(b.Order);
        if (c != 0) return c;
        return a.Sequence.CompareTo(b.Sequence);
    }

    // ---------------------------------------------------------------
    // Sélection à la souris (éditeur) : de l'objet le plus au-dessus au plus en dessous
    // ---------------------------------------------------------------

    /// <summary>
    /// Objets de la scène du plus visible au moins visible, dans le même ordre que le rendu :
    /// l'UI (dessinée en overlay) d'abord, puis les renderers du monde du plus haut Sorting Layer / Order au plus bas.
    /// </summary>
    public static IEnumerable<GameObject> TopToBottom(Scene scene)
    {
        var list = new List<(GameObject Object, int Layer, int Order, int Sequence)>();
        int sequence = 0;

        foreach (var obj in scene.AllObjects)
        {
            var (layer, order) = SortKey(obj);
            list.Add((obj, layer, order, sequence++));
        }

        list.Sort((a, b) =>
        {
            int c = b.Layer.CompareTo(a.Layer);
            if (c != 0) return c;
            c = b.Order.CompareTo(a.Order);
            if (c != 0) return c;
            return b.Sequence.CompareTo(a.Sequence);
        });

        return list.Select(t => t.Object);
    }

    static (int Layer, int Order) SortKey(GameObject obj)
    {
        if (obj.GetComponent<UIElement>() != null)
            return (int.MaxValue, 0);

        foreach (var component in obj.Components)
            if (component is RenderableComponent renderer)
                return (SortingLayers.IndexOf(renderer.SortingLayer), renderer.SortingOrder);

        return (0, 0);
    }
}