using KoreEngine.Core;

/// <summary>
/// <see cref="UnremovableComponentAttribute"/> est un attribut qui peut être appliqué à une classe dérivée de <see cref="Component"/> pour indiquer que ce composant ne doit pas être supprimé par l'utilisateur dans l'éditeur.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UnremovableComponentAttribute : Attribute { }