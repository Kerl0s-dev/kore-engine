using ImGuiNET;
using System.Globalization;
using System.Reflection;

namespace KoreEngine;

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

        if (EditorSelection.SelectedAssetPath is { } assetPath)
        {
            AssetInspector.Draw(assetPath);
            ImGui.End();
            return;
        }

        GameObject? obj = EditorSelection.Selected;

        if (obj == null)
        {
            ImGui.TextDisabled("Nothing selected.");
            ImGui.End();
            return;
        }

        bool enabled = obj.Enabled;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.Checkbox("##enabled", ref enabled))
            obj.Enabled = enabled;

        if (obj.Enabled && !obj.ActiveInHierarchy)
            ImGui.TextDisabled("Désactivé par un parent.");

        ImGui.SameLine();

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

    internal static void DrawFieldDescriptor(object c, InspectorField f)
    {
        string baseId = $"{c.GetHashCode()}_{f.Label}";

        switch (f)
        {
            case TextField tf:
            {
                DrawField(tf.Label, () => ImGui.TextDisabled(tf.Get()));
                break;
            }

            case FloatField ff:
                {
                    float v = ff.Get();
                    DrawField(ff.Label, () =>
                    {
                        bool changed;
                        if (ff.Slider && ff.Min != ff.Max)
                            changed = ImGui.SliderFloat($"##{baseId}", ref v, ff.Min, ff.Max, "%.3f", ImGuiSliderFlags.AlwaysClamp);
                        else if (ff.Min != ff.Max)
                            changed = ImGui.DragFloat($"##{baseId}", ref v, ff.Value, ff.Min, ff.Max);
                        else
                            changed = ImGui.DragFloat($"##{baseId}", ref v, ff.Value);
                        if (changed) ff.Set?.Invoke(v);
                    });
                    break;
                }

            case IntField iF:
            {
                int v = iF.Get();
                    DrawField(iF.Label, () =>
                    {
                        bool changed;
                        if (iF.Slider && iF.Min != iF.Max)
                            changed = ImGui.SliderInt($"##{baseId}", ref v, iF.Min, iF.Max, "%d", ImGuiSliderFlags.AlwaysClamp);
                        else if (iF.Min != iF.Max)
                            changed = ImGui.DragInt($"##{baseId}", ref v, iF.Value, iF.Min, iF.Max);
                        else
                            changed = ImGui.DragInt($"##{baseId}", ref v, iF.Value);
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
                var (newTex, newPath) = DrawTextureField(texF.Label, tex, path, baseId);
                if (newPath != path || newTex != tex)
                    texF.Set?.Invoke(newTex, newPath);
                break;
            }

            case ColorField colF:
            {
                Color col = colF.Get();

                System.Numerics.Vector4 col4 = new System.Numerics.Vector4(
                    col.R / 255f, col.G / 255f, col.B / 255f, col.A / 255f);

                DrawField(colF.Label, () =>
                {
                    if (ImGui.ColorPicker4($"{baseId}_col", ref col4))
                    {
                        colF.Set?.Invoke(new Color(
                            (int)(col4.X * 255),
                            (int)(col4.Y * 255),
                            (int)(col4.Z * 255),
                            (int)(col4.W * 255)
                        ));
                    }
                });
                break;
            }

            case AudioClipField acf:
            {
                string path = acf.Get();
                string newPath = DrawAssetPathField(acf.Label, path, AudioExtensions, baseId);
                if (newPath != path)
                    acf.Set?.Invoke(newPath);
                break;
            }

            case ComponentRefField crf:
            {
                var current = crf.Get();
                var picked = DrawComponentPicker(crf.Label, $"cref_{baseId}", crf.ComponentType, current);
                if (!ReferenceEquals(picked, current)) crf.Set?.Invoke(picked);
                break;
            }

            case ButtonField af:
            {
                float width = ImGui.GetContentRegionAvail().X;
                if (ImGui.Button($"{af.Label}##{baseId}", new System.Numerics.Vector2(width, 0)))
                    af.Action();
                if (af.Tooltip != null && ImGui.IsItemHovered())
                    ImGui.SetTooltip(af.Tooltip);
                break;
            }

            case KoreEventField kef:
            {
                DrawKoreEvent(kef.Label, kef.Get(), baseId);
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
                var range = field.GetCustomAttribute<RangeAttribute>();
                DrawField(field.Name, () =>
                {
                    bool changed = range != null
                        ? ImGui.SliderFloat(id, ref v, range.Min, range.Max, "%.3f", ImGuiSliderFlags.AlwaysClamp)
                        : ImGui.DragFloat(id, ref v, 0.1f);
                    if (changed) field.SetValue(c, v);
                });
            }
            else if (type == typeof(int))
            {
                int v = val is int i ? i : 0;
                var range = field.GetCustomAttribute<RangeAttribute>();
                DrawField(field.Name, () =>
                {
                    bool changed = range != null
                        ? ImGui.SliderInt(id, ref v, (int)range.Min, (int)range.Max, "%d", ImGuiSliderFlags.AlwaysClamp)
                        : ImGui.DragInt(id, ref v);
                    if (changed) field.SetValue(c, v);
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
                var assetAttr = field.GetCustomAttribute<AssetPathAttribute>();
                if (assetAttr != null)
                {
                    string current = val as string ?? "";
                    
                    string chosen = DrawAssetPathField(field.Name, current, assetAttr.Extensions,
                        $"{c.GetHashCode()}_{field.Name}");
                    if (chosen != current) field.SetValue(c, chosen);
                    continue;
                }
                if (field.Name.EndsWith("Path"))
                {
                    string texFieldName = field.Name[..^4];
                    var texField = c.GetType().GetField(texFieldName,
                        BindingFlags.Public | BindingFlags.Instance);

                    if (texField?.FieldType == typeof(IntPtr))
                    {
                        string path = val as string ?? "";
                        IntPtr tex = texField.GetValue(c) is IntPtr t ? t : IntPtr.Zero;

                        var (newTex, newPath) = DrawTextureField(field.Name[..^4], tex, path,
                            $"{c.GetHashCode()}_{field.Name}");

                        if (newPath != path) field.SetValue(c, newPath);
                        if (newTex != tex) texField.SetValue(c, newTex);
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

                System.Numerics.Vector4 col4 = new System.Numerics.Vector4(
                    col.R / 255f, col.G / 255f, col.B / 255f, col.A / 255f);

                DrawField(field.Name, () =>
                {
                    if (ImGui.ColorPicker4($"{id}_col", ref col4))
                    {
                        field.SetValue(c, new Color(
                            (int)(col4.X * 255),
                            (int)(col4.Y * 255),
                            (int)(col4.Z * 255),
                            (int)(col4.W * 255)
                        ));
                    }
                });
            }
            else if (type == typeof(Rectangle))
            {
                var r = val is Rectangle rect ? rect : new Rectangle(0, 0, 0, 0);
                float rx = r.X, ry = r.Y, rw = r.Width, rh = r.Height;
                DrawField($"{field.Name} X", () => { if (ImGui.DragFloat($"{id}_rx", ref rx)) field.SetValue(c, new Rectangle(rx, r.Y, r.Width, r.Height)); });
                DrawField($"{field.Name} Y", () => { if (ImGui.DragFloat($"{id}_ry", ref ry)) field.SetValue(c, new Rectangle(r.X, ry, r.Width, r.Height)); });
                DrawField($"{field.Name} Width", () => { if (ImGui.DragFloat($"{id}_rw", ref rw)) field.SetValue(c, new Rectangle(r.X, r.Y, rw, r.Height)); });
                DrawField($"{field.Name} Height", () => { if (ImGui.DragFloat($"{id}_rh", ref rh)) field.SetValue(c, new Rectangle(r.X, r.Y, r.Width, rh)); });
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
                var picked = DrawComponentPicker(field.Name, $"{c.GetHashCode()}_{field.Name}", type, val as Component);
                if (!ReferenceEquals(picked, val)) field.SetValue(c, picked);
            }
            else if (type == typeof(KoreEvent))
            {
                var ev = val as KoreEvent ?? new KoreEvent();
                if (val == null) field.SetValue(c, ev);
                DrawKoreEvent(field.Name, ev, $"{c.GetHashCode()}_{field.Name}");
            }
            else if (type == typeof(GameObject))
            {
                var picked = DrawObjectField(field.Name, val as GameObject, $"{c.GetHashCode()}_{field.Name}");
                if (!ReferenceEquals(picked, val)) field.SetValue(c, picked);
            }
        }
    }

    const float LabelRatio = 0.6f;
    const float PickerBtnW = 20f;

    /// <summary>Largeur restante jusqu'au bord droit, en réservant le bouton "•".</summary>
    static float WidthBeforeButton() =>
        ImGui.GetContentRegionAvail().X - PickerBtnW - ImGui.GetStyle().ItemSpacing.X;

    public static void DrawField(string label, Action widget)
    {
        float labelW = ImGui.GetContentRegionAvail().X * LabelRatio;
        ImGui.AlignTextToFramePadding();          // label centré verticalement sur le widget
        ImGui.Text(label);
        ImGui.SameLine(labelW);                   // début de colonne identique pour tous
        ImGui.SetNextItemWidth(-1f);              // remplit jusqu'au bord droit
        widget();
    }

    static void DrawKoreEvent(string label, KoreEvent ev, string id)
    {
        if (!ImGui.TreeNodeEx($"{label} ({ev.Listeners.Count})##{id}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        int removeAt = -1;

        for (int i = 0; i < ev.Listeners.Count; i++)
        {
            var l = ev.Listeners[i];
            string lid = $"{id}_{i}";
            ImGui.PushID(lid);

            // --- Objet cible ---
            var current = l.ResolveObject();
            string noneLabel = string.IsNullOrEmpty(l.TargetObject)
                ? "None (GameObject)"
                : $"{l.TargetObject} (missing)";   // cible sauvegardée mais absente de la scène

            var target = DrawObjectField("Object", current, lid, noneLabel);
            if (!ReferenceEquals(target, current))
            {
                l.TargetObject = target?.Name ?? "";
                l.ComponentType = ""; l.MethodName = ""; l.Argument = "";
            }

            if (target != null)
            {
                // --- Composant ---
                var comps = target.Components.ToList();
                string[] cNames = comps.Select(x => x.GetType().Name).ToArray();
                int ci = Array.IndexOf(cNames, l.ComponentType);
                DrawField("Component", () =>
                {
                    if (ImGui.Combo("##comp", ref ci, cNames, cNames.Length))
                    {
                        l.ComponentType = cNames[ci];
                        l.MethodName = ""; l.Argument = "";
                    }
                });

                // --- Méthode ---
                if (ci >= 0)
                {
                    var methods = PersistentListener.GetCallableMethods(comps[ci].GetType()).ToList();
                    string[] mNames = methods.Select(m => m.Name).ToArray();
                    int mi = Array.IndexOf(mNames, l.MethodName);
                    DrawField("Method", () =>
                    {
                        if (ImGui.Combo("##method", ref mi, mNames, mNames.Length))
                        {
                            l.MethodName = mNames[mi];
                            l.Argument = "";
                        }
                    });

                    // --- Argument ---
                    if (mi >= 0)
                    {
                        var ps = methods[mi].GetParameters();
                        if (ps.Length == 1)
                            DrawListenerArg(l, ps[0].ParameterType);
                    }
                }
            }

            if (ImGui.SmallButton("Remove")) removeAt = i;
            ImGui.Separator();
            ImGui.PopID();
        }

        if (removeAt >= 0) ev.Listeners.RemoveAt(removeAt);
        if (ImGui.Button($"+ Add Listener##{id}")) ev.Listeners.Add(new PersistentListener());

        ImGui.TreePop();
    }

    static void DrawListenerArg(PersistentListener l, Type t)
    {
        if (t == typeof(string))
        {
            string s = l.Argument;
            DrawField("Arg", () => { if (ImGui.InputText("##arg", ref s, 256)) l.Argument = s; });
        }
        else if (t == typeof(int))
        {
            int v = (int)PersistentListener.Parse(t, l.Argument)!;
            DrawField("Arg", () => { if (ImGui.DragInt("##arg", ref v)) l.Argument = v.ToString(); });
        }
        else if (t == typeof(float))
        {
            float v = (float)PersistentListener.Parse(t, l.Argument)!;
            DrawField("Arg", () => { if (ImGui.DragFloat("##arg", ref v, 0.1f)) l.Argument = v.ToString(CultureInfo.InvariantCulture); });
        }
        else if (t == typeof(bool))
        {
            bool v = (bool)PersistentListener.Parse(t, l.Argument)!;
            DrawField("Arg", () => { if (ImGui.Checkbox("##arg", ref v)) l.Argument = v.ToString(); });
        }
    }

    // ---------------------------------------------------------------
    // Pickers — briques communes
    // ---------------------------------------------------------------

    // Une seule popup de sélection est ouverte à la fois : un état partagé suffit.
    static string activePopupId = "";
    static string pickerSearch = "";

    // Choix fait dans une popup, appliqué à la frame suivante : popupId → élément (null = None)
    static readonly Dictionary<string, object?> pending = new();
    static List<string>? assetFiles;

    static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg", ".flac" };
    static readonly string[] ImageExtensions = { ".png", ".bmp" };

    static void RefreshAssetFiles() => assetFiles = ProjectPanel.GetAllProjectFiles();

    static IEnumerable<string> FilterAssets(string[] extensions, string filter) =>
        (assetFiles ?? Enumerable.Empty<string>()).Where(p =>
            extensions.Contains(Path.GetExtension(p).ToLowerInvariant()) &&
            (filter.Length == 0 || Path.GetFileName(p).ToLowerInvariant().Contains(filter)));

    static IEnumerable<GameObject> SceneObjects(string filter) =>
        (SceneManager.Current?.AllObjects ?? Enumerable.Empty<GameObject>())
            .Where(o => filter.Length == 0 || o.Name.ToLowerInvariant().Contains(filter));

    /// <summary>
    /// Ligne commune à tous les pickers : libellé à gauche ; à droite, aperçu optionnel +
    /// champ en lecture seule + bouton "•" qui ouvre la popup. Clic droit sur le champ →
    /// "Clear / None" si onClear est fourni.
    /// </summary>
    static void DrawPickerRow(string label, string display, string popupId,
        string? tooltip = null, Action? onClear = null, IntPtr preview = default, Action? onOpen = null)
    {
        float labelW = ImGui.GetContentRegionAvail().X * LabelRatio;

        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(labelW);

        ImGui.BeginGroup();
        if (preview != IntPtr.Zero)
        {
            ImGui.Image(preview, new System.Numerics.Vector2(32, 32));
            ImGui.SameLine();
        }
        ImGui.SetNextItemWidth(WidthBeforeButton());
        ImGui.InputText($"##{popupId}_display", ref display, 256, ImGuiInputTextFlags.ReadOnly);
        ImGui.EndGroup();

        bool hovered = ImGui.IsItemHovered();

        if (onClear != null && ImGui.BeginPopupContextItem($"##{popupId}_ctx"))
        {
            if (ImGui.MenuItem("Clear / None")) onClear();
            ImGui.EndPopup();
        }

        if (hovered && !string.IsNullOrEmpty(tooltip)) ImGui.SetTooltip(tooltip);

        ImGui.SameLine();
        if (ImGui.Button($"•##{popupId}_btn", new System.Numerics.Vector2(PickerBtnW, 0)))
        {
            activePopupId = popupId;
            pickerSearch = "";
            onOpen?.Invoke();
            ImGui.OpenPopup(popupId);
        }
    }

    /// <summary>Squelette commun des popups : recherche, entrée "None", liste défilante.</summary>
    static void DrawPopupFrame(string popupId, System.Numerics.Vector2 size, Action onNone, Action<string> drawItems)
    {
        if (activePopupId != popupId) return;

        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        if (!ImGui.BeginPopup(popupId)) return;

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##picker_search", ref pickerSearch, 128);
        ImGui.Separator();

        if (ImGui.Selectable("None"))
        {
            onNone();
            ImGui.CloseCurrentPopup();
        }

        ImGui.Separator();
        ImGui.BeginChild("##picker_list", System.Numerics.Vector2.Zero);
        drawItems(pickerSearch.Trim().ToLowerInvariant());
        ImGui.EndChild();
        ImGui.EndPopup();
    }

    // ---------------------------------------------------------------
    // Picker générique : T = ce qu'on cherche
    // ---------------------------------------------------------------

    /// <param name="current">Valeur actuelle (null = None).</param>
    /// <param name="name">Texte d'un élément, dans le champ et dans la liste.</param>
    /// <param name="search">Éléments qui correspondent au filtre saisi (déjà en minuscules).</param>
    /// <param name="tooltip">Infobulle d'un élément (champ et liste).</param>
    /// <param name="preview">Aperçu affiché dans le champ pour la valeur actuelle.</param>
    /// <param name="drawItems">Rendu personnalisé de la liste (ex. grille) ; reçoit les éléments et le callback de choix.</param>
    /// <param name="onOpen">Appelé à l'ouverture de la popup (ex. rescanner les fichiers).</param>
    /// <returns>La nouvelle valeur (la même qu'en entrée s'il n'y a pas eu de changement).</returns>
    static T? DrawPicker<T>(string label, string id, T? current,
        Func<T, string> name, Func<string, IEnumerable<T>> search,
        string noneLabel = "None",
        Func<T, string?>? tooltip = null,
        Func<T, IntPtr>? preview = null,
        Action<IEnumerable<T>, Action<T>>? drawItems = null,
        Action? onOpen = null,
        System.Numerics.Vector2? popupSize = null) where T : class
    {
        string popupId = $"picker_{id}";

        if (pending.Remove(popupId, out var picked))
            current = (T?)picked;

        DrawPickerRow(label,
            current != null ? name(current) : noneLabel,
            popupId,
            tooltip: current != null ? tooltip?.Invoke(current) : null,
            onClear: () => current = null,
            preview: current != null && preview != null ? preview(current) : IntPtr.Zero,
            onOpen: onOpen);

        DrawPopupFrame(popupId, popupSize ?? new System.Numerics.Vector2(260, 300),
            () => pending[popupId] = null,
            filter =>
            {
                var items = search(filter);

                void Pick(T item)
                {
                    pending[popupId] = item;
                    ImGui.CloseCurrentPopup();
                }

                if (drawItems != null) { drawItems(items, Pick); return; }

                foreach (var item in items)
                {
                    // "##" : deux éléments de même nom restent distincts pour ImGui
                    if (ImGui.Selectable($"{name(item)}##{item.GetHashCode()}")) Pick(item);
                    if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip(item));
                }
            });

        return current;
    }

    // ---------------------------------------------------------------
    // Wrappers : un par type cherché
    // ---------------------------------------------------------------

    public static GameObject? DrawObjectField(string label, GameObject? current,
        string? id = null, string noneLabel = "None (GameObject)") =>
        DrawPicker(label, id ?? $"go_{label}", current,
            go => go.Name, SceneObjects, noneLabel);

    /// <summary>Picker de Component d'un type donné (type connu seulement à l'exécution).</summary>
    static Component? DrawComponentPicker(string label, string id, Type type, Component? current) =>
        DrawPicker(label, id, current,
            c => c.gameObject?.Name ?? "?",
            filter => SceneObjects(filter).Select(o => o.GetComponent(type)).OfType<Component>(),
            noneLabel: $"None ({type.Name})");

    public static T? DrawComponentField<T>(string label, T? current, string? id = null) where T : Component =>
        (T?)DrawComponentPicker(label, id ?? $"comp_{label}_{typeof(T).Name}", typeof(T), current);

    /// <summary>Picker de fichier filtré par extensions (audio, polices, …). Retourne "" pour None.</summary>
    public static string DrawAssetPathField(string label, string currentPath, string[] extensions, string id) =>
        DrawPicker(label, $"asset_{id}",
            string.IsNullOrEmpty(currentPath) ? null : currentPath,
            p => Path.GetFileName(p),
            filter => FilterAssets(extensions, filter),
            tooltip: p => p,
            onOpen: RefreshAssetFiles,
            popupSize: new System.Numerics.Vector2(280, 320)) ?? "";

    public static (IntPtr texture, string path) DrawTextureField(
        string label, IntPtr currentTexture, string currentPath, string? id = null)
    {
        string newPath = DrawPicker(label, $"tex_{id ?? label}",
            string.IsNullOrEmpty(currentPath) ? null : currentPath,
            p => Path.GetFileName(p),
            filter => FilterAssets(ImageExtensions, filter),
            tooltip: p => p,
            preview: _ => currentTexture,
            drawItems: DrawTextureGrid,
            onOpen: RefreshAssetFiles,
            popupSize: new System.Numerics.Vector2(360, 400)) ?? "";

        if (newPath != currentPath)
            currentTexture = newPath.Length == 0 ? IntPtr.Zero : TextureCache.Get(newPath);

        return (currentTexture, newPath);
    }

    static void DrawTextureGrid(IEnumerable<string> paths, Action<string> pick)
    {
        const float thumbSize = 64f;
        const float padding = 8f;
        int cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (thumbSize + padding)));
        int col = 0;

        foreach (var path in paths)
        {
            IntPtr tex = TextureCache.Get(path);

            ImGui.BeginGroup();

            if (tex != IntPtr.Zero)
            {
                ImGui.Image(tex, new System.Numerics.Vector2(thumbSize, thumbSize));
            }
            else
            {
                ImGui.Dummy(new System.Numerics.Vector2(thumbSize, thumbSize));
                var pos = ImGui.GetItemRectMin();
                ImGui.GetWindowDrawList().AddRectFilled(pos,
                    new System.Numerics.Vector2(pos.X + thumbSize, pos.Y + thumbSize), 0xFF333333);
            }

            ImGui.TextUnformatted(Path.GetFileName(path));
            ImGui.EndGroup();

            if (ImGui.IsItemClicked()) pick(path);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);

            if (++col < cols) ImGui.SameLine();
            else col = 0;
        }
    }
}