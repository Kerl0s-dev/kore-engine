namespace KoreEngine;

public enum ColliderShapeType
{
    Box,
    Circle,
    Capsule
}

/// <summary>
/// Forme d'un collider dans l'espace monde (mètres, Y vers le haut).
/// Box = rectangle orienté (OBB). Circle/Capsule = segment [SegA, SegB] épaissi de Radius
/// (un cercle est une capsule dont le segment est réduit à un point).
/// </summary>
public struct WorldShape
{
    public ColliderShapeType Type;
    public Vector2 Center;
    public Vector2 AxisX, AxisY;   // axes locaux exprimés en monde (unitaires, anti-horaire)
    public Vector2 HalfExtents;    // Box uniquement
    public Vector2 SegA, SegB;     // Circle / Capsule
    public float Radius;           // Circle / Capsule (0 pour Box)

    /// <summary>Vrai pour une Box dont les axes sont alignés sur le monde (rotation multiple de 90°).</summary>
    public bool IsAxisAlignedBox => Type == ColliderShapeType.Box && MathF.Abs(AxisX.X * AxisX.Y) < 1e-5f;

    /// <summary>AABB englobant (broadphase, CheckGrounded, sélection dans l'éditeur).</summary>
    public Rectangle Bounds
    {
        get
        {
            if (Type == ColliderShapeType.Box)
            {
                float ex = MathF.Abs(AxisX.X) * HalfExtents.X + MathF.Abs(AxisY.X) * HalfExtents.Y;
                float ey = MathF.Abs(AxisX.Y) * HalfExtents.X + MathF.Abs(AxisY.Y) * HalfExtents.Y;
                return new Rectangle(Center.X - ex, Center.Y - ey, ex * 2f, ey * 2f);
            }

            float minX = MathF.Min(SegA.X, SegB.X) - Radius;
            float maxX = MathF.Max(SegA.X, SegB.X) + Radius;
            float minY = MathF.Min(SegA.Y, SegB.Y) - Radius;
            float maxY = MathF.Max(SegA.Y, SegB.Y) + Radius;
            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }
    }

    internal Core GetCore()
    {
        var c = new Core();
        if (Type == ColliderShapeType.Box)
        {
            Vector2 ex = AxisX * HalfExtents.X;
            Vector2 ey = AxisY * HalfExtents.Y;
            c.Count = 4;                       // sens anti-horaire
            c.V0 = Center - ex - ey;
            c.V1 = Center + ex - ey;
            c.V2 = Center + ex + ey;
            c.V3 = Center - ex + ey;
        }
        else if (SegA == SegB)
        {
            c.Count = 1;
            c.V0 = SegA;
        }
        else
        {
            c.Count = 2;
            c.V0 = SegA;
            c.V1 = SegB;
        }
        return c;
    }
}

/// <summary>Points de contact (1 ou 2) entre deux formes, en monde. La normale est fournie par <see cref="ShapeCollision.Test(in WorldShape, in WorldShape, out Vector2, out float, out ContactManifold)"/>.</summary>
public struct ContactManifold
{
    public int Count;
    public Vector2 P0, P1;

    public Vector2 this[int i] => i == 0 ? P0 : P1;
}

/// <summary>Noyau convexe d'une forme : 1 point, 1 segment ou 1 quadrilatère (jusqu'à 4 sommets).</summary>
internal struct Core
{
    public int Count;
    public Vector2 V0, V1, V2, V3;

    public Vector2 this[int i] => i switch { 0 => V0, 1 => V1, 2 => V2, _ => V3 };
}

/// <summary>
/// Collision SAT entre formes convexes "noyau + rayon" (OBB, cercle, capsule).
/// Axes testés : normales d'arêtes des deux noyaux, plus (si une forme est arrondie)
/// l'axe reliant chaque sommet au point le plus proche de l'autre noyau.
/// </summary>
public static class ShapeCollision
{
    const float Eps = 1e-6f;

