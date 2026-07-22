using KoreEngine.Core;
using KoreEngine.Components;

namespace KoreEngine.Physics
{
    public class CollisionSystem
    {
        List<Collider> colliders = new();

        public void Register(Collider c) => colliders.Add(c);
        public void Unregister(Collider c) => colliders.Remove(c);

        public void Update(float dt)
        {
            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    var a = colliders[i];
                    var b = colliders[j];

                    PhysicsBody? bodyA = a.Owner.GetComponent<PhysicsBody>();
                    PhysicsBody? bodyB = b.Owner.GetComponent<PhysicsBody>();

                    if (bodyA == null || bodyB == null) return;
                    
                    bool aStatic = bodyA.IsStatic;
                    bool bStatic = bodyB.IsStatic;

                    if (aStatic && bStatic) continue;

                    CheckGrounded(a, b, bodyA, bodyB, aStatic, bStatic);

                    // Vélocité relative entre les deux objets
                    Vector2 velA = bodyA != null && !bodyA.IsStatic ? bodyA.Velocity : Vector2.Zero;
                    Vector2 velB = bodyB != null && !bodyB.IsStatic ? bodyB.Velocity : Vector2.Zero;
                    Vector2 relVel = new Vector2(velA.X - velB.X, velA.Y - velB.Y);

                    float tFirst, tLast;
                    Vector2 normal;

                    bool hit = SweptAABB(a.Bounds, b.Bounds, relVel, dt,
                                            out tFirst, out tLast, out normal);

                    if (!hit) continue;

                    if (a.IsTrigger || b.IsTrigger)
                    {
                        a.OnTriggerEnter?.Invoke(a, new CollisionInfo { Other = b, Normal = -normal, Penetration = 0 });
                        b.OnTriggerEnter?.Invoke(b, new CollisionInfo { Other = a, Normal = normal, Penetration = 0 });
                        continue;
                    }

                    // Déplace les objets jusqu'au point de contact
                    if (aStatic)
                    {
                        if (tFirst <= 0f)
                        {
                            // Déjà en overlap — résolution AABB classique
                            Rectangle ra = a.Bounds;
                            Rectangle rb = b.Bounds;

                            float overlapX = MathF.Min(ra.Right, rb.Right) - MathF.Max(ra.Left, rb.Left);
                            float overlapY = MathF.Min(ra.Bottom, rb.Bottom) - MathF.Max(ra.Top, rb.Top);

                            float centerAX = ra.X + ra.Width * 0.5f;
                            float centerAY = ra.Y + ra.Height * 0.5f;
                            float centerBX = rb.X + rb.Width * 0.5f;
                            float centerBY = rb.Y + rb.Height * 0.5f;

                            if (overlapX < overlapY)
                            {
                                float sign = centerAX < centerBX ? -1 : 1;
                                b.Owner.Position -= new Vector2(overlapX * sign, 0);
                                normal = new Vector2(-sign, 0);
                            }
                            else
                            {
                                float sign = centerAY < centerBY ? -1 : 1;
                                b.Owner.Position -= new Vector2(0, overlapY * sign);
                                normal = new Vector2(0, -sign);
                            }
                        }
                        else
                        {
                            Vector2 excess = new Vector2(velA.X * dt * (1f - tFirst),
                                                            velA.Y * dt * (1f - tFirst));
                            b.Owner.Position -= excess;
                        }

                        if (bodyB == null) return;

                        CancelVelocity(bodyB, -normal);
                    }
                    else if (bStatic)
                    {
                        if (tFirst <= 0f)
                        {
                            // Déjà en overlap — résolution AABB classique
                            Rectangle ra = a.Bounds;
                            Rectangle rb = b.Bounds;

                            float overlapX = MathF.Min(ra.Right, rb.Right) - MathF.Max(ra.Left, rb.Left);
                            float overlapY = MathF.Min(ra.Bottom, rb.Bottom) - MathF.Max(ra.Top, rb.Top);

                            float centerAX = ra.X + ra.Width * 0.5f;
                            float centerAY = ra.Y + ra.Height * 0.5f;
                            float centerBX = rb.X + rb.Width * 0.5f;
                            float centerBY = rb.Y + rb.Height * 0.5f;

                            if (overlapX < overlapY)
                            {
                                float sign = centerAX < centerBX ? -1 : 1;
                                a.Owner.Position -= new Vector2(overlapX * sign, 0);
                                normal = new Vector2(-sign, 0);
                            }
                            else
                            {
                                float sign = centerAY < centerBY ? -1 : 1;
                                a.Owner.Position -= new Vector2(0, overlapY * sign);
                                normal = new Vector2(0, -sign);
                            }
                        }
                        else
                        {
                            Vector2 excess = new Vector2(velA.X * dt * (1f - tFirst),
                                                            velA.Y * dt * (1f - tFirst));
                            a.Owner.Position -= excess;
                        }

                        if (bodyA == null) return;

                        CancelVelocity(bodyA, -normal);
                    }
                    else
                    {
                        if (bodyA == null || bodyB == null) return;

                        float totalMass = bodyA.Mass + bodyB.Mass;
                        float ratioA = bodyB.Mass / totalMass;
                        float ratioB = bodyA.Mass / totalMass;

                        a.Owner.Position -= new Vector2(velA.X * (1f - tFirst) * dt * ratioA,
                                                        velA.Y * (1f - tFirst) * dt * ratioA);
                        b.Owner.Position -= new Vector2(velB.X * (1f - tFirst) * dt * ratioB,
                                                        velB.Y * (1f - tFirst) * dt * ratioB);
                        CancelVelocity(bodyA, -normal);
                        CancelVelocity(bodyB, normal);
                    }

                    a.OnCollision?.Invoke(a, new CollisionInfo { Other = b, Normal = -normal, Penetration = 0 });
                    b.OnCollision?.Invoke(b, new CollisionInfo { Other = a, Normal = normal, Penetration = 0 });
                }
            }
        }

        // Calcule quand A (en mouvement avec relVel) va toucher B (statique)
        // Retourne true si collision, tFirst = moment d'impact (0-1)
        bool SweptAABB(Rectangle a, Rectangle b, Vector2 relVel, float dt,
                       out float tFirst, out float tLast, out Vector2 normal)
        {
            tFirst = 0f;
            tLast = 1f;
            normal = Vector2.Zero;

            float dx = relVel.X * dt;
            float dy = relVel.Y * dt;

            float tFirstX = 0f, tLastX = 1f;
            float tFirstY = 0f, tLastY = 1f;
            Vector2 normalX = Vector2.Zero;
            Vector2 normalY = Vector2.Zero;

            // Axe X
            if (MathF.Abs(dx) < 0.0001f)
            {
                // Pas de mouvement sur X — vérifie si déjà en overlap
                if (a.Right <= b.Left || a.Left >= b.Right)
                    return false;
            }
            else
            {
                if (dx > 0)
                {
                    tFirstX = (b.Left - a.Right) / dx;
                    tLastX = (b.Right - a.Left) / dx;
                    normalX = new Vector2(-1, 0);
                }
                else
                {
                    tFirstX = (b.Right - a.Left) / dx;
                    tLastX = (b.Left - a.Right) / dx;
                    normalX = new Vector2(1, 0);
                }
            }

            // Axe Y
            if (MathF.Abs(dy) < 0.0001f)
            {
                if (a.Bottom <= b.Top || a.Top >= b.Bottom)
                    return false;
            }
            else
            {
                if (dy > 0)
                {
                    tFirstY = (b.Top - a.Bottom) / dy;
                    tLastY = (b.Bottom - a.Top) / dy;
                    normalY = new Vector2(0, -1);
                }
                else
                {
                    tFirstY = (b.Bottom - a.Top) / dy;
                    tLastY = (b.Top - a.Bottom) / dy;
                    normalY = new Vector2(0, 1);
                }
            }

            // Temps d'entrée = max des deux axes (les deux doivent se chevaucher)
            // Temps de sortie = min des deux axes
            if (tFirstX > tLastY || tFirstY > tLastX)
                return false;

            tFirst = MathF.Max(tFirstX, tFirstY);
            tLast = MathF.Min(tLastX, tLastY);

            if (tFirst > 1f || tLast < 0f) return false;

            tFirst = Math.Clamp(tFirst, 0f, 1f);

            // La normale est celle de l'axe qui entre en collision en dernier
            normal = tFirstX > tFirstY ? normalX : normalY;

            return true;
        }

        void CheckGrounded(Collider a, Collider b, PhysicsBody bodyA, PhysicsBody bodyB, bool aStatic, bool bStatic)
        {
            Rectangle ra = a.Bounds;
            Rectangle rb = b.Bounds;

            float overlapX = MathF.Min(ra.Right, rb.Right) - MathF.Max(ra.Left, rb.Left);
            float overlapY = MathF.Min(ra.Bottom, rb.Bottom) - MathF.Max(ra.Top, rb.Top);

            if (overlapX <= 0) return;

            if (!aStatic && bStatic)
            {
                float distBottom = rb.Top - ra.Bottom;
                if (distBottom >= -8f && distBottom <= 2f && bodyA != null)
                    bodyA.IsGrounded = true;
            }
            else if (aStatic && !bStatic)
            {
                float distBottom = ra.Top - rb.Bottom;
                if (distBottom >= -10f && distBottom <= 2f && bodyB != null)
                    bodyB.IsGrounded = true;
            }
        }

        void CancelVelocity(PhysicsBody body, Vector2 normal)
        {
            if (body == null) return;
            float dot = body.Velocity.X * normal.X + body.Velocity.Y * normal.Y;
            if (dot < 0)
            {
                body.Velocity -= normal * dot;
                if (MathF.Abs(normal.X) > 1f) body.Velocity.X = 0;
                if (MathF.Abs(normal.Y) > 1f) body.Velocity.Y = 0;
            }

            // Sol détecté si la normale pointe vers le haut
            if (normal.Y < -0.5f)
                body.IsGrounded = true;
        }
    }
}