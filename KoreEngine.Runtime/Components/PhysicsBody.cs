using System;

namespace KoreEngine
{
    public class PhysicsBody : Component
    {
        [HideInInspector] public Vector2 PreviousPosition;
        public Vector2 Velocity = Vector2.Zero;
        public bool IsStatic = false;
        public float GravityScale = 1f;
        public float Friction = 0f;
        public float Mass = 1f;
        public float Restitution = 0f; // 0 = pas de rebond, 1 = rebond parfait
        public float MaxFallSpeed = 50f;
        public bool FreezeRotation = false;   // true = aucune rotation
        public float AngularVelocity = 0f;   // °/s, positif = anti-horaire
        public float AngularDrag = 0.05f;
        public float SurfaceFriction = 0.4f; // coefficient de Coulomb

        public override void Update()
        {
            if (IsStatic) return;

            PreviousPosition = gameObject.transform.Position;

            // 1. Appel de la physique (assurez-vous que l'accélération y est bien multipliée par Time.deltaTime)
            Physics.CalculatePhysics(this, Time.deltaTime);

            // 2. Clamp de la vitesse de chute (Monde Y-haut : la chute va vers les Y négatifs)
            if (GravityScale > 0)
            {
                Velocity.Y = MathF.Max(Velocity.Y, -MaxFallSpeed);
            }

            // 3. Intégration de la position
            gameObject.transform.Position += Velocity * Time.deltaTime;

            // 4. Gestion de la rotation
            if (!FreezeRotation)
            {
                AngularVelocity /= 1f + AngularDrag * Time.deltaTime;
                if (MathF.Abs(AngularVelocity) > 0.0001f)
                {
                    RotateAroundCenterOfMass(AngularVelocity * Time.deltaTime);
                }
            }
            else
            {
                AngularVelocity = 0f;
            }
        }

        void RotateAroundCenterOfMass(float deltaDeg)
        {
            var t = gameObject.transform;
            var collider = gameObject.GetComponent<Collider>();

            if (collider != null)
            {
                Vector2 com = collider.GetWorldShape().Center;

                // CORRECTION SYNTAXE 1 : Multiplication au lieu d'appeler deltaDeg comme une méthode
                float rad = deltaDeg * (MathF.PI / 180f);
                float c = MathF.Cos(rad), s = MathF.Sin(rad);
                Vector2 d = t.Position - com;

                t.Position = com + new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
            }

            t.Rotation += deltaDeg;
        }

        public void ApplyForce(Vector2 force) => Velocity += (force / Mass) * Time.deltaTime; // Assurez-vous d'appliquer les forces de manière cohérente
        public void ApplyImpulse(Vector2 impulse) => Velocity += impulse / Mass;
    }
}