    /// <param name="normal">Direction (unitaire) dans laquelle repousser A hors de B.</param>
    /// <param name="penetration">Distance de repoussage minimale le long de <paramref name="normal"/>, en mètres.</param>
    public static bool Test(in WorldShape a, in WorldShape b, out Vector2 normal, out float penetration)
    {
        Core ca = a.GetCore();
        Core cb = b.GetCore();
        float ra = a.Radius, rb = b.Radius;

        float best = float.MaxValue;
        Vector2 bestN = Vector2.Zero;
        normal = Vector2.Zero;
        penetration = 0f;

        // 1. Normales d'arêtes
        if (!EdgeAxes(a, in ca, ra, in cb, rb, ref best, ref bestN)) return false;
        if (!EdgeAxes(b, in cb, rb, in ca, ra, ref best, ref bestN, flip: true)) return false;

        // 2. Axes sommet -> point le plus proche (nécessaires dès qu'une forme est arrondie)
        if (ra > 0f || rb > 0f)
        {
            for (int i = 0; i < ca.Count; i++)
            {
                Vector2 p = ca[i];
                Vector2 d = ClosestOnCore(in cb, p) - p;
                if (!TryAxis(d, in ca, ra, in cb, rb, ref best, ref bestN)) return false;
            }
            for (int i = 0; i < cb.Count; i++)
            {
                Vector2 p = cb[i];
                Vector2 d = ClosestOnCore(in ca, p) - p;
                if (!TryAxis(d, in ca, ra, in cb, rb, ref best, ref bestN)) return false;
            }
        }

        if (best == float.MaxValue)
        {
            // Aucun axe exploitable (deux cercles de même centre) : repoussage arbitraire vers le haut
            Vector2 dc = a.Center - b.Center;
            bestN = dc.LengthSquared() > Eps ? dc.Normalize() : Vector2.Up;
            best = ra + rb;
        }

        normal = bestN;
        penetration = best;
        return true;
    }

    // Normales d'arêtes d'une forme. 'flip' n'est utilisé que pour garder la même signature ;
    // le signe est géré par TryAxis, qui compare toujours A (premier noyau) à B (second).
    static bool EdgeAxes(in WorldShape s, in Core own, float ownR, in Core other, float otherR,
                         ref float best, ref Vector2 bestN, bool flip = false)
    {
        // Quand on traite les axes de B, 'own' = B et 'other' = A : on réordonne pour TryAxis.
        if (s.Type == ColliderShapeType.Box)
        {
            return flip
                ? TryAxis(s.AxisX, in other, otherR, in own, ownR, ref best, ref bestN)
                  && TryAxis(s.AxisY, in other, otherR, in own, ownR, ref best, ref bestN)
                : TryAxis(s.AxisX, in own, ownR, in other, otherR, ref best, ref bestN)
                  && TryAxis(s.AxisY, in own, ownR, in other, otherR, ref best, ref bestN);
        }

        if (own.Count == 2)
        {
            Vector2 d = own.V1 - own.V0;
            Vector2 n = new Vector2(-d.Y, d.X);
            return flip
                ? TryAxis(n, in other, otherR, in own, ownR, ref best, ref bestN)
                : TryAxis(n, in own, ownR, in other, otherR, ref best, ref bestN);
        }

        return true; // cercle : pas d'arête
    }

    // Teste un axe (normalisé ici). Renvoie false si l'axe sépare les formes.
    // Met à jour le meilleur axe (plus petit repoussage de A).
    static bool TryAxis(Vector2 axis, in Core ca, float ra, in Core cb, float rb,
                        ref float best, ref Vector2 bestN)
    {
        float len = axis.Length();
        if (len < Eps) return true;       // axe dégénéré : ignoré
        Vector2 n = axis / len;

        Project(in ca, n, out float aMin, out float aMax);
        Project(in cb, n, out float bMin, out float bMax);
        aMin -= ra; aMax += ra;
        bMin -= rb; bMax += rb;

        float pushPos = bMax - aMin;      // déplacer A vers +n
        float pushNeg = aMax - bMin;      // déplacer A vers -n
        if (pushPos <= 0f || pushNeg <= 0f) return false;   // séparés

        if (pushPos < pushNeg)
        {
            if (pushPos < best) { best = pushPos; bestN = n; }
        }
        else
        {
            if (pushNeg < best) { best = pushNeg; bestN = -n; }
        }
        return true;
    }

    static void Project(in Core c, Vector2 n, out float min, out float max)
    {
        min = max = Vector2.Dot(c.V0, n);
        for (int i = 1; i < c.Count; i++)
        {
            float d = Vector2.Dot(c[i], n);
            if (d < min) min = d;
            if (d > max) max = d;
        }
    }

