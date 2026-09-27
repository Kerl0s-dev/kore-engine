using KoreEngine.Components;
using KoreEngine.Core;

namespace KoreEngine.Editor
{
    /// <summary>
    /// Caméra de l'éditeur — indépendante de la caméra de jeu.
    /// N'est jamais attachée à un GameObject (Owner == null) : elle gère
    /// sa propre position via le champ _position hérité de Camera.
    /// Pan : clic molette + drag. Zoom : Ctrl + molette.
    /// </summary>
    public class EditorCamera : Camera
    {
        public EditorCamera(int viewWidth, int viewHeight)
            : base(viewWidth, viewHeight) { }

        public void Reset()
        {
            Position = Vector2.Zero;
            Zoom = 1f;
        }
    }
}