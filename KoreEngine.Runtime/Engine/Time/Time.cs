using SDL3;

namespace KoreEngine
{
    public class Time
    {
        public static float deltaTime;
        static float lastTicks = 0;

        /// <summary>Delta time en millisencondes.</summary>
        public static void ComputeDeltaTime()
        {
            ulong now = SDL.GetTicks();
            float dt = (now - lastTicks);
            lastTicks = now;

            deltaTime = MathF.Max(dt, 0.0001f);
        }
    }
}
