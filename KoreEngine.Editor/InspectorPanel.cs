using ImGuiNET;
using KoreEngine.Core;
using KoreEngine.Engine;
using System.Reflection;

namespace KoreEngine.Editor;

public class InspectorPanel
{
    Renderer renderer;
    Type[]? componentTypes;
    string[] componentNames = Array.Empty<string>();
    string componentSearch = "";

    public InspectorPanel(Renderer renderer)
    {
        this.renderer = renderer;
        ScriptCompiler.OnCompileSuccess += () => componentTypes = null; // force rescan
    }

    public void Draw()
    {
        ImGui.Begin("Inspector");

        GameObject? obj = EditorSelection.Selected;

        if (obj == null)
        {
            ImGui.TextDisabled("Nothing selected.");
            ImGui.End();
            return;
        }

        // --- Nom ---
        string name = obj.Name;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("##name", ref name, 128))
            obj.Name = name;

        ImGui.Separator();

        // --- Transform ---
        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float px = obj.LocalPosition.X;
            float py = obj.LocalPosition.Y;

            DrawField("Local X", () =>
            {
                if (ImGui.DragFloat("##px", ref px, 0.5f))
                    obj.LocalPosition = new Vector2(px, obj.LocalPosition.Y);
            });
            DrawField("Local Y", () =>
            {
                if (ImGui.DragFloat("##py", ref py, 0.5f))
                    obj.LocalPosition = new Vector2(obj.LocalPosition.X, py);
            });

            float rot = obj.LocalRotation;
            DrawField("Rotation", () =>
            {
                if (ImGui.DragFloat("##rot", ref rot, 1f))
                    obj.LocalRotation = rot;
            });

            float sx = obj.LocalScale.X;
            float sy = obj.LocalScale.Y;
            DrawField("Scale X", () =>
            {
                if (ImGui.DragFloat("##sx", ref sx, 0.01f))
                    obj.LocalScale = new Vector2(sx, obj.LocalScale.Y);
            });
            DrawField("Scale Y", () =>
            {
                if (ImGui.DragFloat("##sy", ref sy, 0.01f))
                    obj.LocalScale = new Vector2(obj.LocalScale.X, sy);
            });

