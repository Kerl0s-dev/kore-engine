namespace KoreEngine
{
    public class CollisionSystem
    {
        List<Collider> colliders = new();

        public void Register(Collider c) => colliders.Add(c);
        public void Unregister(Collider c) => colliders.Remove(c);

        // Convention utilisée partout :
        //   normal = direction dans laquelle on repousse A hors de B
        //   pour B, la normale de repoussage est -normal.
        public void Update(float dt)
        {
            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    var a = colliders[i];
                    var b = colliders[j];

                    PhysicsBody? bodyA = a.gameObject.GetComponent<PhysicsBody>();
                    PhysicsBody? bodyB = b.gameObject.GetComponent<PhysicsBody>();

                    if (bodyA == null || bodyB == null) continue;

                    bool aStatic = bodyA.IsStatic;
                    bool bStatic = bodyB.IsStatic;

                    if (aStatic && bStatic) continue;

                    CheckGrounded(a, b, bodyA, bodyB, aStatic, bStatic);

                    // Déplacement réel effectué par PhysicsBody pendant cette frame
                    Vector2 dispA = Displacement(a, bodyA);
                    Vector2 dispB = Displacement(b, bodyB);

                    bool hit = SweptAABB(a.Bounds, dispA, b.Bounds, dispB,
                                         out float tFirst, out float tLast, out Vector2 normal);

                    if (!hit) continue;

                    if (a.IsTrigger || b.IsTrigger)
                    {
                        a.OnTriggerEnter(b);
                        b.OnTriggerEnter(a);
                        continue;
                    }

                    float restitution = MathF.Max(bodyA.Restitution, bodyB.Restitution);

                    if (aStatic || bStatic)
                    {
                        // Le corps dynamique est celui qu'on déplace
                        Collider dyn = aStatic ? b : a;
                        PhysicsBody dynBody = aStatic ? bodyB : bodyA;
                        Vector2 dynDisp = aStatic ? dispB : dispA;

                        if (tFirst <= 0f)
                        {
                            // Déjà en overlap au début de la frame — résolution AABB classique
                            if (!ResolveOverlap(a.Bounds, b.Bounds, out Vector2 pushA, out normal))
                                continue; // pas de pénétration réelle

                            Vector2 pushDyn = aStatic ? -pushA : pushA;
                            dyn.gameObject.transform.Position += pushDyn;
                        }
                        else
                        {
                            // Contact en cours de frame : on retire uniquement la partie du
                            // mouvement restant qui va DANS la surface (le glissement est conservé)
                            Vector2 dynNormal = aStatic ? -normal : normal;
                            PushOut(dyn, dynDisp, 1f - tFirst, dynNormal);
                        }

                        Vector2 n = aStatic ? -normal : normal;
                        CancelVelocity(dynBody, n, restitution);
                    }
                    else
                    {
                        float totalMass = bodyA.Mass + bodyB.Mass;
                        float ratioA = bodyB.Mass / totalMass; // le plus léger bouge le plus
                        float ratioB = bodyA.Mass / totalMass;

                        Vector2 push;

                        if (tFirst <= 0f)
                        {
                            if (!ResolveOverlap(a.Bounds, b.Bounds, out push, out normal))
                                continue;
                        }
                        else
                        {
                            float remaining = 1f - tFirst;
                            float relX = (dispA.X - dispB.X) * remaining;
                            float relY = (dispA.Y - dispB.Y) * remaining;
                            float into = relX * normal.X + relY * normal.Y;

                            push = into < 0f
                                ? new Vector2(normal.X * -into, normal.Y * -into)
                                : Vector2.Zero;
                        }

                        a.gameObject.transform.Position += new Vector2(push.X * ratioA, push.Y * ratioA);
                        b.gameObject.transform.Position -= new Vector2(push.X * ratioB, push.Y * ratioB);

                        CancelVelocity(bodyA, normal, restitution);
                        CancelVelocity(bodyB, -normal, restitution);
                    }

                    a.OnCollision(b);
                    b.OnCollision(a);
                }
            }
        }

        // Déplacement effectué par le corps pendant la frame (position actuelle - position avant intégration)
        static Vector2 Displacement(Collider c, PhysicsBody body)
        {
            if (body.IsStatic) return Vector2.Zero;
            Vector2 p = c.gameObject.transform.Position;
            return new Vector2(p.X - body.PreviousPosition.X, p.Y - body.PreviousPosition.Y);
        }

        // Retire de 'disp * remaining' la composante qui va vers la surface (n = normale de repoussage du corps)
        static void PushOut(Collider c, Vector2 disp, float remaining, Vector2 n)
        {
            float rx = disp.X * remaining;
            float ry = disp.Y * remaining;
            float into = rx * n.X + ry * n.Y; // < 0 si on va vers la surface
            if (into >= 0f) return;
            c.gameObject.transform.Position -= new Vector2(n.X * into, n.Y * into);
        }

        // Pénétration réelle (bounds actuels) : renvoie le vecteur qui repousse A hors de B
        // et la normale correspondante (direction de repoussage de A).
        static bool ResolveOverlap(Rectangle ra, Rectangle rb, out Vector2 pushA, out Vector2 normal)
        {
            pushA = Vector2.Zero;
            normal = Vector2.Zero;

            float overlapX = MathF.Min(ra.Right, rb.Right) - MathF.Max(ra.Left, rb.Left);
            float overlapY = MathF.Min(ra.Bottom, rb.Bottom) - MathF.Max(ra.Top, rb.Top);

            if (overlapX <= 0f || overlapY <= 0f) return false;

            float centerAX = ra.X + ra.Width * 0.5f;
            float centerAY = ra.Y + ra.Height * 0.5f;
            float centerBX = rb.X + rb.Width * 0.5f;
            float centerBY = rb.Y + rb.Height * 0.5f;

            if (overlapX < overlapY)
            {
                float sign = centerAX < centerBX ? -1f : 1f;
                pushA = new Vector2(overlapX * sign, 0f);
                normal = new Vector2(sign, 0f);
            }
            else
            {
                float sign = centerAY < centerBY ? -1f : 1f;
                pushA = new Vector2(0f, overlapY * sign);
                normal = new Vector2(0f, sign);
            }

            return true;
        }

        // Sweep de A contre B, chacun avec son déplacement sur la frame.
        // Les bounds passés sont ceux de FIN de frame ; on remonte au début en soustrayant le déplacement.
        // tFirst / tLast sont exprimés en fraction de la frame (0-1).
        // normal = direction de repoussage de A.
        bool SweptAABB(Rectangle a, Vector2 dispA, Rectangle b, Vector2 dispB,
                       out float tFirst, out float tLast, out Vector2 normal)
        {
            tFirst = 0f;
            tLast = 1f;
            normal = Vector2.Zero;

            // Bounds au début de la frame
            float aLeft = a.Left - dispA.X, aRight = a.Right - dispA.X;
            float aTop = a.Top - dispA.Y, aBottom = a.Bottom - dispA.Y;
            float bLeft = b.Left - dispB.X, bRight = b.Right - dispB.X;
            float bTop = b.Top - dispB.Y, bBottom = b.Bottom - dispB.Y;

            // Déplacement relatif de A par rapport à B
            float dx = dispA.X - dispB.X;
            float dy = dispA.Y - dispB.Y;

            float tFirstX = float.NegativeInfinity, tLastX = float.PositiveInfinity;
            float tFirstY = float.NegativeInfinity, tLastY = float.PositiveInfinity;
            Vector2 normalX = Vector2.Zero;
            Vector2 normalY = Vector2.Zero;

            // Axe X
            if (MathF.Abs(dx) < 0.0001f)
            {
                if (aRight <= bLeft || aLeft >= bRight) return false;
            }
            else if (dx > 0)
            {
                tFirstX = (bLeft - aRight) / dx;
                tLastX = (bRight - aLeft) / dx;
                normalX = new Vector2(-1, 0);
            }
            else
            {
                tFirstX = (bRight - aLeft) / dx;
                tLastX = (bLeft - aRight) / dx;
                normalX = new Vector2(1, 0);
            }

            // Axe Y
            if (MathF.Abs(dy) < 0.0001f)
            {
                if (aBottom <= bTop || aTop >= bBottom) return false;
            }
            else if (dy > 0)
            {
                tFirstY = (bTop - aBottom) / dy;
                tLastY = (bBottom - aTop) / dy;
                normalY = new Vector2(0, -1);
            }
            else
            {
                tFirstY = (bBottom - aTop) / dy;
                tLastY = (bTop - aBottom) / dy;
                normalY = new Vector2(0, 1);
            }

            if (tFirstX > tLastY || tFirstY > tLastX) return false;

            tFirst = MathF.Max(tFirstX, tFirstY);
            tLast = MathF.Min(tLastX, tLastY);

            // tLast <= 0 : les objets se touchent tout juste et s'éloignent (ex. saut depuis le sol)
            if (tFirst > 1f || tLast <= 0f) return false;

            normal = tFirstX > tFirstY ? normalX : normalY;

            tFirst = Math.Clamp(tFirst, 0f, 1f);

            return true;
        }

        void CheckGrounded(Collider a, Collider b, PhysicsBody bodyA, PhysicsBody bodyB, bool aStatic, bool bStatic)
        {
            Rectangle ra = a.Bounds;
            Rectangle rb = b.Bounds;

            float overlapX = MathF.Min(ra.Right, rb.Right) - MathF.Max(ra.Left, rb.Left);

            if (overlapX <= 0) return;

            if (!aStatic && bStatic)
            {
                float distBottom = rb.Top - ra.Bottom;
                if (distBottom >= -8f && distBottom <= 2f)
                    bodyA.IsGrounded = true;
            }
            else if (aStatic && !bStatic)
            {
                float distBottom = ra.Top - rb.Bottom;
                if (distBottom >= -10f && distBottom <= 2f)
                    bodyB.IsGrounded = true;
            }
        }

        // normal = direction de repoussage de CE corps
        void CancelVelocity(PhysicsBody body, Vector2 normal, float restitution)
        {
            float dot = body.Velocity.X * normal.X + body.Velocity.Y * normal.Y;
            if (dot < 0)
            {
                // Annule la composante vers la surface (et la renvoie * restitution).
                // Le glissement le long de la surface est conservé.
                body.Velocity -= normal * dot * (1f + restitution);
            }

            if (normal.Y < -0.5f)
                body.IsGrounded = true;
        }
    }
}