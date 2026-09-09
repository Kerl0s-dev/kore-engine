using KoreEngine.Components;

namespace KoreEngine
{
    public class Physics
    {
        public static float gravity = -9.81f;

        public static void CalculatePhysics(PhysicsBody body, float deltaTime)
        {
            if (body.IsStatic) return;

            // Calculate the force applied to the body
            float force = gravity * body.GravityScale * body.Mass;
            body.Velocity.Y += force * deltaTime;

            // Apply friction
            if (body.Friction > 0)
                body.Velocity.X *= MathF.Pow(1f - body.Friction, deltaTime);

            // Update position
            body.Owner.Position += body.Velocity * deltaTime;
        }
    }
}
