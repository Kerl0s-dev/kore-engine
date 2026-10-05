using ImGuiNET;
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

        // --- Composants ---
        if (obj.Components.Count > 0)
        {
            Component? pendingRemove = null;

            foreach (var c in obj.Components)
            {
                bool open = ImGui.CollapsingHeader($"{c.GetType().Name}##{c.GetHashCode()}");

                // Vérification si le composant possède l'attribut UnremovableComponent
                bool isUnremovable = c.GetType().GetCustomAttribute<UnremovableComponentAttribute>() != null;

                if (ImGui.BeginPopupContextItem($"cctx_{c.GetHashCode()}"))
                {
                    if (isUnremovable)
                    {
                        // Option : désactivation du menu de suppression
                        ImGui.BeginDisabled();
                        ImGui.MenuItem("Remove Component (Required)");
                        ImGui.EndDisabled();
                    }
                    else
                    {
                        if (ImGui.MenuItem("Remove Component"))
                        {
                            pendingRemove = c;
                        }
                    }
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
            if (componentTypes[i] == typeof(Transform))
                continue;

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
    // Auto-draw par réflexion
    // ---------------------------------------------------------------

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
                            ? ImGui.DragFloat($"##{baseId}", ref v, ff.Value, ff.Min, ff.Max)
                            : ImGui.DragFloat($"##{baseId}", ref v, ff.Value);
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
                            ? ImGui.DragInt($"##{baseId}", ref v, iF.Value, iF.Min, iF.Max)
                            : ImGui.DragInt($"##{baseId}", ref v, iF.Value);
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
                    DrawPickerField(crf.Label, current?.gameObject?.Name,
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

                System.Numerics.Vector2 vector = new System.Numerics.Vector2(v.X, v.Y);
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

                System.Numerics.Vector3 col3 = new System.Numerics.Vector3(
                    col.R / 255f, col.G / 255f, col.B / 255f);

                DrawField(field.Name, () =>
                {
                    if (ImGui.ColorPicker3($"{id}_col", ref col3))
                    {
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
                var current = val as Component;
                DrawField(field.Name, () =>
                {
                    float total = ImGui.GetContentRegionAvail().X;
                    float widgetW = total * 0.6f - 24f;
                    ImGui.SetNextItemWidth(widgetW);
                    string display = current?.gameObject?.Name ?? $"None ({type.Name})";
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

    static readonly Dictionary<string, GameObject?> pendingResults = new();

    public static GameObject? DrawObjectField(string label, GameObject? current)
    {
        string popupId = $"picker_go_{label}";

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

        if (pendingResults.TryGetValue(popupId, out var pending))
        {
            current = pending?.GetComponent<T>();
            pendingResults.Remove(popupId);
        }

        DrawPickerField(label, current?.gameObject?.Name, $"None ({typeof(T).Name})",
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

    static readonly Dictionary<string, string> pendingTextureResults = new();
    static List<string>? assetFiles;

    public static (IntPtr texture, string path) DrawTextureField(
        string label, IntPtr currentTexture, string currentPath)
    {
        string popupId = $"tex_picker_{label}";

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
        float widgetW = total * 0.6f - 44f;

        ImGui.Text(label);
        ImGui.SameLine(labelW);

        ImGui.BeginGroup();

        if (currentTexture != IntPtr.Zero)
        {
            ImGui.Image(currentTexture, new System.Numerics.Vector2(32, 32));
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(widgetW);
        string display = string.IsNullOrEmpty(currentPath)
            ? "None"
            : Path.GetFileName(currentPath);

        ImGui.InputText($"##{popupId}_display", ref display, 256, ImGuiInputTextFlags.ReadOnly);

        ImGui.EndGroup();

        if (ImGui.BeginPopupContextItem($"##{popupId}_ctx"))
        {
            if (ImGui.MenuItem("Clear / None"))
            {
                currentPath = "";
                currentTexture = IntPtr.Zero;
            }
            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(currentPath))
        {
            ImGui.SetTooltip(currentPath);
        }

        ImGui.SameLine();
        if (ImGui.Button($"•##{popupId}_btn", new System.Numerics.Vector2(20, 0)))
        {
            texPickerPopupId = popupId;
            texPickerSearch = "";
            assetFiles = ProjectPanel.GetAllProjectFiles();
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
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga")) continue;

            string fname = Path.GetFileName(path);
            if (filter.Length > 0 && !fname.ToLowerInvariant().Contains(filter)) continue;

            IntPtr tex = TextureCache.Get(path);

            ImGui.BeginGroup();

            if (tex != IntPtr.Zero)
            {
                ImGui.Image(tex, new System.Numerics.Vector2(thumbSize, thumbSize));
            }
            else
            {
                ImGui.Dummy(new System.Numerics.Vector2(thumbSize, thumbSize));
                var dl = ImGui.GetWindowDrawList();
                var pos = ImGui.GetItemRectMin();
                dl.AddRectFilled(pos, new System.Numerics.Vector2(pos.X + thumbSize, pos.Y + thumbSize), 0xFF333333);
            }

            ImGui.TextUnformatted(fname);
            ImGui.EndGroup();

            if (ImGui.IsItemClicked())
            {
                pendingTextureResults[popupId] = path;
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(path);
            }

            col++;
            if (col < cols)
            {
                ImGui.SameLine();
            }
            else
            {
                col = 0;
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    // ---------------------------------------------------------------
    // Audio Clip picker
    // ---------------------------------------------------------------

    static string audioPickerPopupId = "";
    static string audioPickerSearch = "";
    static readonly Dictionary<string, string> pendingAudioResults = new();

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

        if (ImGui.BeginPopupContextItem($"##{popupId}_ctx"))
        {
            if (ImGui.MenuItem("Clear / None"))
            {
                currentPath = "";
            }
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button($"•##{popupId}_btn", new System.Numerics.Vector2(20, 0)))
        {
            audioPickerPopupId = popupId;
            audioPickerSearch = "";
            assetFiles = ProjectPanel.GetAllProjectFiles();
            ImGui.OpenPopup(popupId);
        }

        DrawAudioPickerPopup(popupId);

        return currentPath;
    }

    static void DrawAudioPickerPopup(string popupId)
    {
        if (audioPickerPopupId != popupId) return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 320), ImGuiCond.Always);
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

        foreach (var path in assetFiles ?? Enumerable.Empty<string>())
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".wav" or ".mp3" or ".ogg" or ".flac")) continue;

            string fname = Path.GetFileName(path);
            if (filter.Length > 0 && !fname.ToLowerInvariant().Contains(filter)) continue;

            if (ImGui.Selectable(fname))
            {
                pendingAudioResults[popupId] = path;
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(path);
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }
}