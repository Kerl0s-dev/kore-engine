using System.Text;
using KoreEngine.Core;

namespace KoreEngine.Engine;

/// <summary>
/// Prefabs : un GameObject (et toute sa hiérarchie d'enfants) sérialisé dans
/// un fichier .kprefab, réutilisable dans n'importe quelle scène. Même format
/// texte que les scènes — voir SceneSerializer.SerializeObjectTree /
/// DeserializeObjectTree, réutilisés tels quels ici, juste sans l'en-tête
/// "Scene:" (un prefab n'a pas de nom de scène).
/// </summary>
public static class PrefabManager
{
    /// <summary>Tous les fichiers .kprefab sous Assets/, comme SceneManager.SceneFiles().</summary>
    public static IEnumerable<string> PrefabFiles()
    {
        if (!Directory.Exists(SceneManager.AssetsDirectory))
            return Enumerable.Empty<string>();

        return Directory
            .GetFiles(SceneManager.AssetsDirectory, "*.kprefab", SearchOption.AllDirectories)
            .OrderBy(f => f);
    }

    /// <summary>Sauvegarde un objet (et ses enfants) en tant que prefab réutilisable.</summary>
    public static void Save(GameObject obj, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        string text = "# KoreEngine Prefab File\r\n" + SceneSerializer.SerializeObjectTree(obj);
        File.WriteAllText(path, text, Encoding.UTF8);
    }

    /// <summary>
    /// Instancie un prefab dans une scène, en tant qu'objet racine (ou enfant
    /// de <paramref name="parent"/> si fourni), à sa position d'origine (ou
    /// <paramref name="position"/> si fournie).
    /// </summary>
    public static GameObject Instantiate(string path, Scene scene, GameObject? parent = null, Vector2? position = null)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);

        var obj = SceneSerializer.DeserializeObjectTree(text, scene)
            ?? throw new Exception($"Échec de l'instanciation du prefab : {path}");

        if (parent != null) obj.SetParent(parent, scene);
        if (position.HasValue) obj.LocalPosition = position.Value;

        return obj;
    }
}
