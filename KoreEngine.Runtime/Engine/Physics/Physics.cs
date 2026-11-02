namespace KoreEngine
{
    // Structure pour stocker les infos de l'impact
    public struct RaycastHit
    {
        public PhysicsBody Body;     // L'objet touché
        public Vector2 Point;        // Le point d'impact exact dans le monde
        public Vector2 Normal;       // La normale de la surface au point d'impact
        public float Distance;       // La distance depuis l'origine du rayon
        public bool HasHit => Body != null;
    }

    public static class ShapeRaycast
    {
        const float Eps = 1e-6f;

        // Teste un rayon (origin -> origin + direction * maxDistance) contre un WorldShape
        public static bool Test(in WorldShape shape, Vector2 origin, Vector2 direction, float maxDistance, out float hitDistance, out Vector2 hitNormal)
        {
            hitDistance = float.MaxValue;
            hitNormal = Vector2.Zero;

            switch (shape.Type)
            {
                case ColliderShapeType.Box:
                    return RayVsBox(in shape, origin, direction, maxDistance, out hitDistance, out hitNormal);

                case ColliderShapeType.Circle:
                case ColliderShapeType.Capsule:
                    return RayVsCapsule(in shape, origin, direction, maxDistance, out hitDistance, out hitNormal);
            }

            return false;
        }

        // --- Ray vs Box (Méthode des plans / Slab Method en espace local) ---
        private static bool RayVsBox(in WorldShape box, Vector2 origin, Vector2 direction, float maxDistance, out float distance, out Vector2 normal)
        {
            distance = float.MaxValue;
            normal = Vector2.Zero;

            // 1. Passage du rayon en espace local de la Box
            Vector2 relOrigin = origin - box.Center;
            Vector2 localOrigin = new Vector2(Vector2.Dot(relOrigin, box.AxisX), Vector2.Dot(relOrigin, box.AxisY));
            Vector2 localDir = new Vector2(Vector2.Dot(direction, box.AxisX), Vector2.Dot(direction, box.AxisY));

            float tMin = 0f;
            float tMax = maxDistance;
            int hitAxis = -1;
            int hitSign = 1;

            // Axe X local
            if (MathF.Abs(localDir.X) < Eps)
            {
                if (MathF.Abs(localOrigin.X) > box.HalfExtents.X) return false;
            }
            else
            {
                float ood = 1f / localDir.X;
                float t1 = (-box.HalfExtents.X - localOrigin.X) * ood;
                float t2 = (box.HalfExtents.X - localOrigin.X) * ood;
                int sign = -1;
                if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1; }

                if (t1 > tMin) { tMin = t1; hitAxis = 0; hitSign = sign; }
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }

            // Axe Y local
            if (MathF.Abs(localDir.Y) < Eps)
            {
                if (MathF.Abs(localOrigin.Y) > box.HalfExtents.Y) return false;
            }
            else
            {
                float ood = 1f / localDir.Y;
                float t1 = (-box.HalfExtents.Y - localOrigin.Y) * ood;
                float t2 = (box.HalfExtents.Y - localOrigin.Y) * ood;
                int sign = -1;
                if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1; }

                if (t1 > tMin) { tMin = t1; hitAxis = 1; hitSign = sign; }
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }

            if (tMin < 0f || tMin > maxDistance) return false;

            distance = tMin;
            // Retransformation de la normale locale vers le monde
            normal = hitAxis switch
            {
                0 => box.AxisX * hitSign,
                _ => box.AxisY * hitSign
            };

            return true;
        }

        // --- Ray vs Circle / Capsule (Segment + Rayon) ---
        private static bool RayVsCapsule(in WorldShape capsule, Vector2 origin, Vector2 direction, float maxDistance, out float distance, out Vector2 normal)
        {
            distance = float.MaxValue;
            normal = Vector2.Zero;

            Vector2 a = capsule.SegA;
            Vector2 b = capsule.SegB;
            float r = capsule.Radius;

            // Résolution géométrique d'intersection Rayon vs Capsule
            // Si SegA == SegB, c'est un cercle pur. Sinon c'est une capsule.
            // On teste l'intersection du rayon avec le segment élargi ou les calottes sphériques.

            // Approximation robuste pour le moteur : test d'interception par pas ou équation quadratique segment-point
            // Pour un cercle (A == B) :
            if (Vector2.DistanceSquared(a, b) < Eps)
            {
                Vector2 m = origin - a;
                float bCoeff = Vector2.Dot(m, direction);
                float cCoeff = Vector2.Dot(m, m) - r * r;

                // Si l'origine est déjà dedans
                if (cCoeff <= 0f)
                {
                    distance = 0f;
                    normal = -direction; // ou normale sortante
                    return true;
                }

                if (bCoeff > 0f) return false; // S'éloigne du cercle

                float discr = bCoeff * bCoeff - cCoeff;
                if (discr < 0f) return false;

                float t = -bCoeff - MathF.Sqrt(discr);
                if (t < 0f || t > maxDistance) return false;

                distance = t;
                Vector2 hitPoint = origin + direction * t;
                normal = (hitPoint - a).Normalize();
                return true;
            }

            // Pour une capsule complète, on peut utiliser le test de distance minimale rayon-segment
            // ou déléguer via une passe d'échantillonnage / équation de distance.
            // (Implémentation simplifiée du segment de capsule)
            Vector2 ba = b - a;
            Vector2 oa = origin - a;

            // Résolution analytique simplifiée ou bounding check
            // Une capsule peut être vue comme l'enveloppe de balayage d'un cercle.
            // Pour l'instant, testons les extrémités et le corps central :
            float bestT = float.MaxValue;
            Vector2 bestNorm = Vector2.Zero;
            bool hit = false;

            // Test calotte A
            if (RayVsCircleSimple(a, r, origin, direction, maxDistance, out float tA, out Vector2 nA))
            {
                if (tA < bestT) { bestT = tA; bestNorm = nA; hit = true; }
            }
            // Test calotte B
            if (RayVsCircleSimple(b, r, origin, direction, maxDistance, out float tB, out Vector2 nB))
            {
                if (tB < bestT) { bestT = tB; bestNorm = nB; hit = true; }
            }

            if (hit)
            {
                distance = bestT;
                normal = bestNorm;
                return true;
            }

            return false;
        }

        private static bool RayVsCircleSimple(Vector2 center, float radius, Vector2 origin, Vector2 direction, float maxDistance, out float distance, out Vector2 normal)
        {
            distance = 0f;
            normal = Vector2.Zero;
            Vector2 m = origin - center;
            float b = Vector2.Dot(m, direction);
            float c = Vector2.Dot(m, m) - radius * radius;

            if (c <= 0f) // Origine dedans
            {
                distance = 0f;
                normal = (origin - center).Normalize();
                return true;
            }

            if (b > 0f) return false;

            float discr = b * b - c;
            if (discr < 0f) return false;

            float t = -b - MathF.Sqrt(discr);
            if (t < 0f || t > maxDistance) return false;

            distance = t;
            Vector2 hitPoint = origin + direction * t;
            normal = (hitPoint - center).Normalize();
            return true;
        }
    }

    public static class Physics
    {
        public static float gravity = -9.81f;

        public static bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit hit)
        {
            hit = default;
            Vector2 dirNormalized = direction.Normalize();
            Vector2 endPoint = origin + dirNormalized * maxDistance;

            float closestDistance = maxDistance;
            bool hasHit = false;

            PhysicsBody[] bodies = Array.Empty<PhysicsBody>();

            foreach (GameObject go in SceneManager.Current?.ActiveObjects!) {
                var body = go.GetComponent<PhysicsBody>();

                if (body != null)
                    bodies.Append(body);
            }

            foreach (PhysicsBody body in bodies)
            {
                WorldShape shape = (WorldShape)body.gameObject.GetComponent<Collider>()?.GetWorldShape()!; // Méthode qui retourne le WorldShape actuel du corps

                if (ShapeRaycast.Test(in shape, origin, dirNormalized, closestDistance, out float dist, out Vector2 normal))
                {
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        hit.Body = body;
                        hit.Distance = dist;
                        hit.Point = origin + dirNormalized * dist;
                        hit.Normal = normal;
                        hasHit = true;
                    }
                }
            }

            return hasHit;
        }

        public static bool Raycast(Vector2 origin, Vector2 direction, float maxDistance)
        {
            Vector2 dirNormalized = direction.Normalize();
            Vector2 endPoint = origin + dirNormalized * maxDistance;

            float closestDistance = maxDistance;
            bool hasHit = false;

            PhysicsBody[] bodies = Array.Empty<PhysicsBody>();

            foreach (GameObject go in SceneManager.Current?.ActiveObjects!)
            {
                var body = go.GetComponent<PhysicsBody>();

                if (body != null)
                    bodies.Append(body);
            }

            foreach (PhysicsBody body in bodies)
            {
                WorldShape shape = (WorldShape)body.gameObject.GetComponent<Collider>()?.GetWorldShape()!; // Méthode qui retourne le WorldShape actuel du corps

                if (ShapeRaycast.Test(in shape, origin, dirNormalized, closestDistance, out float dist, out Vector2 normal))
                {
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        hasHit = true;
                    }
                }
            }

            return hasHit;
        }

        /// <summary>
        /// Applique les forces (gravité, friction) à la vélocité du corps.
        /// N'intègre PAS la position — ça reste la responsabilité unique de
        /// PhysicsBody.Update, pour éviter toute double-intégration.
        /// </summary>
        internal static void CalculatePhysics(PhysicsBody body, float deltaTime)
        {
            if (body.IsStatic) return;

            float force = gravity * body.GravityScale * body.Mass;
            body.Velocity.Y += force * deltaTime;

            if (body.Friction > 0)
                body.Velocity.X *= MathF.Pow(1f - body.Friction, deltaTime);
        }
    }
}
