using System.Reflection;
using System.Text;

namespace KoreEngine;

/// <summary>
/// Module 2 : Socle + Réflexion automatique pour tous les composants et scripts custom.
/// </summary>
public static class SceneSerializer
{
    static readonly HashSet<Type> SerializableTypes = new()
    {
        typeof(int), typeof(float), typeof(double), typeof(bool),
        typeof(string), typeof(byte), typeof(long),
        typeof(Vector2),
        typeof(Rectangle),
        typeof(Color)
    };

    // =========================================================================
    // 1. SAUVEGARDE
    // =========================================================================

    public static void SaveScene(Scene scene, string path)
    {
        var sb = new StringBuilder();
        var idMap = new Dictionary<GameObject, string>();
        int counter = 0;

        foreach (var obj in scene.AllObjects)
            idMap[obj] = $"obj_{counter++}";

        sb.AppendLine("# KoreEngine Scene File");
        sb.AppendLine($"Scene: {scene.Name}");

        foreach (var obj in scene.AllObjects)
        {
            WriteObject(sb, obj, idMap);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static string SerializeObjectTree(GameObject root)
    {
        var sb = new StringBuilder();
        var flat = FlattenTree(root).ToList();
        var idMap = new Dictionary<GameObject, string>();
        int counter = 0;

        foreach (var obj in flat)
            idMap[obj] = $"obj_{counter++}";

        foreach (var obj in flat)
        {
            WriteObject(sb, obj, idMap);
        }

        return sb.ToString();
    }

    static void WriteObject(StringBuilder sb, GameObject obj, Dictionary<GameObject, string> idMap)
    {
        string id = idMap[obj];
        string parent = obj.transform.Parent != null && idMap.TryGetValue(obj.transform.Parent, out var pid)
            ? pid : "none";

        sb.AppendLine();
        sb.AppendLine($"Object: {id} {{");
        sb.AppendLine($"    Name: {obj.Name}");
        sb.AppendLine($"    Parent: {parent}");
        sb.AppendLine($"    Enabled: {obj.Enabled.ToString().ToLower()}");

        // Sauvegarde dynamique de TOUS les composants de l'objet
        foreach (var c in obj.Components)
        {
            WriteComponent(sb, c, idMap);
        }

        sb.AppendLine("}");
    }

    static void WriteComponent(StringBuilder sb, Component c, Dictionary<GameObject, string> idMap)
    {
        string typeName = c.GetType().FullName ?? c.GetType().Name;
        sb.AppendLine();
        sb.AppendLine($"    Component: {typeName} {{");

        // Champs et propriétés sérialisables
        foreach (var member in GetSerializableMembers(c.GetType()))
        {
            var value = GetMemberValue(member, c);
            if (value == null) continue;
            sb.AppendLine($"        {member.Name}: {FormatValue(value, GetMemberType(member))}");
        }

        // Références vers GameObject ou Component (@obj_X)
        foreach (var field in GetReferenceFields(c.GetType()))
        {
            var value = field.GetValue(c);
            if (value == null) continue;

            if (value is Component comp && comp.gameObject != null && idMap.TryGetValue(comp.gameObject, out var refId))
            {
                sb.AppendLine($"        {field.Name}: @{refId}:{comp.GetType().Name}");
            }
            else if (value is GameObject go && idMap.TryGetValue(go, out var goId))
            {
                sb.AppendLine($"        {field.Name}: @{goId}");
            }
        }

        // Sérialisation personnalisée (si le composant l'implémente)
        foreach (var line in c.Serialize())
        {
            sb.AppendLine($"        {line}");
        }

        sb.AppendLine("    }");
    }


    // =========================================================================
    // 2. CHARGEMENT
    // =========================================================================

    public static Scene LoadScene(string path)
    {
        if (!File.Exists(path))
        {
            return new Scene();
        }

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var scene = new Scene();

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith("Scene:"))
            {
                scene.Name = line["Scene:".Length..].Trim();
                break;
            }
        }

        LoadObjectsInto(lines, scene);
        scene.FindCamera();
        scene.RefreshColliders();
        return scene;
    }

