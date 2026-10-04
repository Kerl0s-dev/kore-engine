namespace KoreEngine;

public class PhysicsBody : Component
{
    [HideInInspector] public Vector2 PreviousPosition;
    public Vector2 Velocity = Vector2.Zero;
    public bool IsStatic = false;
    public float GravityScale = 1f;
    public float Friction = 0f;
    public float Mass = 20f;
    public float Restitution = 0f; // 0 = pas de rebond, 1 = rebond parfait
    public float MaxFallSpeed = 1000f;
    public bool IsGrounded = false; // mis à jour par CollisionSystem

    public override void Update(float dt)
    {
        if (IsStatic) return;

        // Reset de la vélocité verticale si au sol
        if (IsGrounded && Velocity.Y > 0)
            Velocity.Y = 0;

        PreviousPosition = gameObject.transform.Position;
        IsGrounded = false; // reset chaque frame, rétabli par CollisionSystem

        Physics.CalculatePhysics(this, dt);

        if (GravityScale > 0)
            Velocity.Y = MathF.Max(Velocity.Y, -MaxFallSpeed); // clamp AVANT l'intégration

        // Intégration de la position — une seule fois, ici.
        gameObject.transform.Position += Velocity * dt;
    }

    public void ApplyForce(Vector2 force) => Velocity += force / Mass;
    public void ApplyImpulse(Vector2 impulse) => Velocity += impulse;
}