    static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float denom = Vector2.Dot(ab, ab);
        if (denom < Eps) return a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / denom, 0f, 1f);
        return a + ab * t;
    }

    static Vector2 ClosestOnCore(in Core c, Vector2 p)
    {
        if (c.Count == 1) return c.V0;
        if (c.Count == 2) return ClosestOnSegment(c.V0, c.V1, p);

        // Quadrilatère convexe anti-horaire : à l'intérieur, le point le plus proche est p lui-même
        bool inside = true;
        for (int i = 0; i < 4; i++)
        {
            Vector2 e = c[(i + 1) & 3] - c[i];
            if (Vector2.Cross(e, p - c[i]) < 0f) { inside = false; break; }
        }
        if (inside) return p;

        Vector2 bestPt = c.V0;
        float bestD = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 q = ClosestOnSegment(c[i], c[(i + 1) & 3], p);
            float d = Vector2.DistanceSquared(p, q);
            if (d < bestD) { bestD = d; bestPt = q; }
        }
        return bestPt;
    }

    // ------------------------------------------------------------------
    // Points de contact
    // ------------------------------------------------------------------

    /// <summary>
    /// Même test que <see cref="Test(in WorldShape, in WorldShape, out Vector2, out float)"/>, avec en plus
    /// les points de contact (clipping face de référence / arête incidente, comme Box2D).
    /// </summary>
    public static bool Test(in WorldShape a, in WorldShape b, out Vector2 normal, out float penetration,
                            out ContactManifold manifold)
    {
        manifold = default;
        if (!Test(in a, in b, out normal, out penetration)) return false;
        manifold = BuildManifold(in a, in b, normal);
        return true;
    }

    // Faces d'une Box : 0 = +X, 1 = -X, 2 = +Y, 3 = -Y
    static void FaceFrame(in WorldShape s, int face, out Vector2 nr, out Vector2 t, out float halfT, out Vector2 fc)
    {
        switch (face)
        {
            case 0: nr = s.AxisX; t = s.AxisY; halfT = s.HalfExtents.Y; fc = s.Center + nr * s.HalfExtents.X; break;
            case 1: nr = -s.AxisX; t = s.AxisY; halfT = s.HalfExtents.Y; fc = s.Center + nr * s.HalfExtents.X; break;
            case 2: nr = s.AxisY; t = s.AxisX; halfT = s.HalfExtents.X; fc = s.Center + nr * s.HalfExtents.Y; break;
            default: nr = -s.AxisY; t = s.AxisX; halfT = s.HalfExtents.X; fc = s.Center + nr * s.HalfExtents.Y; break;
        }
    }

    static Vector2 FaceNormal(in WorldShape s, int face) => face switch
    {
        0 => s.AxisX,
        1 => -s.AxisX,
        2 => s.AxisY,
        _ => -s.AxisY
    };

    // Face de la Box la plus alignée avec 'dir' ; renvoie le cosinus de l'angle (1 = parfaitement parallèle)
    static float BestFace(in WorldShape s, Vector2 dir, out int face)
    {
        float dx = Vector2.Dot(s.AxisX, dir);
        float dy = Vector2.Dot(s.AxisY, dir);
        if (MathF.Abs(dx) >= MathF.Abs(dy)) { face = dx >= 0f ? 0 : 1; return MathF.Abs(dx); }
        face = dy >= 0f ? 2 : 3;
        return MathF.Abs(dy);
    }

    // Paire de points la plus proche entre deux noyaux convexes (point, segment ou quadrilatère).
    // Pour des noyaux disjoints, l'une des extrémités de la paire est toujours un sommet.
    static void ClosestCoreFeatures(in Core ca, in Core cb, out Vector2 pa, out Vector2 pb)
    {
        pa = ca.V0;
        pb = cb.V0;
        float best = float.MaxValue;

        for (int i = 0; i < ca.Count; i++)
        {
            Vector2 v = ca[i];
            Vector2 q = ClosestOnCore(in cb, v);
            float d = Vector2.DistanceSquared(v, q);
            if (d < best) { best = d; pa = v; pb = q; }
        }
        for (int i = 0; i < cb.Count; i++)
        {
            Vector2 w = cb[i];
            Vector2 q = ClosestOnCore(in ca, w);
            float d = Vector2.DistanceSquared(w, q);
            if (d < best) { best = d; pa = q; pb = w; }
        }
    }

    // normal : direction de repoussage de A (de B vers A)
    static ContactManifold BuildManifold(in WorldShape a, in WorldShape b, Vector2 n)
    {
        const float align = 0.9995f;   // ~1.8° : la normale est celle d'une face

        float alignA = -2f, alignB = -2f;
        int faceA = 0, faceB = 0;
        if (a.Type == ColliderShapeType.Box) alignA = BestFace(in a, -n, out faceA);
        if (b.Type == ColliderShapeType.Box) alignB = BestFace(in b, n, out faceB);

        if (alignA >= align && alignA >= alignB)
        {
            if (ClipIncident(in a, faceA, in b, out var m)) return m;
        }
        else if (alignB >= align)
        {
            if (ClipIncident(in b, faceB, in a, out var m)) return m;
        }

        // Contact ponctuel (cercle, coin de box, normale issue d'un sommet...) :
        // paire de points la plus proche entre les noyaux, ramenée sur les surfaces
        Core ca = a.GetCore(), cb = b.GetCore();
        ClosestCoreFeatures(in ca, in cb, out Vector2 qa, out Vector2 qb);
        Vector2 pA = qa - n * a.Radius;
        Vector2 pB = qb + n * b.Radius;
        return new ContactManifold { Count = 1, P0 = (pA + pB) * 0.5f };
    }

    // Coupe l'arête incidente de 'inc' contre les plans latéraux de la face de référence de 'refShape'
    static bool ClipIncident(in WorldShape refShape, int refFace, in WorldShape inc, out ContactManifold m)
    {
        m = default;
        FaceFrame(in refShape, refFace, out Vector2 nr, out Vector2 t, out float halfT, out Vector2 fc);

        Vector2 p0, p1;
        float rI;
        if (inc.Type == ColliderShapeType.Box)
        {
            // arête de 'inc' la plus anti-parallèle à la face de référence
            int bestF = 0;
            float bestDot = float.MaxValue;
            for (int f = 0; f < 4; f++)
            {
                float d = Vector2.Dot(FaceNormal(in inc, f), nr);
                if (d < bestDot) { bestDot = d; bestF = f; }
            }
            FaceFrame(in inc, bestF, out _, out Vector2 ti, out float hi, out Vector2 fci);
            p0 = fci - ti * hi;
            p1 = fci + ti * hi;
            rI = 0f;
        }
        else
        {
            p0 = inc.SegA;
            p1 = inc.SegB;
            rI = inc.Radius;
        }

        // Clipping 1D le long de la face
        float s0 = Vector2.Dot(p0 - fc, t);
        float s1 = Vector2.Dot(p1 - fc, t);
        float ta = 0f, tb = 1f;
        float ds = s1 - s0;
        if (MathF.Abs(ds) < 1e-7f)
        {
            if (MathF.Abs(s0) > halfT) return false;
        }
        else
        {
            float u0 = (-halfT - s0) / ds;
            float u1 = (halfT - s0) / ds;
            if (u0 > u1) (u0, u1) = (u1, u0);
            ta = MathF.Max(0f, u0);
            tb = MathF.Min(1f, u1);
            if (ta > tb) return false;
        }

        Vector2 d01 = p1 - p0;
        Vector2 q0 = p0 + d01 * ta;
        Vector2 q1 = p0 + d01 * tb;

        const float margin = 0.002f;
        int count = 0;
        Vector2 c0 = default, c1 = default;

        Vector2 q = q0;
        for (int k = 0; k < 2; k++, q = q1)
        {
            if (k == 1 && Vector2.DistanceSquared(q0, q1) < 1e-10f) break;

            float sep = Vector2.Dot(q - fc, nr) - rI;     // < 0 : pénètre la face de référence
            if (sep > margin) continue;

            Vector2 surf = q - nr * rI;                   // point de la surface incidente tourné vers la face
            Vector2 pt = surf - nr * (sep * 0.5f);        // milieu entre surface incidente et face de référence
            if (count == 0) c0 = pt; else c1 = pt;
            count++;
        }

        if (count == 0) return false;
        m = new ContactManifold { Count = count, P0 = c0, P1 = c1 };
        return true;
    }
}

/// <summary>Moment d'inertie (autour du centre de la forme) d'un solide homogène de masse donnée.</summary>
public static class ShapeInertia
{
    public static float Compute(in WorldShape s, float mass)
    {
        switch (s.Type)
        {
            case ColliderShapeType.Box:
                {
                    float w = s.HalfExtents.X * 2f, h = s.HalfExtents.Y * 2f;
                    return mass * (w * w + h * h) / 12f;
                }
            case ColliderShapeType.Circle:
                return 0.5f * mass * s.Radius * s.Radius;
            default:
                {
                    float r = s.Radius;
                    float L = Vector2.Distance(s.SegA, s.SegB);
                    float areaRect = L * 2f * r;
                    float areaDisk = MathF.PI * r * r;
                    float mRect = mass * areaRect / (areaRect + areaDisk);
                    float mDisk = mass - mRect;
                    float iRect = mRect * (L * L + 4f * r * r) / 12f;
                    float d = 4f * r / (3f * MathF.PI);
                    float iDisk = mDisk * (0.5f * r * r + L * L * 0.25f + L * d);
                    return iRect + iDisk;
                }
        }
    }
}