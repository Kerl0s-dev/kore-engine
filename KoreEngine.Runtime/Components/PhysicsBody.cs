namespace KoreEngine;

public class PhysicsBody : Component
{
    [HideInInspector] public Vector2 PreviousPosition;
    public Vector2 Velocity = Vector2.Zero;
    public bool IsStatic = false;
    public float GravityScale = 1f;
    public float Friction = 0f;
    public float Mass = 1f;
    public float Restitution = 0f; // 0 = pas de rebond, 1 = rebond parfait
    public float MaxFallSpeed = 50;
    public bool FreezeRotation = true;   // true = comportement actuel, aucune rotation
    public float AngularVelocity = 0f;   // °/s, positif = anti-horaire
    public float AngularDrag = 0.05f;
    public float SurfaceFriction = 0.4f; // coefficient de Coulomb

    public override void Update(float dt)
    {
        if (IsStatic) return;

        PreviousPosition = gameObject.transform.Position;

        Physics.CalculatePhysics(this, dt);

        if (GravityScale > 0)
            Velocity.Y = MathF.Max(Velocity.Y, -MaxFallSpeed); // clamp AVANT l'intégration

        // Intégration de la position — une seule fois, ici.
        gameObject.transform.Position += Velocity * dt;

        if (!FreezeRotation)
        {
            AngularVelocity /= 1f + AngularDrag * dt;
            if (AngularVelocity != 0f) RotateAroundCenterOfMass(AngularVelocity * dt);
        }
        else AngularVelocity = 0f;
    }

    void RotateAroundCenterOfMass(float deltaDeg)
    {
        var t = gameObject.transform;
        var collider = gameObject.GetComponent<Collider>();
        if (collider != null)
        {
            Vector2 com = collider.GetWorldShape().Center;
            float rad = deltaDeg * (MathF.PI / 180f);
            float c = MathF.Cos(rad), s = MathF.Sin(rad);
            Vector2 d = t.Position - com;
            t.Position = com + new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c);
        }
        t.Rotation += deltaDeg;
    }

    public void ApplyForce(Vector2 force) => Velocity += force / Mass;
    public void ApplyImpulse(Vector2 impulse) => Velocity += impulse;
}