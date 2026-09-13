using KoreEngine.Core;

namespace KoreEngine.Components
{
    [UnremovableComponent]
    public class Transform : Component
    {
        // Position LOCALE : relative au parent (ou absolue si pas de parent).
        // C'est cette valeur qu'on édite dans l'Inspector et qu'on stocke.
        public Vector2 LocalPosition;
        public Vector2 PreviousPosition;

        // Position MONDE : remonte la chaîne de parents pour calculer la
        // position absolue. Utilisée par le rendu et la physique.
        // IMPORTANT : tous les composants qui dessinaient via Owner.Position
        // doivent maintenant utiliser Owner.WorldPosition.
        public Vector2 WorldPosition
        {
            get => gameObject.Parent != null
                ? gameObject.Parent.transform.WorldPosition + LocalPosition
                : LocalPosition;
        }

        // Alias rétrocompatible — pointe sur LocalPosition pour que l'ancien
        // code qui écrit Position continue de compiler. À migrer vers
        // LocalPosition/WorldPosition selon le contexte au fil du temps.
        public Vector2 Position
        {
            get => LocalPosition;
            set => LocalPosition = value;
        }

        // Rotation locale en degrés.
        public float LocalRotation;

        public float WorldRotation
        {
            get => gameObject.Parent != null ? gameObject.Parent.transform.WorldRotation + LocalRotation : LocalRotation;
        }

        // Scale locale — multiplicatif le long de la hiérarchie.
        public Vector2 LocalScale = new Vector2(1f, 1f);

        public Vector2 WorldScale
        {
            get => gameObject.Parent != null
                ? new Vector2(gameObject.Parent.transform.WorldScale.X * LocalScale.X, gameObject.Parent.transform.WorldScale.Y * LocalScale.Y)
                : LocalScale;
        }

        public override IEnumerable<InspectorField> GetInspectorFields()
        {
            yield return new FloatField { Label = "X", Get = () => Position.X, Set = (v) => Position = new Vector2(v, Position.Y), ReadOnly = false };
            yield return new FloatField { Label = "Y", Get = () => Position.Y, Set = (v) => Position = new Vector2(Position.X, v), ReadOnly = false };
            yield return new FloatField { Label = "Rotation", Get = () => LocalRotation, Set = (v) => LocalRotation = v, ReadOnly = false };
            yield return new FloatField { Label = "Scale X", Get = () => LocalScale.X, Set = (v) => LocalScale = new Vector2(v, LocalScale.Y), ReadOnly = false };
            yield return new FloatField { Label = "Scale Y", Get = () => LocalScale.Y, Set = (v) => LocalScale = new Vector2(LocalScale.X, v), ReadOnly = false };
        }
    }
}