using KoreEngine.Components;
using KoreEngine.Core;

namespace KoreEngine.Physics;

public struct CollisionInfo
{
    public Collider Other;       // l'objet avec lequel on collisionne
    public Vector2 Normal;       // direction de la collision (ex: (0,-1) = par le bas)
    public float Penetration;    // profondeur du chevauchement en pixels
}