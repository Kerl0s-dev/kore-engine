using KoreEngine.Core;
using System.Reflection;
using System.Text;

namespace KoreEngine.Engine;

/// <summary>
/// Sérialise/désérialise les scènes au format .kscene
/// </summary>
public static class SceneSerializer
{
    static readonly HashSet<Type> SerializableTypes = new()
    {
        typeof(int), typeof(float), typeof(double), typeof(bool),
        typeof(string), typeof(byte), typeof(long),
        typeof(Vector2), typeof(Rectangle)
    };

    // ---------------------------------------------------------------
    // Sauvegarde
    // ---------------------------------------------------------------

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
            WriteObject(sb, obj, idMap);

        Console.WriteLine(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static void WriteObject(StringBuilder sb, GameObject obj, Dictionary<GameObject, string> idMap)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        string id = idMap[obj];
        string parent = obj.Parent != null && idMap.TryGetValue(obj.Parent, out var pid)
            ? pid : "none";

        sb.AppendLine();
        sb.AppendLine($"Object: {id} {{");
        sb.AppendLine($"  Name: {obj.Name}");
        sb.AppendLine($"  Position: {obj.LocalPosition.X.ToString(inv)}, {obj.LocalPosition.Y.ToString(inv)}");
        sb.AppendLine($"  Rotation: {obj.LocalRotation.ToString(inv)}");
        sb.AppendLine($"  Scale: {obj.LocalScale.X.ToString(inv)}, {obj.LocalScale.Y.ToString(inv)}");
        sb.AppendLine($"  Parent: {parent}");

        foreach (var c in obj.Components)
            WriteComponent(sb, c, idMap);

        sb.AppendLine("}");
    }

    static void WriteComponent(StringBuilder sb, Component c,
        Dictionary<GameObject, string> idMap)
    {
        sb.AppendLine();
        sb.AppendLine($"  Component: {c.GetType().FullName} {{");

        // Champs primitifs / types simples
        foreach (var field in GetSerializableFields(c.GetType()))
        {
            var value = field.GetValue(c);
            if (value == null) continue;
            sb.AppendLine($"    {field.Name}: {FormatValue(value, field.FieldType)}");
        }

        // Champs référence : Component et GameObject
        foreach (var field in GetReferenceFields(c.GetType()))
        {
            var value = field.GetValue(c);
            if (value == null) continue;

            if (value is Component comp && comp.Owner != null
                && idMap.TryGetValue(comp.Owner, out var refId))
            {
                // @obj_2:Camera
                sb.AppendLine($"    {field.Name}: @{refId}:{comp.GetType().Name}");
            }
            else if (value is GameObject go && idMap.TryGetValue(go, out var goId))
            {
                // @obj_2
                sb.AppendLine($"    {field.Name}: @{goId}");
            }
        }

        // Sérialisation personnalisée (listes, types complexes)
        foreach (var line in c.Serialize())
            sb.AppendLine($"    {line}");

        sb.AppendLine("  }");
    }

