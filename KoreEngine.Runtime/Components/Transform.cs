namespace KoreEngine
{
    [UnremovableComponent]
    public class Transform : Component
    {
        public GameObject? Parent { get; private set; }

        public Vector2 Position;

        public Vector2 WorldPosition
        {
            get => Parent != null
                ? Parent.transform.WorldPosition + Position
                : Position;
        }

        // Rotation locale en degrés.
        public float Rotation;

        public float WorldRotation
        {
            get => Parent != null ? Parent.transform.WorldRotation + Rotation : Rotation;
        }

        // Scale locale — multiplicatif le long de la hiérarchie.
        public Vector2 Scale = new Vector2(1f, 1f);

        public Vector2 WorldScale
        {
            get => Parent != null
                ? new Vector2(Parent.transform.WorldScale.X * Scale.X, Parent.transform.WorldScale.Y * Scale.Y)
                : Scale;
        }

        /// <summary>
        /// Rattache cet objet à un nouveau parent (ou à la racine de la scène si newParent est null),
        /// Et conserve la position monde actuelle. Si le parent est changé, la position locale est recalculée pour que l'objet reste à la même position dans le monde.
        /// Évite les cycles de parentage (un objet ne peut pas devenir parent de lui-même ou d'un de ses descendants).
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
            Position = newParent != null
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

        public void Translate(Vector2 translation)
        {
            Position += translation;
        }

        public void RotateBy(float angle)
        {
            Rotation += angle;
        }

        public void ScaleBy(Vector2 scaleFactor)
        {
            Scale = new Vector2(Scale.X * scaleFactor.X, Scale.Y * scaleFactor.Y);
        }

        public override IEnumerable<InspectorField> GetInspectorFields()
        {
            yield return new FloatField { Label = "X", Get = () => Position.X, Set = (v) => Position = new Vector2(v, Position.Y), ReadOnly = false };

            yield return new FloatField { Label = "Y", Get = () => Position.Y, Set = (v) => Position = new Vector2(Position.X, v), ReadOnly = false };

            yield return new FloatField { Label = "Rotation", Get = () => Rotation, Set = (v) => Rotation = v, ReadOnly = false };

            yield return new FloatField { Label = "Scale X", Get = () => Scale.X, Set = (v) => Scale = new Vector2(v, Scale.Y), ReadOnly = false };

            yield return new FloatField { Label = "Scale Y", Get = () => Scale.Y, Set = (v) => Scale = new Vector2(Scale.X, v), ReadOnly = false };
        }
    }
}