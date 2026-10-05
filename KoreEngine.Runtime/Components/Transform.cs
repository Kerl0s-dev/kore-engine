namespace KoreEngine
{
    [UnremovableComponent]
    public class Transform : Component
    {
        public GameObject? Parent { get; private set; }

        public Vector2 Position;

        public Vector2 WorldPosition
        {
            get
            {
                if (Parent == null) return Position;

                var parentTransform = Parent.transform;
                var scaledLocal = Position * parentTransform.WorldScale;
                var rotatedLocal = RotateVector(scaledLocal, parentTransform.WorldRotation);

                return parentTransform.WorldPosition + rotatedLocal;
            }
        }

        // Local rotation in degrees
        public float Rotation;

        public float WorldRotation
        {
            get => Parent != null ? Parent.transform.WorldRotation + Rotation : Rotation;
        }

        // Local scale
        public Vector2 Scale = Vector2.One;

        public Vector2 WorldScale
        {
            get => Parent != null
                ? Parent.transform.WorldScale * Scale
                : Scale;
        }

        public void SetParent(GameObject? newParent, Scene? scene = null)
        {
            if (newParent == gameObject) return;
            if (newParent != null && newParent.transform.IsDescendantOf(gameObject)) return;

            // Preserve current world position, rotation, and scale
            var worldPos = WorldPosition;
            var worldRot = WorldRotation;
            var worldScale = WorldScale;

            // Detach from current parent or scene root
            if (Parent != null)
                Parent.children.Remove(gameObject);
            else if (scene != null)
                scene.RootObjects.Remove(gameObject);

            Parent = newParent;

            // Attach to new parent or scene root
            if (newParent != null)
            {
                newParent.children.Add(gameObject);

                // Recalculate local transforms relative to the new parent
                var parentTransform = newParent.transform;
                var parentScale = parentTransform.WorldScale;

                // Avoid divide-by-zero if parent scale is 0
                var safeParentScale = new Vector2(
                    parentScale.X != 0f ? parentScale.X : 0.0001f,
                    parentScale.Y != 0f ? parentScale.Y : 0.0001f
                );

                var deltaPos = worldPos - parentTransform.WorldPosition;
                var unrotatedPos = RotateVector(deltaPos, -parentTransform.WorldRotation);

                Position = unrotatedPos / safeParentScale;
                Rotation = worldRot - parentTransform.WorldRotation;
                Scale = worldScale / safeParentScale;
            }
            else
            {
                if (scene != null && !scene.RootObjects.Contains(gameObject))
                    scene.RootObjects.Add(gameObject);

                Position = worldPos;
                Rotation = worldRot;
                Scale = worldScale;
            }
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

        public void Rotate(float angle)
        {
            Rotation += angle;
        }

        private static Vector2 RotateVector(Vector2 v, float angleDegrees)
        {
            float radians = angleDegrees * (MathF.PI / 180f);
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        public override IEnumerable<InspectorField> GetInspectorFields()
        {
            yield return new FloatField { Label = "X", Get = () => Position.X, Set = (v) => Position = new Vector2(v, Position.Y) };
            yield return new FloatField { Label = "Y", Get = () => Position.Y, Set = (v) => Position = new Vector2(Position.X, v) };
            yield return new FloatField { Label = "Rotation", Get = () => Rotation, Set = (v) => Rotation = v };
            yield return new FloatField { Label = "Scale X", Get = () => Scale.X, Set = (v) => Scale = new Vector2(v, Scale.Y) };
            yield return new FloatField { Label = "Scale Y", Get = () => Scale.Y, Set = (v) => Scale = new Vector2(Scale.X, v) };
        }
    }
}