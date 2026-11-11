using SDL3;

namespace KoreEngine
{
    public class Time
    {
        public static float deltaTime;
        static ulong lastTicks = 0; // Utiliser ulong pour correspondre à SDL.GetTicks()

        /// <summary>Delta time en secondes.</summary>
        public static void ComputeDeltaTime()
        {
            ulong now = SDL.GetTicks();

            // Conversion des millisecondes en secondes (/ 1000f) et correction de la soustraction
            float dt = (now - lastTicks) / 1000f;

            lastTicks = now;

            deltaTime = MathF.Max(dt, 0.0001f);
        }
    }
}
