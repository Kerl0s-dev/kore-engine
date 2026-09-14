using KoreEngine.Core;

namespace KoreEngine.Components
{
    [UnremovableComponent]
    public class Transform : Component
    {
        public GameObject? Parent { get; private set; }

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
            get => Parent != null
                ? Parent.transform.WorldPosition + LocalPosition
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
            get => Parent != null ? Parent.transform.WorldRotation + LocalRotation : LocalRotation;
        }

        public float Rotation
        {
            get => LocalRotation;
            set => LocalRotation = value;
        }

        // Scale locale — multiplicatif le long de la hiérarchie.
        public Vector2 LocalScale = new Vector2(1f, 1f);

        public Vector2 WorldScale
        {
            get => Parent != null
                ? new Vector2(Parent.transform.WorldScale.X * LocalScale.X, Parent.transform.WorldScale.Y * LocalScale.Y)
                : LocalScale;
        }

        public Vector2 Scale
        {
            get => LocalScale;
            set => LocalScale = value;
        }

        /// <summary>
        /// Rattache cet objet à un nouveau parent (ou le passe en racine si null).
        /// Préserve la position monde : LocalPosition est recalculée pour que
        /// l'objet ne "saute" pas visuellement au moment du reparentage.
        /// Protège contre les cycles (on ne peut pas devenir enfant de soi-même
        /// ni d'un de ses propres descendants).
        /// </summary>
        public void SetParent(GameObject? newParent, Scene? scene = null)
        {
            if (newParent == gameObject) return;
            if (newParent != null && newParent.transform.IsDescendantOf(gameObject)) return;

            // Sauvegarde la position monde avant de changer de parent.
            var worldPos = WorldPosition;

            // Détache de l'ancien parent (ou de la racine de la scène).
            if (Parent != null)
                Parent.children.Remove(gameObject);
            else
                scene?.RootObjects.Remove(gameObject);

            Parent = newParent;

            // Rattache au nouveau parent (ou à la racine de la scène).
            if (newParent != null)
                newParent.children.Add(gameObject);
            else
                scene?.RootObjects.Add(gameObject);

            // Recalcule LocalPosition pour conserver la position monde.
            LocalPosition = newParent != null
                ? worldPos - newParent.transform.WorldPosition
                : worldPos;
        }

        public bool IsDescendantOf(GameObject ancestor)
        {
            var p = Parent;
            while (p != null)
            {
                if (p == ancestor) return true;
                p = p.transform.Parent;
            }
            return false;
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