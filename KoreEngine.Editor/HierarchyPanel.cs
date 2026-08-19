using ImGuiNET;
using KoreEngine.Core;
using KoreEngine.Engine;

namespace KoreEngine.Editor;

public class HierarchyPanel
{
    GameObject? pendingDelete;

    // Source du drag — désormais dans EditorSelection pour être accessible
    // depuis InspectorPanel (champs assignables par drop).

    public void Draw()
    {
        ImGui.Begin("Hierarchy");

        Scene? scene = SceneManager.Current;

        if (scene == null)
        {
            ImGui.TextDisabled("No scene loaded.");
            ImGui.End();
            return;
        }

        // --- En-tête : nom de la scène actuelle ---
        string sceneName = SceneManager.CurrentSceneName ?? scene.Name;
        ImGui.TextDisabled(sceneName);
        ImGui.Separator();

        // --- Arbre des objets ---
        foreach (var obj in scene.RootObjects.ToList())
            DrawNode(obj, scene);

        // --- Zone de drop "racine" en bas de la liste ---
        // Permet de reparenter un objet draggé vers la racine de la scène
        // en le déposant dans l'espace vide sous tous les objets.
        ImGui.Dummy(new System.Numerics.Vector2(
            ImGui.GetContentRegionAvail().X, 20f));

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("GAMEOBJECT");
            unsafe
            {
                if (payload.NativePtr != null && EditorSelection.DraggedObject != null)
                {
                    EditorSelection.DraggedObject.SetParent(null, scene);
                    EditorSelection.DraggedObject = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        // --- Menu contextuel sur l'espace vide (clic droit hors d'un objet) ---
        if (ImGui.BeginPopupContextWindow("##ctx_empty",
            ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCreateMenu(scene, null);

            ImGui.Separator();

            if (ImGui.MenuItem("Save Scene"))
            {
                string? name = SceneManager.CurrentSceneName;
                if (name != null)
                {
                    SceneManager.SaveCurrentScene();
                }
            }

            ImGui.EndPopup();
        }

        // --- Suppression différée ---
        if (pendingDelete != null)
        {
            EditorSelection.ClearIfDeleted(pendingDelete);
            scene.Remove(pendingDelete);
            pendingDelete = null;
        }

        ImGui.End();
    }

    void DrawNode(GameObject obj, Scene scene)
    {
        bool isSelected = EditorSelection.Selected == obj;
        bool hasChildren = obj.Children.Count > 0;

        ImGuiTreeNodeFlags flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.SpanAvailWidth;

        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;

        bool open = ImGui.TreeNodeEx(
            $"{obj.Name}##{obj.GetHashCode()}", flags);

        // Sélection au clic (pas sur la flèche — OpenOnArrow s'en charge)
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
            EditorSelection.Selected = obj;

        // --- Drag source ---
        if (ImGui.BeginDragDropSource())
        {
            EditorSelection.DraggedObject = obj;
            ImGui.SetDragDropPayload("GAMEOBJECT", IntPtr.Zero, 0);
            ImGui.Text($"↕ {obj.Name}"); // aperçu pendant le drag
            ImGui.EndDragDropSource();
        }

        // --- Drop target : devient enfant de cet objet ---
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("GAMEOBJECT");
            unsafe
            {
                if (payload.NativePtr != null && EditorSelection.DraggedObject != null
                    && EditorSelection.DraggedObject != obj
                    && !obj.IsDescendantOf(EditorSelection.DraggedObject))
                {
                    EditorSelection.DraggedObject.SetParent(obj, scene);
                    EditorSelection.DraggedObject = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        // --- Menu contextuel sur l'objet ---
        if (ImGui.BeginPopupContextItem($"ctx_{obj.GetHashCode()}"))
        {
            ImGui.TextDisabled("Rename");
            string name = obj.Name;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.InputText("##rename", ref name, 128))
                obj.Name = name;

            ImGui.Separator();

            DrawCreateMenu(scene, obj); // enfants du cet objet

            ImGui.Separator();

            if (ImGui.MenuItem("Duplicate"))
                DuplicateObject(obj, scene);

            if (ImGui.MenuItem("Create Prefab"))
                CreatePrefab(obj);

            ImGui.Separator();

            if (ImGui.MenuItem("Delete"))
                pendingDelete = obj;

            ImGui.EndPopup();
        }

        // --- Enfants (récursif) ---
        if (open)
        {
            foreach (var child in obj.Children.ToList())
                DrawNode(child, scene);

            ImGui.TreePop();
        }
    }

    /// <summary>
    /// Duplique un objet (et sa hiérarchie d'enfants) via GameObject.Instantiate,
    /// puis le rattache au même parent que l'original — Instantiate() root
    /// par défaut sur la scène, ce qui conviendrait pour un spawn en jeu mais
    /// surprendrait ici : "Dupliquer" doit garder l'objet à côté de sa source.
    /// </summary>
    void DuplicateObject(GameObject obj, Scene scene)
    {
        var clone = obj.Instantiate(scene);

        if (obj.Parent != null)
            clone.SetParent(obj.Parent, scene);

        EditorSelection.Selected = clone;
    }

    /// <summary>
    /// Sauvegarde l'objet (et ses enfants) dans Assets/Prefabs/{Name}.kprefab,
    /// réutilisable ensuite via "Create > Instantiate Prefab" sur n'importe
    /// quelle scène. Chemin fixe pour rester simple — utilise "Rename" dans
    /// le Project Panel pour déplacer/renommer le fichier après coup.
    /// </summary>
    void CreatePrefab(GameObject obj)
    {
        string dir = Path.Combine(SceneManager.AssetsDirectory, "Prefabs");
        string path = Path.Combine(dir, $"{obj.Name}.kprefab");
        PrefabManager.Save(obj, path);
    }

    /// <summary>
    /// Sous-menu "Create" affiché dans les deux menus contextuels.
    /// parent == null → crée à la racine de la scène.
    /// parent != null → crée en tant qu'enfant de l'objet.
    /// </summary>
    void DrawCreateMenu(Scene scene, GameObject? parent)
    {
        if (!ImGui.BeginMenu("Create")) return;

        if (ImGui.MenuItem("Empty"))
            CreatePredefined("Empty", scene, parent);

        ImGui.Separator();

        if (ImGui.MenuItem("Camera"))
            CreatePredefined("Camera", scene, parent);

        if (ImGui.MenuItem("Rect"))
            CreatePredefined("Rect", scene, parent);

        if (ImGui.MenuItem("Sprite"))
            CreatePredefined("Sprite", scene, parent);

        if (ImGui.MenuItem("Physics Object"))
            CreatePredefined("Physics", scene, parent);

        ImGui.Separator();

        if (ImGui.MenuItem("UI Canvas"))
            CreatePredefined("UICanvas", scene, parent);

        if (ImGui.MenuItem("UI Button"))
            CreatePredefined("UIButton", scene, parent);

        if (ImGui.MenuItem("UI Image"))
            CreatePredefined("UIImage", scene, parent);

        ImGui.Separator();

        if (ImGui.BeginMenu("Instantiate Prefab"))
        {
            var prefabs = PrefabManager.PrefabFiles().ToList();

            if (prefabs.Count == 0)
                ImGui.TextDisabled("Aucun prefab (clic droit sur un objet > Create Prefab)");

            foreach (var path in prefabs)
            {
                if (ImGui.MenuItem(Path.GetFileNameWithoutExtension(path)))
                {
                    var obj = PrefabManager.Instantiate(path, scene, parent);
                    EditorSelection.Selected = obj;
                }
            }

            ImGui.EndMenu();
        }

        ImGui.EndMenu();
    }

    void CreatePredefined(string type, Scene scene, GameObject? parent)
    {
        var obj = type switch
        {
            "Camera" => CreateWithComponents("Camera",
                new Components.Camera()),

            "Rect" => CreateWithComponents("Rect",
                new Components.RectRenderer() { Size = new Vector2(16, 16) }),

            "Sprite" => CreateWithComponents("Sprite",
                new Components.SpriteRenderer() { Size = new Vector2(16, 16) }),

            "Physics" => CreateWithComponents("Physics Object",
                new Components.PhysicsBody(),
                new Components.Collider()),

            "UICanvas" => CreateWithComponents("Canvas",
                new Components.UI.UICanvas()),

            "UIButton" => CreateWithComponents("Button",
                new Components.UI.UIButton()),

            "UIImage" => CreateWithComponents("Image",
                new Components.UI.UIImage()),

            _ => new GameObject("GameObject")
        };

        scene.Add(obj);

        if (parent != null)
            obj.SetParent(parent, scene);

        EditorSelection.Selected = obj;
    }

    static GameObject CreateWithComponents(string name, params Component[] components)
    {
        var obj = new GameObject(name);
        foreach (var c in components)
            obj.AddComponent(c);
        return obj;
    }
}