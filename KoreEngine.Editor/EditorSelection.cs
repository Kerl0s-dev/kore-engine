using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Editor
{
    public enum GizmoMode { Move, Rotate, Scale }
    public enum GizmoSpace { Local, World }

    /// <summary>
    /// Source de vérité unique pour la sélection courante dans l'éditeur.
    /// HierarchyPanel écrit dedans, InspectorPanel lit dedans.
    /// Statique pour éviter de faire passer une référence partout.
    /// </summary>
    public static class EditorSelection
    {
        static EditorSelection()
        {
            // SceneManager ne connaît pas EditorSelection (Runtime ne doit
            // rien savoir de l'Editor) — c'est l'Editor qui s'abonne pour
            // vider la sélection à chaque changement de scène.
            SceneManager.OnSceneChanging += () => Selected = null;
        }

        public static GameObject? Selected { get; set; }

        // Objet en cours de drag depuis la hiérarchie — lu par InspectorPanel
        // pour les champs de type GameObject/Component assignables par drop.
        public static GameObject? DraggedObject { get; set; }

        public static void ClearIfDeleted(GameObject obj)
        {
            if (Selected == obj) Selected = null;
            if (DraggedObject == obj) DraggedObject = null;
        }

        public static GizmoMode ActiveGizmoMode { get; set; } = GizmoMode.Move;
        public static GizmoSpace ActiveGizmoSpace { get; set; } = GizmoSpace.World;
    }
}