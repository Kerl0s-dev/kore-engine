namespace KoreEngine;

public enum GizmoMode { Move, Rotate, Scale }
public enum GizmoSpace { Local, World }

/// <summary>
/// Source de vérité unique pour la sélection courante dans l'éditeur.
/// HierarchyPanel écrit dedans, InspectorPanel lit dedans.
/// Statique pour éviter de faire passer une référence partout.
///
/// Deux sélections mutuellement exclusives : un objet de la scène (Selected) ou un asset du
/// Project Panel (SelectedAssetPath). Sélectionner l'un vide l'autre.
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

    static GameObject? selected;
    static string? selectedAssetPath;

    public static GameObject? Selected
    {
        get => selected;
        set { selected = value; if (value != null) selectedAssetPath = null; }
    }

    /// <summary>Chemin complet de l'asset sélectionné dans le Project Panel (null = aucun).</summary>
    public static string? SelectedAssetPath
    {
        get => selectedAssetPath;
        set { selectedAssetPath = value; if (value != null) selected = null; }
    }

    // Objet en cours de drag depuis la hiérarchie — lu par InspectorPanel
    // pour les champs de type GameObject/Component assignables par drop.
    public static GameObject? DraggedObject { get; set; }

    /// Asset en cours de drag depuis le Project Panel — lu par InspectorPanel
    /// pour les champs de type Asset assignables par drop.
    public static string? DraggedAssetPath { get; set; }

    public static void ClearIfDeleted(GameObject obj)
    {
        if (Selected == obj) Selected = null;
        if (DraggedObject == obj) DraggedObject = null;
    }

    public static GizmoMode ActiveGizmoMode { get; set; } = GizmoMode.Move;
    public static GizmoSpace ActiveGizmoSpace { get; set; } = GizmoSpace.World;
}