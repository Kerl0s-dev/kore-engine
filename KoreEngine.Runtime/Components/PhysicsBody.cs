using KoreEngine.Core;

namespace KoreEngine.Components;

public class PhysicsBody : Component
{
    public Vector2 Velocity;
    public bool IsStatic = false;
    public float GravityScale = 0f;
    public static float GlobalGravity = 800f;
    public float Friction = 0f;
    public float Mass = 1f;
    public float MaxFallSpeed = 1000f;
    public bool IsGrounded = false; // mis à jour par CollisionSystem

    public override void Update(float dt)
    {
        if (IsStatic) return;

        // Reset de la vélocité verticale si au sol
        if (IsGrounded && Velocity.Y > 0)
            Velocity.Y = 0;

        IsGrounded = false; // reset chaque frame, rétabli par CollisionSystem

        Velocity.Y += GlobalGravity * GravityScale * dt;

        if (GravityScale > 0)
            Velocity.Y = MathF.Min(Velocity.Y, MaxFallSpeed);

        if (Friction > 0)
            Velocity.X *= MathF.Pow(1f - Friction, dt);

        Owner.Position += Velocity * dt;
    }

    public void ApplyForce(Vector2 force) => Velocity += force / Mass;
    public void ApplyImpulse(Vector2 impulse) => Velocity += impulse;
}