    public static GameObject? DeserializeObjectTree(string text, Scene targetScene)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var root = LoadObjectsInto(lines, targetScene);

        if (root != null) targetScene.RegisterColliders(root);
        return root;
    }

    static GameObject? LoadObjectsInto(string[] lines, Scene scene)
    {
        var objMap = new Dictionary<string, GameObject>();
        var parentMap = new Dictionary<string, string>();
        var compBlocks = new List<(string objId, string typeName, List<string> lines)>();

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i].Trim();

            if (line.StartsWith("#") || line.Length == 0 || line.StartsWith("Scene:"))
            {
                i++;
                continue;
            }

            if (line.StartsWith("Object:"))
            {
                i = ParseObject(lines, i, scene, objMap, parentMap, compBlocks);
                continue;
            }

            i++;
        }

        // Garde-fou Transform par défaut si manquant
        foreach (var o in objMap.Values)
        {
            if (o.GetComponent<Transform>() == null)
                o.AddComponent(new Transform());
        }

        // Restauration de la hiérarchie parent/enfant
        foreach (var (id, parentId) in parentMap)
        {
            if (parentId == "none") continue;
            if (objMap.TryGetValue(id, out var obj) && objMap.TryGetValue(parentId, out var parent))
            {
                obj.transform.AttachToParentKeepLocal(parent, scene);
            }
        }

        // Instanciation dynamique de TOUS les composants
        var allComps = new List<(Component comp, List<string> refLines)>();

        foreach (var (objId, typeName, fieldLines) in compBlocks)
        {
            if (!objMap.TryGetValue(objId, out var obj)) continue;

            var type = FindType(typeName);
            if (type == null)
            {
                continue;
            }

            // Évite de dupliquer le Transform créé par défaut par le constructeur de GameObject
            Component? comp = null;
            if (type == typeof(Transform))
            {
                comp = obj.GetComponent<Transform>();
            }

            if (comp == null)
            {
                comp = (Component?)Activator.CreateInstance(type);
                if (comp == null) continue;
                obj.AddComponent(comp);
            }

            // Sépare les lignes standards, les références et les lignes personnalisées
            var knownKeys = GetSerializableMembers(type).Select(m => m.Name).ToHashSet();
            var standardLines = fieldLines.Where(l => { var k = l.Split(':')[0].Trim(); return knownKeys.Contains(k); }).ToList();
            var refLines = fieldLines.Where(l => l.Contains(": @")).ToList();
            var extraLines = fieldLines.Where(l => { var k = l.Split(':')[0].Trim(); return !knownKeys.Contains(k) && !l.Contains(": @"); }).ToList();

            ApplyFields(comp, standardLines);

            if (extraLines.Count > 0) comp.Deserialize(extraLines);
            if (refLines.Count > 0) allComps.Add((comp, refLines));
        }

        // Passe de liaison des références croisées (@obj_X)
        foreach (var (comp, refLines) in allComps)
        {
            var fields = comp.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var line in refLines)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string fieldName = line[..colon].Trim();
                string refVal = line[(colon + 1)..].Trim();

                if (!refVal.StartsWith("@")) continue;
                refVal = refVal[1..];

                var field = fields.FirstOrDefault(f => f.Name == fieldName);
                if (field == null) continue;

                if (refVal.Contains(':'))
                {
                    var parts = refVal.Split(':');
                    string oId = parts[0].Trim();
                    string cTypeName = parts[1].Trim();

                    if (objMap.TryGetValue(oId, out var refObj))
                    {
                        var refComp = refObj.Components.FirstOrDefault(c => c.GetType().Name == cTypeName);
                        if (refComp != null) field.SetValue(comp, refComp);
                    }
                }
                else
                {
                    if (objMap.TryGetValue(refVal, out var refObj))
                        field.SetValue(comp, refObj);
                }
            }
        }

        foreach (var (id, parentId) in parentMap)
        {
            if (parentId == "none" && objMap.TryGetValue(id, out var rootObj))
                return rootObj;
        }

        return objMap.Values.FirstOrDefault();
    }

    static int ParseObject(string[] lines, int i, Scene scene,
        Dictionary<string, GameObject> objMap,
        Dictionary<string, string> parentMap,
        List<(string, string, List<string>)> compBlocks)
    {
        string header = lines[i].Trim();
        string objId = header.Split(':')[1].Trim().TrimEnd('{').Trim();
        i++;

        var obj = new GameObject();
        objMap[objId] = obj;
        scene.Add(obj);

        string? currentComp = null;
        var compLines = new List<string>();
        int depth = 0;

        while (i < lines.Length)
        {
            string line = lines[i].Trim();

            if (line == "}")
            {
                if (depth == 1)
                {
                    if (currentComp != null)
                    {
                        compBlocks.Add((objId, currentComp, new List<string>(compLines)));
                        currentComp = null;
                        compLines.Clear();
                    }
                    depth = 0;
                }
                else
                {
                    i++;
                    break;
                }
                i++;
                continue;
            }

            if (line.StartsWith("Name:"))
            {
                obj.Name = line["Name:".Length..].Trim();
            }
            else if (line.StartsWith("Parent:"))
            {
                parentMap[objId] = line["Parent:".Length..].Trim();
            }
            else if (depth == 0 && line.StartsWith("Enabled:"))
            {
                obj.Enabled = line["Enabled:".Length..].Trim() == "true";
            }
            else if (line.StartsWith("Component:"))
            {
                if (currentComp != null)
                {
                    compBlocks.Add((objId, currentComp, new List<string>(compLines)));
                }
                currentComp = line["Component:".Length..].Trim().TrimEnd('{').Trim();
                compLines.Clear();
                depth = 1;
            }
            else if (depth == 1 && line.Contains(':'))
            {
                compLines.Add(line);
            }

            i++;
        }

        if (currentComp != null)
        {
            compBlocks.Add((objId, currentComp, new List<string>(compLines)));
        }

        return i;
    }


    // =========================================================================
    // 3. APPLICATION ET FORMATAGE DES VALEURS
    // =========================================================================

    static void ApplyFields(Component comp, List<string> fieldLines)
    {
        var members = GetSerializableMembers(comp.GetType()).ToDictionary(m => m.Name);
        string compName = comp.GetType().Name;
        var compType = comp.GetType();

        foreach (var line in fieldLines)
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            string key = line[..colon].Trim();
            string rawValue = line[(colon + 1)..].Trim().Trim('"');

            if (!members.TryGetValue(key, out var member)) continue;

            try
            {
                var memberType = GetMemberType(member);
                var parsed = ParseValue(rawValue, memberType);
                if (parsed != null)
                {
                    SetMemberValue(member, comp, parsed);

                    // Convention XxxTexturePath -> charge et applique XxxTexture (IntPtr)
                    if (memberType == typeof(string) && key.EndsWith("Path"))
                    {
                        string targetName = key[..^4];
                        var targetMember = compType.GetMember(targetName, BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();

                        if (targetMember != null && GetMemberType(targetMember) == typeof(IntPtr) && !string.IsNullOrEmpty(rawValue))
                        {
                            IntPtr textureHandle = TextureCache.Get(rawValue);
                            SetMemberValue(targetMember, comp, textureHandle);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Module 2] Erreur sur {compName}.{key}: {ex.Message}");
            }
        }
    }

    static string FormatValue(object value, Type type)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        if (type == typeof(Vector2))
        {
            var v = (Vector2)value;
            return $"{v.X.ToString(inv)}, {v.Y.ToString(inv)}";
        }
        if (type == typeof(Rectangle))
        {
            var r = (Rectangle)value;
            return $"{r.X.ToString(inv)}, {r.Y.ToString(inv)}, {r.Width.ToString(inv)}, {r.Height.ToString(inv)}";
        }
        if (type == typeof(Color))
        {
            var col = (Color)value;
            return $"{col.R.ToString(inv)}, {col.G.ToString(inv)}, {col.B.ToString(inv)}";
        }
        if (type == typeof(bool))
            return value.ToString()!.ToLower();
        if (type == typeof(string))
        {
            var s = (string)value;
            return s.Contains(' ') ? $"\"{s}\"" : s;
        }
        if (type == typeof(float) || type == typeof(double))
            return ((IFormattable)value).ToString(null, inv);

        return value.ToString() ?? "";
    }

    static object? ParseValue(string value, Type type)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        if (type == typeof(int)) return int.Parse(value, inv);
        if (type == typeof(float)) return float.Parse(value, inv);
        if (type == typeof(double)) return double.Parse(value, inv);
        if (type == typeof(bool)) return value == "true";
        if (type == typeof(string)) return value;
        if (type == typeof(byte)) return byte.Parse(value, inv);
        if (type == typeof(long)) return long.Parse(value, inv);

        if (type == typeof(Vector2))
        {
            var parts = value.Split(',');
            if (parts.Length >= 2 &&
                float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, inv, out float x) &&
                float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, inv, out float y))
            {
                return new Vector2(x, y);
            }
            return null;
        }

        if (type == typeof(Rectangle))
        {
            var parts = value.Split(',');
            if (parts.Length >= 4 &&
                int.TryParse(parts[0].Trim(), out int rx) &&
                int.TryParse(parts[1].Trim(), out int ry) &&
                int.TryParse(parts[2].Trim(), out int rw) &&
                int.TryParse(parts[3].Trim(), out int rh))
            {
                return new Rectangle(rx, ry, rw, rh);
            }
            return null;
        }

        if (type == typeof(Color))
        {
            var parts = value.Split(',');
            if (parts.Length >= 3 &&
                int.TryParse(parts[0].Trim(), out int cr) &&
                int.TryParse(parts[1].Trim(), out int cg) &&
                int.TryParse(parts[2].Trim(), out int cb))
            {
                return new Color(cr, cg, cb);
            }
            return null;
        }

        if (type.IsEnum) return Enum.Parse(type, value);

        return null;
    }


    // =========================================================================
    // 4. HELPERS DE RÉFLEXION
    // =========================================================================

    static IEnumerable<GameObject> FlattenTree(GameObject root)
    {
        yield return root;
        foreach (var child in root.children)
            foreach (var descendant in FlattenTree(child))
                yield return descendant;
    }

    static IEnumerable<MemberInfo> GetSerializableMembers(Type type)
    {
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.IsInitOnly || f.GetCustomAttribute<NonSerializedAttribute>() != null || f.GetCustomAttribute<HideInInspectorAttribute>() != null)
                continue;
            if (SerializableTypes.Contains(f.FieldType) || f.FieldType.IsEnum)
                yield return f;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetCustomAttribute<NonSerializedAttribute>() != null || p.GetCustomAttribute<HideInInspectorAttribute>() != null)
                continue;
            if (SerializableTypes.Contains(p.PropertyType) || p.PropertyType.IsEnum)
                yield return p;
        }
    }

    static Type GetMemberType(MemberInfo member) => member switch
    {
        FieldInfo f => f.FieldType,
        PropertyInfo p => p.PropertyType,
        _ => throw new InvalidOperationException()
    };

    static object? GetMemberValue(MemberInfo member, object target) => member switch
    {
        FieldInfo f => f.GetValue(target),
        PropertyInfo p => p.GetValue(target),
        _ => null
    };

    static void SetMemberValue(MemberInfo member, object target, object value)
    {
        switch (member)
        {
            case FieldInfo f: f.SetValue(target, value); break;
            case PropertyInfo p: p.SetValue(target, value); break;
        }
    }

    static IEnumerable<FieldInfo> GetReferenceFields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly
                && f.GetCustomAttribute<NonSerializedAttribute>() == null
                && f.GetCustomAttribute<HideInInspectorAttribute>() == null
                && (f.FieldType == typeof(GameObject) || typeof(Component).IsAssignableFrom(f.FieldType)));

    static Type? FindType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(name);
            if (t != null) return t;
        }
        string shortName = name.Split('.').Last();
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == shortName && t.IsSubclassOf(typeof(Component)));
    }
}