            if (obj.Parent != null)
            {
                DrawField("World X", () =>
                    ImGui.TextDisabled($"{obj.WorldPosition.X:F1}"));
                DrawField("World Y", () =>
                    ImGui.TextDisabled($"{obj.WorldPosition.Y:F1}"));
                DrawField("World Rotation", () =>
                    ImGui.TextDisabled($"{obj.WorldRotation:F1}"));
            }
        }

        // --- Composants ---
        if (obj.Components.Count > 0)
        {
            Component? pendingRemove = null;

            foreach (var c in obj.Components)
            {
                bool open = ImGui.CollapsingHeader($"{c.GetType().Name}##{c.GetHashCode()}");

                if (ImGui.BeginPopupContextItem($"cctx_{c.GetHashCode()}"))
                {
                    if (ImGui.MenuItem("Remove Component"))
                        pendingRemove = c;
                    ImGui.EndPopup();
                }

                if (open)
                {
                    ImGui.Indent();
                    var fields = c.GetInspectorFields().ToList();
                    if (fields.Count > 0)
                        foreach (var f in fields)
                            DrawFieldDescriptor(c, f);
                    else
                        DrawComponentAuto(c);
                    ImGui.Unindent();
                }
            }

            if (pendingRemove != null)
                obj.RemoveComponent(pendingRemove);
        }

        ImGui.Separator();

        float btnWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.Button("Add Component", new System.Numerics.Vector2(btnWidth, 0)))
        {
            EnsureComponentTypes();
            componentSearch = "";
            ImGui.OpenPopup("##add_component_popup");
        }

        DrawAddComponentPopup(obj);

        ImGui.End();
    }

    void DrawAddComponentPopup(GameObject obj)
    {
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 320), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##add_component_popup")) return;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##search", ref componentSearch, 128);
        ImGui.Separator();
        ImGui.BeginChild("##component_list", new System.Numerics.Vector2(0, 0));

        string filter = componentSearch.Trim().ToLowerInvariant();
        for (int i = 0; i < componentTypes!.Length; i++)
        {
            if (filter.Length > 0 &&
                !componentNames[i].ToLowerInvariant().Contains(filter)) continue;

            if (ImGui.Selectable(componentNames[i]))
            {
                var instance = (Component?)Activator.CreateInstance(componentTypes[i]);
                if (instance != null) obj.AddComponent(instance);
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    void EnsureComponentTypes()
    {
        if (componentTypes != null) return;
        componentTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                { return e.Types.Where(t => t != null).Cast<Type>(); }
            })
            .Where(t => t != null && !t.IsAbstract
                && t.IsSubclassOf(typeof(Component))
                && t.GetConstructor(Type.EmptyTypes) != null)
            .GroupBy(t => t.Name)
            .Select(g => g.Last())
            .OrderBy(t => t.Name)
            .ToArray();
        componentNames = componentTypes.Select(t => t.Name).ToArray();
    }

    // ---------------------------------------------------------------
    // Champs standard
    // ---------------------------------------------------------------

    // ---------------------------------------------------------------
    // Auto-draw par réflexion (comportement par défaut si DrawInspector
    // n'est pas surchargé — comme Unity expose les champs publics)
    // ---------------------------------------------------------------

    // État ouvert/fermé des items de ListField, indexé par
    // "{hash du component}_{label du ListField}_{index de l'item}".
    static readonly Dictionary<string, bool> listItemOpen = new();

    static void DrawFieldDescriptor(Component c, InspectorField f)
    {
        string baseId = $"{c.GetHashCode()}_{f.Label}";

        switch (f)
        {
            case TextField tf:
                DrawField(tf.Label, () => ImGui.TextDisabled(tf.Get()));
                break;

            case FloatField ff:
            {
                float v = ff.Get();
                DrawField(ff.Label, () =>
                {
                    bool changed = ff.Min != ff.Max
                        ? ImGui.DragFloat($"##{baseId}", ref v, ff.Speed, ff.Min, ff.Max)
                        : ImGui.DragFloat($"##{baseId}", ref v, ff.Speed);
                    if (changed) ff.Set?.Invoke(v);
                });
                break;
            }

            case IntField iF:
            {
                int v = iF.Get();
                DrawField(iF.Label, () =>
                {
                    bool changed = iF.Min != iF.Max
                        ? ImGui.DragInt($"##{baseId}", ref v, iF.Speed, iF.Min, iF.Max)
                        : ImGui.DragInt($"##{baseId}", ref v, iF.Speed);
                    if (changed) iF.Set?.Invoke(v);
                });
                break;
            }

            case BoolField bf:
            {
                bool v = bf.Get();
                DrawField(bf.Label, () =>
                {
                    if (ImGui.Checkbox($"##{baseId}", ref v)) bf.Set?.Invoke(v);
                });
                break;
            }

            case StringField sf:
            {
                string v = sf.Get();
                DrawField(sf.Label, () =>
                {
                    if (ImGui.InputText($"##{baseId}", ref v, (uint)sf.MaxLength))
                        sf.Set?.Invoke(v);
                });
                break;
            }

            case EnumField ef:
            {
                int idx = ef.Get();
                DrawField(ef.Label, () =>
                {
                    if (ImGui.Combo($"##{baseId}", ref idx, ef.Names, ef.Names.Length))
                        ef.Set?.Invoke(idx);
                });
                break;
            }

            case TextureField texF:
            {
                var (tex, path) = texF.Get();
                var (newTex, newPath) = DrawTextureField(texF.Label, tex, path);
                if (newPath != path || newTex != tex)
                    texF.Set?.Invoke(newTex, newPath);
                break;
            }

            case AudioClipField acf:
                {
                    string path = acf.Get();
                    string newPath = DrawAudioClipField(acf.Label, path);
                    if (newPath != path)
                        acf.Set?.Invoke(newPath);
                    break;
                }

            case ComponentRefField crf:
            {
                string popupId = $"picker_comp_{baseId}";
                if (pendingResults.TryGetValue(popupId, out var pendingGo))
                {
                    pendingResults.Remove(popupId);
                    crf.Set?.Invoke(pendingGo != null
                        ? pendingGo.GetComponent(crf.ComponentType)
                        : null);
                }

                var current = crf.Get();
                DrawPickerField(crf.Label, current?.Owner?.Name,
                    $"None ({crf.ComponentType.Name})",
                    popupId, crf.ComponentType,
                    go => pendingResults[popupId] = go,
                    () => pendingResults[popupId] = null);
                break;
            }

            case ActionField af:
            {
                float width = ImGui.GetContentRegionAvail().X;
                if (ImGui.Button($"{af.Label}##{baseId}", new System.Numerics.Vector2(width, 0)))
                    af.Action();
                if (af.Tooltip != null && ImGui.IsItemHovered())
                    ImGui.SetTooltip(af.Tooltip);
                break;
            }

            case ListField lf:
            {
                int count = lf.Count();
                if (ImGui.CollapsingHeader($"{lf.Label} ({count})##{baseId}"))
                {
                    ImGui.Indent();
                    int removeAt = -1;

                    for (int i = 0; i < count; i++)
                    {
                        string itemKey = $"{baseId}_{i}";
                        if (!listItemOpen.TryGetValue(itemKey, out bool isOpen)) isOpen = true;

                        ImGui.SetNextItemOpen(isOpen, ImGuiCond.Always);
                        bool open = ImGui.CollapsingHeader($"{lf.ItemHeader(i)}##{itemKey}");
                        listItemOpen[itemKey] = open;

                        if (ImGui.BeginPopupContextItem($"ctx_{itemKey}"))
                        {
                            if (lf.ItemContextActions != null)
                            {
                                foreach (var (label, action) in lf.ItemContextActions(i))
                                    if (ImGui.MenuItem(label)) action();
                                ImGui.Separator();
                            }
                            if (ImGui.MenuItem("Remove")) removeAt = i;
                            ImGui.EndPopup();
                        }

                        if (open)
                        {
                            ImGui.Indent();
                            foreach (var itemField in lf.ItemFields(i))
                                DrawFieldDescriptor(c, itemField);
                            ImGui.Unindent();
                        }
                    }

                    if (removeAt >= 0)
                        lf.RemoveItem?.Invoke(removeAt);

                    if (lf.AddItem != null && ImGui.Button($"+ Add##{baseId}"))
                        lf.AddItem();

                    ImGui.Unindent();
                }
                break;
            }
        }
    }

    static readonly HashSet<Type> SkippedTypes = new()
    {
        typeof(IntPtr), typeof(nint)
    };

    static void DrawComponentAuto(Component c)
    {
        var fields = c.GetType()
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<NonSerializedAttribute>() == null
                     && f.GetCustomAttribute<HideInInspectorAttribute>() == null
                     && !SkippedTypes.Contains(f.FieldType)
                     && !typeof(Delegate).IsAssignableFrom(f.FieldType));

        foreach (var field in fields)
        {
            string id = $"##{c.GetHashCode()}_{field.Name}";
            object? val = field.GetValue(c);
            Type type = field.FieldType;

            if (type == typeof(float))
            {
                float v = val is float f ? f : 0f;
                DrawField(field.Name, () =>
                {
                    if (ImGui.DragFloat(id, ref v, 0.1f))
                        field.SetValue(c, v);
                });
            }
            else if (type == typeof(int))
            {
                int v = val is int i ? i : 0;
                DrawField(field.Name, () =>
                {
                    if (ImGui.DragInt(id, ref v))
                        field.SetValue(c, v);
                });
            }
            else if (type == typeof(byte))
            {
                int v = val is byte b ? b : 0;
                DrawField(field.Name, () =>
                {
                    if (ImGui.DragInt(id, ref v, 1f, 0, 255))
                        field.SetValue(c, (byte)v);
                });
            }
            else if (type == typeof(bool))
            {
                bool v = val is bool b && b;
                DrawField(field.Name, () =>
                {
                    if (ImGui.Checkbox(id, ref v))
                        field.SetValue(c, v);
                });
            }
            else if (type == typeof(string))
            {
                // Champ XxxTexturePath → texture picker
                if (field.Name.EndsWith("Path"))
                {
                    string texFieldName = field.Name[..^4];
                    var texField = c.GetType().GetField(texFieldName,
                        BindingFlags.Public | BindingFlags.Instance);

                    if (texField?.FieldType == typeof(IntPtr))
                    {
                        string path = val as string ?? "";
                        IntPtr tex = texField.GetValue(c) is IntPtr t ? t : IntPtr.Zero;
                        (tex, path) = DrawTextureField(field.Name[..^4], tex, path);
                        field.SetValue(c, path);
                        texField.SetValue(c, tex);
                        continue;
                    }
                }

                string s = val as string ?? "";
                DrawField(field.Name, () =>
                {
                    if (ImGui.InputText(id, ref s, 256))
                        field.SetValue(c, s);
                });
            }
            else if (type == typeof(Vector2))
            {
                var v = val is Vector2 vec ? vec : new Vector2(0, 0);
                float x = v.X, y = v.Y;

                System.Numerics.Vector2 vector = new System.Numerics.Vector2(x, y);
                DrawField($"{field.Name}", () =>
                {
                    if (ImGui.DragFloat2($"{id}_vec", ref vector))
                        field.SetValue(c, new Vector2(vector.X, vector.Y));
                });

                ImGui.Separator();
            }
            else if (type == typeof(Color))
            {
                var col = val is Color color ? color : new Color(0, 0, 0);

                // Convertit 0-255 → 0-1 pour ImGui
                System.Numerics.Vector3 col3 = new System.Numerics.Vector3(
                    col.R / 255f, col.G / 255f, col.B / 255f);

                DrawField(field.Name, () =>
                {
                    if (ImGui.ColorPicker3($"{id}_col", ref col3))
                    {
                        // Reconvertit 0-1 → 0-255
                        field.SetValue(c, new Color(
                            (int)(col3.X * 255),
                            (int)(col3.Y * 255),
                            (int)(col3.Z * 255)));
                    }
                });
            }
            else if (type == typeof(Rectangle))
            {
                var r = val is Rectangle rect ? rect : new Rectangle(0, 0, 0, 0);
                int rx = r.X, ry = r.Y, rw = r.Width, rh = r.Height;
                DrawField($"{field.Name} X", () => { if (ImGui.DragInt($"{id}_rx", ref rx)) field.SetValue(c, new Rectangle(rx, r.Y, r.Width, r.Height)); });
                DrawField($"{field.Name} Y", () => { if (ImGui.DragInt($"{id}_ry", ref ry)) field.SetValue(c, new Rectangle(r.X, ry, r.Width, r.Height)); });

                DrawField($"{field.Name} W", () => { if (ImGui.DragInt($"{id}_rw", ref rw)) field.SetValue(c, new Rectangle(r.X, r.Y, rw, r.Height)); });
                DrawField($"{field.Name} H", () => { if (ImGui.DragInt($"{id}_rh", ref rh)) field.SetValue(c, new Rectangle(r.X, r.Y, r.Width, rh)); });
            }
            else if (type.IsEnum)
            {
                string[] names = Enum.GetNames(type);
                int index = val != null ? (int)val : 0;
                DrawField(field.Name, () =>
                {
                    if (ImGui.Combo(id, ref index, names, names.Length))
                        field.SetValue(c, Enum.ToObject(type, index));
                });
            }
            else if (typeof(Component).IsAssignableFrom(type))
            {
                // Référence à un Component → picker
                var current = val as Component;
                DrawField(field.Name, () =>
                {
                    float total = ImGui.GetContentRegionAvail().X;
                    float labelW = total * 0.4f;
                    float widgetW = total * 0.6f - 24f;
                    ImGui.SetNextItemWidth(widgetW);
                    string display = current?.Owner?.Name ?? $"None ({type.Name})";
                    ImGui.InputText($"{id}_display", ref display, 128,
                        ImGuiInputTextFlags.ReadOnly);
                    ImGui.SameLine();
                    if (ImGui.Button($"•{id}_btn", new System.Numerics.Vector2(20, 0)))
                    {
                        pickerPopupId = $"picker_{id}";
                        pickerSearch = "";
                        pickerFilterType = type;
                        ImGui.OpenPopup($"picker_{id}");
                    }
                    DrawPickerPopup($"picker_{id}",
                        obj => field.SetValue(c, obj.GetComponent(type)),
                        () => field.SetValue(c, null));
                });
            }
            else if (type == typeof(GameObject))
            {
                var current = val as GameObject;
                DrawField(field.Name, () =>
                {
                    float total = ImGui.GetContentRegionAvail().X;
                    float widgetW = total * 0.6f - 24f;
                    ImGui.SetNextItemWidth(widgetW);
                    string display = current?.Name ?? "None (GameObject)";
                    ImGui.InputText($"{id}_godisplay", ref display, 128,
                        ImGuiInputTextFlags.ReadOnly);
                    ImGui.SameLine();
                    if (ImGui.Button($"•{id}_gobtn", new System.Numerics.Vector2(20, 0)))
                    {
                        pickerPopupId = $"gopicker_{id}";
                        pickerSearch = "";
                        pickerFilterType = null;
                        ImGui.OpenPopup($"gopicker_{id}");
                    }
                    DrawPickerPopup($"gopicker_{id}",
                        obj => field.SetValue(c, obj),
                        () => field.SetValue(c, null));
                });
            }
            // Autres types non supportés → ignorés silencieusement
        }
    }

    public static void DrawField(string label, Action widget)
    {
        float total = ImGui.GetContentRegionAvail().X;
        float labelW = total * 0.4f;
        float widgetW = total * 0.6f;
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(widgetW);
        widget();
    }

    // ---------------------------------------------------------------
    // Object picker
    // ---------------------------------------------------------------

    static string pickerPopupId = "";
    static string pickerSearch = "";
    static Type? pickerFilterType;

    // Résultats en attente : quand l'utilisateur clique dans le picker,
    // on stocke ici le GameObject choisi, indexé par l'ID du popup.
    // Il est lu au début du frame SUIVANT par DrawObjectField/DrawComponentField.
    // Nécessaire car le picker est un popup ImGui qui vit sur plusieurs frames —
    // le callback est invoqué après que DrawObjectField a déjà retourné.
    static readonly Dictionary<string, GameObject?> pendingResults = new();

    public static GameObject? DrawObjectField(string label, GameObject? current)
    {
        string popupId = $"picker_go_{label}";

        // Applique le résultat en attente du frame précédent
        if (pendingResults.TryGetValue(popupId, out var pending))
        {
            current = pending;
            pendingResults.Remove(popupId);
        }

        DrawPickerField(label, current?.Name, "None (GameObject)",
            popupId, null, obj => pendingResults[popupId] = obj,
            () => pendingResults[popupId] = null);

        return current;
    }

    public static T? DrawComponentField<T>(string label, T? current) where T : Component
    {
        string popupId = $"picker_comp_{label}_{typeof(T).Name}";

        // Applique le résultat en attente du frame précédent
        if (pendingResults.TryGetValue(popupId, out var pending))
        {
            current = pending?.GetComponent<T>();
            pendingResults.Remove(popupId);
        }

        string display = current?.Owner != null
            ? $"{current.Owner.Name} ({typeof(T).Name})"
            : $"None ({typeof(T).Name})";

        DrawPickerField(label, current?.Owner?.Name, $"None ({typeof(T).Name})",
            popupId, typeof(T),
            obj => pendingResults[popupId] = obj,
            () => pendingResults[popupId] = null);

        return current;
    }

    static void DrawPickerField(string label, string? currentName, string noneLabel, string popupId, Type? filterType, Action<GameObject> onPick, Action onClear)
    {
        float total = ImGui.GetContentRegionAvail().X;
        float labelW = total * 0.4f;
        float widgetW = total * 0.6f - 24f;

        ImGui.Text(label);
        ImGui.SameLine(labelW);

        ImGui.SetNextItemWidth(widgetW);
        string display = currentName ?? noneLabel;
        ImGui.InputText($"##{popupId}_display", ref display, 128,
            ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGui.Button($"•##{popupId}_btn", new System.Numerics.Vector2(20, 0)))
        {
            pickerPopupId = popupId;
            pickerSearch = "";
            pickerFilterType = filterType;
            ImGui.OpenPopup(popupId);
        }

        DrawPickerPopup(popupId, onPick, onClear);
    }

    static void DrawPickerPopup(string popupId, Action<GameObject> onPick, Action onClear)
    {
        if (pickerPopupId != popupId) return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(260, 300), ImGuiCond.Always);
        if (!ImGui.BeginPopup(popupId)) return;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##picker_search", ref pickerSearch, 128);
        ImGui.Separator();

        ImGui.BeginChild("##picker_list", new System.Numerics.Vector2(0, 0));

        if (ImGui.Selectable("None"))
        {
            onClear();
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();

        string filter = pickerSearch.Trim().ToLowerInvariant();
        var objects = SceneManager.Current?.AllObjects ?? Enumerable.Empty<GameObject>();

        foreach (var obj in objects)
        {
            if (pickerFilterType != null &&
                obj.GetComponent(pickerFilterType) == null) continue;

            if (filter.Length > 0 &&
                !obj.Name.ToLowerInvariant().Contains(filter)) continue;

            string itemLabel = pickerFilterType != null
                ? $"{obj.Name} ({pickerFilterType.Name})"
                : obj.Name;

            if (ImGui.Selectable(itemLabel))
            {
                onPick(obj);
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    // ---------------------------------------------------------------
    // Texture picker
    // ---------------------------------------------------------------

    static string texPickerPopupId = "";
    static string texPickerSearch = "";

    // Résultats en attente (même pattern que le object picker)
    // Valeur = chemin du fichier choisi, "" = None
    static readonly Dictionary<string, string> pendingTextureResults = new();

    // Cache de la liste de fichiers — rescannée si le popup vient de s'ouvrir
    static List<string>? assetFiles;

    /// <summary>
    /// Champ texture assignable via un popup avec miniatures.
    /// Retourne le nouvel IntPtr (ou l'ancien si rien n'a changé).
    ///   Texture = InspectorPanel.DrawTextureField("Texture", Texture, texturePath);
    /// Le chemin est stocké séparément pour pouvoir afficher le nom et
    /// recharger la texture (le IntPtr seul ne contient pas l'info de chemin).
    /// </summary>
    public static (IntPtr texture, string path) DrawTextureField(
        string label, IntPtr currentTexture, string currentPath)
    {
        string popupId = $"tex_picker_{label}";

        // Applique le résultat en attente du frame précédent
        if (pendingTextureResults.TryGetValue(popupId, out var pendingPath))
        {
            currentPath = pendingPath;
            currentTexture = string.IsNullOrEmpty(pendingPath)
                ? IntPtr.Zero
                : TextureCache.Get(pendingPath);
            pendingTextureResults.Remove(popupId);
        }

        float total = ImGui.GetContentRegionAvail().X;
        float labelW = total * 0.4f;
        float widgetW = total * 0.6f - 44f; // place pour miniature + bouton

        ImGui.Text(label);
        ImGui.SameLine(labelW);

        // Miniature inline (32x32) si une texture est assignée
        if (currentTexture != IntPtr.Zero)
        {
            ImGui.Image(currentTexture, new System.Numerics.Vector2(32, 32));
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(widgetW);
        string display = string.IsNullOrEmpty(currentPath)
            ? "None" : Path.GetFileName(currentPath);
        ImGui.InputText($"##{popupId}_display", ref display, 256,
            ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGui.Button($"•##{popupId}_btn", new System.Numerics.Vector2(20, 0)))
        {
            texPickerPopupId = popupId;
            texPickerSearch = "";
            assetFiles = TextureCache.ScanAssets(Path.Combine(ProjectPanel.FindProjectRoot(),"Assets")).ToList(); // rescan au clic
            ImGui.OpenPopup(popupId);
        }

        DrawTexturePickerPopup(popupId);

        return (currentTexture, currentPath);
    }

    static void DrawTexturePickerPopup(string popupId)
    {
        if (texPickerPopupId != popupId) return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(360, 400), ImGuiCond.Always);
        if (!ImGui.BeginPopup(popupId)) return;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##tex_search", ref texPickerSearch, 128);
        ImGui.Separator();

        // "None" pour désassigner
        if (ImGui.Selectable("None"))
        {
            pendingTextureResults[popupId] = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();
        ImGui.BeginChild("##tex_list", new System.Numerics.Vector2(0, 0));

        string filter = texPickerSearch.Trim().ToLowerInvariant();
        float thumbSize = 64f;
        float padding = 8f;
        float cellW = thumbSize + padding;
        float panelW = ImGui.GetContentRegionAvail().X;
        int cols = Math.Max(1, (int)(panelW / cellW));
        int col = 0;

        foreach (var path in assetFiles ?? Enumerable.Empty<string>())
        {
            string fname = Path.GetFileName(path);
            if (filter.Length > 0 &&
                !fname.ToLowerInvariant().Contains(filter)) continue;

            IntPtr tex = TextureCache.Get(path);

            ImGui.BeginGroup();

            // Miniature ou placeholder gris si texture invalide
            if (tex != IntPtr.Zero)
                ImGui.Image(tex, new System.Numerics.Vector2(thumbSize, thumbSize));
            else
            {
                ImGui.Dummy(new System.Numerics.Vector2(thumbSize, thumbSize));
                var dl = ImGui.GetWindowDrawList();
                var pos = ImGui.GetItemRectMin();
                dl.AddRectFilled(pos,
                    new System.Numerics.Vector2(pos.X + thumbSize, pos.Y + thumbSize),
                    0xFF555555);
                dl.AddText(new System.Numerics.Vector2(pos.X + 4, pos.Y + thumbSize * 0.5f - 6),
                    0xFFAAAAAA, "?");
            }

            // Nom tronqué sous la miniature
            string shortName = fname.Length > 10 ? fname[..10] + "…" : fname;
            ImGui.TextUnformatted(shortName);

            // Clic sur le groupe = sélection
            if (ImGui.IsItemClicked() ||
                (ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
                 ImGui.IsItemHovered()))
            {
                pendingTextureResults[popupId] = path;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndGroup();

            // Tooltip au survol du groupe
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(path);

            // Disposition en grille
            col++;
            if (col < cols) ImGui.SameLine(col * cellW);
            else col = 0;
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    // ---------------------------------------------------------------
    // Audio clip picker
    // ---------------------------------------------------------------

    static string audioPickerPopupId = "";
    static string audioPickerSearch = "";
    static readonly Dictionary<string, string> pendingAudioResults = new();
    static List<string>? audioFiles;

    static readonly string[] AudioExtensions = { ".wav", ".ogg", ".mp3" };

    /// <summary>
    /// Champ clip audio assignable via un popup listant les fichiers audio
    /// des Assets, avec icône par extension (EditorIcons couvre déjà wav/ogg/mp3).
    /// </summary>
    public static string DrawAudioClipField(string label, string currentPath)
    {
        string popupId = $"audio_picker_{label}";

        if (pendingAudioResults.TryGetValue(popupId, out var pendingPath))
        {
            currentPath = pendingPath;
            pendingAudioResults.Remove(popupId);
        }

        float total = ImGui.GetContentRegionAvail().X;
        float labelW = total * 0.4f;
        float widgetW = total * 0.6f - 24f;

        ImGui.Text(label);
        ImGui.SameLine(labelW);

        ImGui.SetNextItemWidth(widgetW);
        string display = string.IsNullOrEmpty(currentPath) ? "None" : Path.GetFileName(currentPath);
        ImGui.InputText($"##{popupId}_display", ref display, 256, ImGuiInputTextFlags.ReadOnly);

        ImGui.SameLine();
        if (ImGui.Button($"•##{popupId}_btn", new System.Numerics.Vector2(20, 0)))
        {
            audioPickerPopupId = popupId;
            audioPickerSearch = "";
            string assetsRoot = Path.Combine(ProjectPanel.FindProjectRoot(), "Assets");
            audioFiles = Directory.Exists(assetsRoot)
                ? Directory.GetFiles(assetsRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f)
                    .ToList()
                : new List<string>();
            ImGui.OpenPopup(popupId);
        }

        DrawAudioPickerPopup(popupId);

        return currentPath;
    }

    static void DrawAudioPickerPopup(string popupId)
    {
        if (audioPickerPopupId != popupId) return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 360), ImGuiCond.Always);
        if (!ImGui.BeginPopup(popupId)) return;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##audio_search", ref audioPickerSearch, 128);
        ImGui.Separator();

        if (ImGui.Selectable("None"))
        {
            pendingAudioResults[popupId] = "";
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();
        ImGui.BeginChild("##audio_list", new System.Numerics.Vector2(0, 0));

        string filter = audioPickerSearch.Trim().ToLowerInvariant();

        foreach (var path in audioFiles ?? Enumerable.Empty<string>())
        {
            string fname = Path.GetFileName(path);
            if (filter.Length > 0 && !fname.ToLowerInvariant().Contains(filter)) continue;

            IntPtr icon = EditorIcons.Get(Path.GetExtension(path));
            if (icon != IntPtr.Zero)
            {
                ImGui.Image(icon, new System.Numerics.Vector2(20, 20));
                ImGui.SameLine();
            }

            if (ImGui.Selectable(fname))
            {
                pendingAudioResults[popupId] = path;
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(path);
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }
    public void Destroy() => renderer.Clear();
}