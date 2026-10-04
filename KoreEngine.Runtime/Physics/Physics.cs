namespace KoreEngine
{
    public static class Physics
    {
        public static float gravity = -9.81f;

        /// <summary>
        /// Applique les forces (gravité, friction) à la vélocité du corps.
        /// N'intègre PAS la position — ça reste la responsabilité unique de
        /// PhysicsBody.Update, pour éviter toute double-intégration.
        /// </summary>
        public static void CalculatePhysics(PhysicsBody body, float deltaTime)
        {
            if (body.IsStatic) return;

            float force = gravity * body.GravityScale * body.Mass;
            body.Velocity.Y += force * deltaTime;

            if (body.Friction > 0)
                body.Velocity.X *= MathF.Pow(1f - body.Friction, deltaTime);
        }
    }
}
