namespace KoreEngine.Core;

/// <summary>
/// Marque un Component comme un script "utilisateur" (gameplay), par
/// opposition aux composants internes du moteur (SpriteRenderer, PhysicsBody...).
/// Les classes marquées apparaissent dans la catégorie "Scripts" du ProjectPanel,
/// où elles peuvent être ouvertes directement dans l'éditeur de code.
///
/// Utilisation :
///   [UserScript]
///   public class NomDuScript : Component { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UserScriptAttribute : Attribute { }