using KoreEngine.Components;
using KoreEngine.Engine;

namespace KoreEngine.Core;

public abstract class Component
{
    [HideInInspector] public GameObject Owner = new("");
    public virtual void Update(float dt) { }
    public virtual void Render(Renderer renderer, Camera? camera) { }

    /// <summary>
    /// Surcharger cette méthode dans chaque composant concret pour décrire
    /// ses champs éditables via des InspectorField (Text, Float, Int, Bool,
    /// Enum, Texture, ComponentRef...), sans dépendre d'ImGui. C'est
    /// InspectorPanel (Editor) qui traduit chaque descripteur en widgets.
    /// Par défaut : aucun champ personnalisé — InspectorPanel retombe alors
    /// sur l'auto-draw par réflexion.
    /// </summary>
    public virtual IEnumerable<InspectorField> GetInspectorFields()
        => Enumerable.Empty<InspectorField>();

    /// <summary>
    /// Appelé une seule fois quand Playing passe à true.
    /// Sert à initialiser des références, lancer des animations d'entrée,
    /// etc. — tout ce qui ne doit PAS s'exécuter en mode édition.
    /// </summary>
    public virtual void Start() { }

    /// <summary>
    /// Appelé quand le composant est retiré d'un GameObject, ou que le
    /// GameObject lui-même est détruit/déchargé (fin de scène). Sert à
    /// libérer des ressources natives (handles audio, textures allouées
    /// manuellement, etc.) — tout ce qui ne serait pas géré par le GC seul.
    /// </summary>
    public virtual void OnDestroy() { }

    /// <summary>
    /// Sérialisation personnalisée — retourne des lignes supplémentaires
    /// à écrire dans le bloc du composant dans le fichier .kscene.
    /// À surcharger pour les champs non couverts par le sérialiseur standard
    /// (listes, types complexes, etc.).
    /// </summary>
    public virtual IEnumerable<string> Serialize()
        => Enumerable.Empty<string>();

    /// <summary>
    /// Désérialisation personnalisée — reçoit les lignes du fichier .kscene
    /// qui n'ont pas été reconnues par le sérialiseur standard.
    /// À surcharger en parallèle de Serialize().
    /// </summary>
    public virtual void Deserialize(List<string> extraLines) { }
}