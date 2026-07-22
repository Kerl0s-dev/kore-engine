namespace KoreEngine.Core;

/// <summary>
/// Décrit un champ à afficher dans l'Inspector, sans aucune dépendance à
/// ImGui. Chaque Component construit une liste de ces descripteurs dans
/// GetInspectorFields() ; c'est InspectorPanel (Editor) qui sait comment
/// transformer chaque type de champ en widgets ImGui concrets.
/// </summary>
public abstract class InspectorField
{
    public required string Label;

    /// <summary>Si true, affiché en lecture seule (pas de Set possible).</summary>
    public bool ReadOnly;
}

/// <summary>Texte en lecture seule (ex: position calculée, valeurs dérivées).</summary>
public class TextField : InspectorField
{
    public required Func<string> Get;
}

public class FloatField : InspectorField
{
    public required Func<float> Get;
    public Action<float>? Set;
    public float Speed = 0.1f;
    public float Min = 0f;
    public float Max = 0f; // Min == Max == 0 => pas de clamp
}

public class IntField : InspectorField
{
    public required Func<int> Get;
    public Action<int>? Set;
    public float Speed = 1f;
    public int Min = 0;
    public int Max = 0; // Min == Max == 0 => pas de clamp
}

public class BoolField : InspectorField
{
    public required Func<bool> Get;
    public Action<bool>? Set;
}

/// <summary>Combo box avec des noms d'énum affichés.</summary>
public class EnumField : InspectorField
{
    public required string[] Names;
    public required Func<int> Get;
    public Action<int>? Set;
}

/// <summary>Champ de sélection de texture (asset browser).</summary>
public class TextureField : InspectorField
{
    public required Func<(IntPtr texture, string path)> Get;
    public Action<IntPtr, string>? Set;
}

/// <summary>Champ de sélection de clip audio (asset browser).</summary>
public class AudioClipField : InspectorField
{
    public required Func<string> Get;
    public Action<string>? Set;
}

public class StringField : InspectorField
{
    public required Func<string> Get;
    public Action<string>? Set;
    public int MaxLength = 64;
}

/// <summary>Bouton d'action simple (pas de valeur à éditer, juste déclencher du code).</summary>
public class ActionField : InspectorField
{
    public required Action Action;
    public string? Tooltip;
}

/// <summary>
/// Liste d'items éditables (ajout/suppression/champs par item), pour les cas
/// comme Animator.Definitions/Transitions. InspectorPanel gère l'affichage
/// (headers repliables, popups) et son propre état d'ouverture/fermeture —
/// le composant ne connaît que les données, pas la présentation.
/// </summary>
public class ListField : InspectorField
{
    public required Func<int> Count;
    public required Func<int, string> ItemHeader;
    public required Func<int, IEnumerable<InspectorField>> ItemFields;
    public Action? AddItem;
    public Action<int>? RemoveItem;

    /// <summary>Actions contextuelles supplémentaires par item (menu clic-droit), en plus de "Remove".</summary>
    public Func<int, IEnumerable<(string label, Action action)>>? ItemContextActions;
}

/// <summary>Référence à un autre Component sur le même GameObject (ou ailleurs).</summary>
public class ComponentRefField : InspectorField
{
    public required Type ComponentType;
    public required Func<Component?> Get;
    public Action<Component?>? Set;
}

