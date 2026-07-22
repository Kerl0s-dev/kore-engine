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

        // Owner est toujours null pour l'EditorCamera, donc Position
        // lit/écrit directement _position (comportement hérité de Camera).

        public void Reset()
        {
            _position = new Vector2(0, 0);
            Zoom = 1f;
        }
    }
}