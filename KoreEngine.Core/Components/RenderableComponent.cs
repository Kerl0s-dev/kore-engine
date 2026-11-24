using System.Drawing;

namespace KoreEngine;

/// <summary>
/// Base des composants qui dessinent dans le monde (SpriteRenderer, ParticleSystem...).
/// Ils ne sont plus dessinés dans l'ordre de la hiérarchie : la caméra les rassemble, élimine ceux qui sont
/// hors champ (culling), les trie par Sorting Layer puis Order in Layer, puis les dessine (voir RenderQueue).
/// </summary>
public abstract class RenderableComponent : Component
{
    /// <summary>Nom du Sorting Layer (voir SortingLayers). Les layers plus bas dans la liste sont dessinés par-dessus.</summary>
    public string SortingLayer = "Default";

    /// <summary>Ordre dans le Sorting Layer : le plus grand est dessiné au-dessus.</summary>
    public int SortingOrder = 0;

    /// <summary>
    /// Rectangle englobant en unités monde, pour le culling. Retourner false = toujours dessiné
    /// (étendue inconnue ou changeante, comme des particules).
    /// </summary>
    public virtual bool TryGetWorldBounds(out RectangleF bounds)
    {
        bounds = default;
        return false;
    }
}