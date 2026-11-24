using System.Runtime.InteropServices;

namespace KoreEngine;

/// <summary>État d'un corps pendant la résolution des contacts (unités SI : m/s, rad/s).</summary>
public sealed class SolverBody
{
    public Vector2 Velocity;
    public float AngularVelocity;   // rad/s, positif = anti-horaire
    public float InvMass;           // 0 = masse infinie (corps statique)
    public float InvInertia;        // 0 = rotation bloquée
    public object? Source;          // objet d'origine (PhysicsBody) pour la réécriture des vitesses
    public bool IsStatic;
}

/// <summary>
/// Résolution séquentielle d'impulsions (friction de Coulomb + contact normal + restitution)
/// avec accumulation et clamp des impulsions. Les positions sont corrigées ailleurs
/// (CollisionSystem) : ce solveur ne touche qu'aux vitesses.
/// </summary>
public sealed class ContactSolver
{
    /// <summary>Sous cette vitesse d'impact (m/s), pas de rebond : évite les rebonds infinitésimaux au repos.</summary>
    public const float RestitutionThreshold = 1.0f;

    struct Point
    {
        public Vector2 RA, RB;
        public float NormalMass, TangentMass;
        public float Jn, Jt;
        public float Bias;
    }

    struct Constraint
    {
        public SolverBody A, B;
        public Vector2 N, T;
        public float Friction;
        public int Count;
        public Point P0, P1;
    }

    readonly List<Constraint> constraints = new();

    public int Count => constraints.Count;

    public void Clear() => constraints.Clear();

    static Vector2 VelocityAt(SolverBody b, Vector2 r)
        => new(b.Velocity.X - b.AngularVelocity * r.Y, b.Velocity.Y + b.AngularVelocity * r.X);

    static void Prepare(ref Point p, SolverBody a, SolverBody b, Vector2 n, Vector2 t, Vector2 contact,
                        Vector2 centerA, Vector2 centerB, float restitution)
    {
        p.RA = contact - centerA;
        p.RB = contact - centerB;

        float rnA = Vector2.Cross(p.RA, n), rnB = Vector2.Cross(p.RB, n);
        float kN = a.InvMass + b.InvMass + a.InvInertia * rnA * rnA + b.InvInertia * rnB * rnB;
        p.NormalMass = kN > 0f ? 1f / kN : 0f;

        float rtA = Vector2.Cross(p.RA, t), rtB = Vector2.Cross(p.RB, t);
        float kT = a.InvMass + b.InvMass + a.InvInertia * rtA * rtA + b.InvInertia * rtB * rtB;
        p.TangentMass = kT > 0f ? 1f / kT : 0f;

        Vector2 vrel = VelocityAt(a, p.RA) - VelocityAt(b, p.RB);
        float vn = Vector2.Dot(vrel, n);
        p.Bias = vn < -RestitutionThreshold ? -restitution * vn : 0f;
        p.Jn = 0f;
        p.Jt = 0f;
    }

    /// <param name="normal">Direction de repoussage de A (de B vers A), unitaire.</param>
    /// <param name="centerA">Centre de masse de A au moment du contact (monde).</param>
    public void Add(SolverBody a, SolverBody b, Vector2 normal, in ContactManifold m,
                    Vector2 centerA, Vector2 centerB, float restitution, float friction)
    {
        var c = new Constraint
        {
            A = a,
            B = b,
            N = normal,
            T = new Vector2(-normal.Y, normal.X),
            Friction = friction,
            Count = m.Count
        };
        Prepare(ref c.P0, a, b, c.N, c.T, m.P0, centerA, centerB, restitution);
        if (m.Count > 1)
            Prepare(ref c.P1, a, b, c.N, c.T, m.P1, centerA, centerB, restitution);
        constraints.Add(c);
    }

    static void ApplyImpulse(SolverBody a, SolverBody b, Vector2 ra, Vector2 rb, Vector2 impulse)
    {
        a.Velocity += impulse * a.InvMass;
        a.AngularVelocity += a.InvInertia * Vector2.Cross(ra, impulse);
        b.Velocity -= impulse * b.InvMass;
        b.AngularVelocity -= b.InvInertia * Vector2.Cross(rb, impulse);
    }

    static void SolvePoint(ref Constraint c, ref Point p)
    {
        SolverBody a = c.A, b = c.B;

        // Normale (l'impulsion accumulée ne peut pas devenir attractive)
        Vector2 vrel = VelocityAt(a, p.RA) - VelocityAt(b, p.RB);
        float vn = Vector2.Dot(vrel, c.N);
        float jn = -(vn - p.Bias) * p.NormalMass;
        float newJn = MathF.Max(p.Jn + jn, 0f);
        jn = newJn - p.Jn;
        p.Jn = newJn;
        ApplyImpulse(a, b, p.RA, p.RB, c.N * jn);

        // Friction (borne de Coulomb : |jt| <= mu * jn accumulé), après la normale pour utiliser son impulsion à jour
        vrel = VelocityAt(a, p.RA) - VelocityAt(b, p.RB);
        float vt = Vector2.Dot(vrel, c.T);
        float jt = -vt * p.TangentMass;
        float maxF = c.Friction * p.Jn;
        float newJt = Math.Clamp(p.Jt + jt, -maxF, maxF);
        jt = newJt - p.Jt;
        p.Jt = newJt;
        ApplyImpulse(a, b, p.RA, p.RB, c.T * jt);
    }

    public void Solve(int iterations)
    {
        Span<Constraint> span = CollectionsMarshal.AsSpan(constraints);
        for (int it = 0; it < iterations; it++)
        {
            for (int i = 0; i < span.Length; i++)
            {
                ref Constraint c = ref span[i];
                SolvePoint(ref c, ref c.P0);
                if (c.Count > 1) SolvePoint(ref c, ref c.P1);
            }
        }
    }
}