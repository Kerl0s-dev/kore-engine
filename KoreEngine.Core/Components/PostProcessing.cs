namespace KoreEngine;

/// <summary>
/// Effets plein écran appliqués à la sortie d'une caméra, dans l'ordre de la liste.
/// À ajouter sur le même GameObject que la Camera. Chaque effet est un matériau (.kmat) dont le shader
/// lit la scène avec SampleMainTex(uv). Visible dans la vue Game et dans le jeu exporté, pas dans la vue Scene.
/// </summary>
public class PostProcessing : Component
{
    /// <summary>
    /// Effets sérialisés : "activé?matériau?surcharges" séparés par '*' (ces caractères sont interdits dans un nom de fichier).
    /// Ne PAS marquer ce champ [HideInInspector] : le sérialiseur ignore les champs ainsi marqués, et les effets
    /// seraient perdus au chargement de la scène (donc absents d'un build). Il n'apparaît pas dans l'Inspector
    /// de toute façon, puisque GetInspectorFields est redéfini.
    /// </summary>
    public string Effects = "";

    sealed class Entry
    {
        public bool Enabled = true;
        public string MaterialPath = "";
        public MaterialParamSet Params = new();
    }

    readonly List<Entry> entries = new();
    string? parsedFrom;

    Entry NewEntry()
    {
        var entry = new Entry();
        entry.Params.Changed = _ => Save();
        return entry;
    }

    void Sync()
    {
        if (parsedFrom == Effects) return;

        entries.Clear();
        if (!string.IsNullOrEmpty(Effects))
        {
            foreach (var raw in Effects.Split('*', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Split('?');
                var entry = NewEntry();
                entry.Enabled = parts[0] != "0";
                entry.MaterialPath = parts.Length > 1 ? parts[1] : "";
                entry.Params.Text = parts.Length > 2 ? parts[2] : "";
                entries.Add(entry);
            }
        }
        parsedFrom = Effects;
    }

    void Save()
    {
        Effects = string.Join("*", entries.Select(e => $"{(e.Enabled ? 1 : 0)}?{e.MaterialPath}?{e.Params.Text}"));
        parsedFrom = Effects;
    }

    /// <summary>Effets activés dont le matériau et le shader sont valides, dans l'ordre d'application.</summary>
    public List<(Material Material, MaterialParamSet Params)> GetActiveEffects()
    {
        Sync();

        var list = new List<(Material, MaterialParamSet)>();
        foreach (var entry in entries)
        {
            if (!entry.Enabled || string.IsNullOrEmpty(entry.MaterialPath)) continue;

            var material = MaterialCache.Get(entry.MaterialPath);
            if (material?.Shader == null) continue;

            list.Add((material, entry.Params));
        }
        return list;
    }

    void Move(int index, int delta)
    {
        int target = index + delta;
        if (index < 0 || index >= entries.Count || target < 0 || target >= entries.Count) return;
        (entries[index], entries[target]) = (entries[target], entries[index]);
        Save();
    }

    IEnumerable<InspectorField> ItemFields(int index)
    {
        var entry = entries[index];

        yield return new BoolField { Label = "Enabled", Get = () => entry.Enabled, Set = v => { entry.Enabled = v; Save(); } };

        yield return new AssetPathField
        {
            Label = "Material",
            Extensions = new[] { ".kmat" },
            Get = () => entry.MaterialPath,
            Set = v => { entry.MaterialPath = v; entry.Params.Text = ""; Save(); }
        };

        var material = string.IsNullOrEmpty(entry.MaterialPath) ? null : MaterialCache.Get(entry.MaterialPath);
        if (material != null)
            foreach (var field in entry.Params.CreateFields(material))
                yield return field;
    }

    public override IEnumerable<InspectorField> GetInspectorFields()
    {
        yield return new ListField
        {
            Label = "Effects",
            Count = () => { Sync(); return entries.Count; },
            ItemHeader = i =>
            {
                var e = entries[i];
                string name = string.IsNullOrEmpty(e.MaterialPath) ? "(none)" : Path.GetFileNameWithoutExtension(e.MaterialPath);
                return e.Enabled ? name : name + " (off)";
            },
            ItemFields = ItemFields,
            AddItem = () => { Sync(); entries.Add(NewEntry()); Save(); },
            RemoveItem = i => { entries.RemoveAt(i); Save(); },
            ItemContextActions = i => new (string, Action)[]
            {
                ("Move up", () => Move(i, -1)),
                ("Move down", () => Move(i, 1))
            }
        };
    }
}