    static IEnumerable<FieldInfo> GetReferenceFields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly
                && f.GetCustomAttribute<NonSerializedAttribute>() == null
                && f.GetCustomAttribute<HideInInspectorAttribute>() == null
                && (f.FieldType == typeof(GameObject)
                    || typeof(Component).IsAssignableFrom(f.FieldType)));

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
            return $"{r.X} {r.Y} {r.Width} {r.Height}";
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

    // ---------------------------------------------------------------
    // Chargement
    // ---------------------------------------------------------------

    public static Scene LoadScene(string path)
    {
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

        // Résout automatiquement la caméra de la scène
        scene.FindCamera();

        // Réenregistre TOUS les colliders : chaque Scene.Add() (fait par
        // ParseObject) a eu lieu AVANT que les composants (dont Collider)
        // soient attachés, donc rien n'a pu être enregistré jusqu'ici.
        scene.RefreshColliders();

        return scene;
    }

    // ---------------------------------------------------------------
    // Clonage d'un sous-arbre (GameObject.Instantiate) — même format texte
    // que SaveScene/LoadScene, pour ne pas dupliquer la logique de clonage.
    // ---------------------------------------------------------------

    /// <summary>
    /// Sérialise UN objet et toute sa hiérarchie d'enfants (mêmes règles que
    /// SaveScene). Les références vers des objets HORS de cette
    /// sous-arborescence (ex: un champ pointant vers la Camera de la scène)
    /// sont silencieusement omises — elles ne peuvent pas survivre à un clone
    /// partiel, exactement comme pour un prefab qui sortirait de sa scène.
    /// </summary>
    public static string SerializeObjectTree(GameObject root)
    {
        var sb = new StringBuilder();
        var flat = FlattenTree(root).ToList();
        var idMap = new Dictionary<GameObject, string>();
        int counter = 0;

        foreach (var obj in flat)
            idMap[obj] = $"obj_{counter++}";

        foreach (var obj in flat)
            WriteObject(sb, obj, idMap);

        return sb.ToString();
    }

    /// <summary>
    /// Réciproque de SerializeObjectTree : reconstruit la sous-arborescence
    /// comme nouvel objet racine de targetScene, et retourne cette racine.
    /// Utilisé par GameObject.Instantiate().
    /// </summary>
    public static GameObject? DeserializeObjectTree(string text, Scene targetScene)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var root = LoadObjectsInto(lines, targetScene);

        // Seuls les colliders de la NOUVELLE sous-arborescence doivent être
        // (re)enregistrés ici — un RefreshColliders() global dupliquerait tous
        // les colliders déjà enregistrés du reste de la scène à chaque appel.
        if (root != null) targetScene.RegisterColliders(root);

        return root;
    }

    static IEnumerable<GameObject> FlattenTree(GameObject root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in FlattenTree(child))
                yield return descendant;
    }

    /// <summary>
    /// Cœur commun à LoadScene (fichier .kscene complet) et
    /// DeserializeObjectTree (sous-arborescence pour Instantiate) : parse les
    /// blocs "Object:", reconstruit la hiérarchie et les composants, résout
    /// les références internes. Retourne le premier objet créé sans parent
    /// (la racine du bloc parsé).
    /// </summary>
    static GameObject? LoadObjectsInto(string[] lines, Scene scene)
    {
        var objMap = new Dictionary<string, GameObject>();
        var parentMap = new Dictionary<string, string>(); // id -> parentId
        var compBlocks = new List<(string objId, string typeName, List<string> lines)>();

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i].Trim();

            if (line.StartsWith("#") || line.Length == 0) { i++; continue; }
            if (line.StartsWith("Scene:")) { i++; continue; }

            if (line.StartsWith("Object:"))
            {
                i = ParseObject(lines, i, scene, objMap, parentMap, compBlocks);
                continue;
            }

            i++;
        }

        // Rétablit la hiérarchie
        foreach (var (id, parentId) in parentMap)
        {
            if (parentId == "none") continue;
            if (objMap.TryGetValue(id, out var obj) &&
                objMap.TryGetValue(parentId, out var parent))
                obj.SetParent(parent, scene);
        }

        // Attache les composants
        // compBlocks contient aussi les lignes de référence (@obj_X)
        // qu'on traite dans une passe séparée après tout avoir instancié.
        var allComps = new List<(Component comp, List<string> refLines)>();

        foreach (var (objId, typeName, fieldLines) in compBlocks)
        {
            if (!objMap.TryGetValue(objId, out var obj)) continue;
            var type = FindType(typeName);
            if (type == null) { Console.WriteLine($"[KScene] Type inconnu : {typeName}"); continue; }

            var comp = (Component?)Activator.CreateInstance(type);
            if (comp == null) continue;

            obj.AddComponent(comp);

            var knownKeys = GetSerializableFields(type)
                .Select(f => f.Name).ToHashSet();

            var standardLines = fieldLines
                .Where(l => { var k = l.Split(':')[0].Trim(); return knownKeys.Contains(k); })
                .ToList();
            var refLines = fieldLines
                .Where(l => l.Contains(": @"))
                .ToList();
            var extraLines = fieldLines
                .Where(l => { var k = l.Split(':')[0].Trim(); return !knownKeys.Contains(k) && !l.Contains(": @"); })
                .ToList();

            ApplyFields(comp, standardLines);
            if (extraLines.Count > 0) comp.Deserialize(extraLines);
            if (refLines.Count > 0) allComps.Add((comp, refLines));
        }

        // Passe de résolution des références (@obj_X et @obj_X:Type)
        foreach (var (comp, refLines) in allComps)
        {
            var fields = comp.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var line in refLines)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string fieldName = line[..colon].Trim();
                string refVal = line[(colon + 1)..].Trim(); // "@obj_2:Camera" ou "@obj_2"

                if (!refVal.StartsWith("@")) continue;
                refVal = refVal[1..]; // retire le @

                var field = fields.FirstOrDefault(f => f.Name == fieldName);
                if (field == null) continue;

                if (refVal.Contains(':'))
                {
                    // Référence à un composant : @obj_2:Camera
                    var parts = refVal.Split(':');
                    string oId = parts[0].Trim();
                    string cTypeName = parts[1].Trim();

                    if (objMap.TryGetValue(oId, out var refObj))
                    {
                        var refComp = refObj.Components
                            .FirstOrDefault(c => c.GetType().Name == cTypeName);
                        if (refComp != null) field.SetValue(comp, refComp);
                    }
                }
                else
                {
                    // Référence à un GameObject : @obj_2
                    if (objMap.TryGetValue(refVal, out var refObj))
                        field.SetValue(comp, refObj);
                }
            }
        }

        // La racine du bloc parsé est le premier objet dont le Parent déclaré
        // est "none" (donc resté objet racine de `scene` après la passe de
        // hiérarchie ci-dessus).
        foreach (var (id, parentId) in parentMap)
            if (parentId == "none" && objMap.TryGetValue(id, out var rootObj))
                return rootObj;

        return objMap.Values.FirstOrDefault();
    }

    static int ParseObject(string[] lines, int i,
        Scene scene,
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
        int depth = 0; // 0 = dans l'objet, 1 = dans un composant

        while (i < lines.Length)
        {
            string line = lines[i].Trim();

            if (line == "}")
            {
                if (depth == 1)
                {
                    // Ferme le bloc composant courant
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
                    // Ferme le bloc objet
                    i++;
                    break;
                }
                i++;
                continue;
            }

            if (line.StartsWith("Name:"))
                obj.Name = line["Name:".Length..].Trim();

            else if (line.StartsWith("Position:"))
            {
                var parts = line["Position:".Length..].Trim().Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float y))
                    obj.LocalPosition = new Vector2(x, y);
            }

            else if (line.StartsWith("Rotation:"))
            {
                if (float.TryParse(line["Rotation:".Length..].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rot))
                    obj.LocalRotation = rot;
            }

            else if (line.StartsWith("Scale:"))
            {
                var parts = line["Scale:".Length..].Trim().Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float sx) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float sy))
                    obj.LocalScale = new Vector2(sx, sy);
            }

            else if (line.StartsWith("Parent:"))
                parentMap[objId] = line["Parent:".Length..].Trim();

            else if (line.StartsWith("Component:"))
            {
                // Sauvegarde le composant précédent si pas encore fermé
                if (currentComp != null)
                    compBlocks.Add((objId, currentComp, new List<string>(compLines)));

                currentComp = line["Component:".Length..].Trim().TrimEnd('{').Trim();
                compLines.Clear();
                depth = 1;
            }

            else if (depth == 1 && line.Contains(':'))
                compLines.Add(line);

            i++;
        }

        // Composant non fermé (sécurité)
        if (currentComp != null)
            compBlocks.Add((objId, currentComp, new List<string>(compLines)));

        return i;
    }

    static void ApplyFields(Component comp, List<string> fieldLines)
    {
        var fields = GetSerializableFields(comp.GetType())
            .ToDictionary(f => f.Name);

        foreach (var line in fieldLines)
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim().Trim('"');

            if (!fields.TryGetValue(key, out var field)) continue;

            try
            {
                var parsed = ParseValue(value, field.FieldType);
                if (parsed != null) field.SetValue(comp, parsed);

                // Convention XxxTexturePath -> charge XxxTexture (IntPtr)
                if (field.FieldType == typeof(string) && key.EndsWith("Path"))
                {
                    string texFieldName = key[..^4];
                    var texField = comp.GetType().GetField(texFieldName,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (texField?.FieldType == typeof(IntPtr) && !string.IsNullOrEmpty(value))
                        texField.SetValue(comp, TextureCache.Get(value));
                }
            }
            catch { /* champ inconnu ou type incompatible */ }
        }
    }

    static object? ParseValue(string value, Type type)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        if (type == typeof(int)) return int.Parse(value);
        if (type == typeof(float)) return float.Parse(value, inv);
        if (type == typeof(double)) return double.Parse(value, inv);
        if (type == typeof(bool)) return value == "true";
        if (type == typeof(string)) return value;
        if (type == typeof(byte)) return byte.Parse(value);
        if (type == typeof(long)) return long.Parse(value);

        if (type == typeof(Vector2))
        {
            var p = value.Split(',');
            return new Vector2(
                float.Parse(p[0].Trim(), inv),
                float.Parse(p[1].Trim(), inv));
        }

        if (type == typeof(Rectangle))
        {
            var p = value.Split(' ');
            return new Rectangle(
                int.Parse(p[0]), int.Parse(p[1]),
                int.Parse(p[2]), int.Parse(p[3]));
        }

        if (type.IsEnum) return Enum.Parse(type, value);

        return null;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    static IEnumerable<FieldInfo> GetSerializableFields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly
                && f.GetCustomAttribute<NonSerializedAttribute>() == null
                && (SerializableTypes.Contains(f.FieldType) || f.FieldType.IsEnum));

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