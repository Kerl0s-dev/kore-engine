namespace KoreEngine;

/// <summary>
/// Relie le ContactSolver (pur, en SI) aux PhysicsBody / Collider du moteur.
/// Cycle par frame : Begin() -> Add(...) pour chaque contact SAT -> SolveAndApply().
/// </summary>
internal sealed class RigidBodyBridge
{
    const float Deg2Rad = MathF.PI / 180f;
    const float Rad2Deg = 180f / MathF.PI;

    public int Iterations = 10;

    readonly ContactSolver solver = new();
    readonly Dictionary<PhysicsBody, SolverBody> bodies = new();
    readonly List<SolverBody> pool = new();
    int poolUsed;

    public void Begin()
    {
        solver.Clear();
        bodies.Clear();
        poolUsed = 0;
    }

    SolverBody Get(Collider c, PhysicsBody p)
    {
        if (bodies.TryGetValue(p, out var sb)) return sb;

        if (poolUsed == pool.Count) pool.Add(new SolverBody());
        sb = pool[poolUsed++];
        sb.Source = p;
        sb.IsStatic = p.IsStatic;

        if (p.IsStatic)
        {
            sb.Velocity = Vector2.Zero;
            sb.AngularVelocity = 0f;
            sb.InvMass = 0f;
            sb.InvInertia = 0f;
        }
        else
        {
            sb.Velocity = p.Velocity;
            sb.AngularVelocity = p.AngularVelocity * Deg2Rad;
            sb.InvMass = 1f / MathF.Max(p.Mass, 1e-4f);
            float inertia = p.FreezeRotation ? 0f : c.ComputeInertia(p.Mass);
            sb.InvInertia = inertia > 1e-6f ? 1f / inertia : 0f;
        }

        bodies[p] = sb;
        return sb;
    }

    /// <param name="normal">Direction de repoussage de A (de B vers A).</param>
    public void Add(Collider ca, PhysicsBody pa, Collider cb, PhysicsBody pb,
                    in WorldShape sa, in WorldShape sb, Vector2 normal, in ContactManifold manifold)
    {
        float restitution = MathF.Max(pa.Restitution, pb.Restitution);
        float mu = MathF.Sqrt(MathF.Max(pa.SurfaceFriction, 0f) * MathF.Max(pb.SurfaceFriction, 0f));

        solver.Add(Get(ca, pa), Get(cb, pb), normal, manifold, sa.Center, sb.Center, restitution, mu);
    }

    public void SolveAndApply()
    {
        if (solver.Count == 0) return;
        solver.Solve(Iterations);

        foreach (var kv in bodies)
        {
            var sb = kv.Value;
            if (sb.IsStatic) continue;
            kv.Key.Velocity = sb.Velocity;
            kv.Key.AngularVelocity = sb.AngularVelocity * Rad2Deg;
        }
    }
}