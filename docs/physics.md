# KoreEngine — Physique, unités et collisions

> Mis à jour le 26/10/2026. Couvre le passage aux **mètres**, les formes de collider (boîte, cercle, capsule), la **rotation physique** (corps rigides) et le pipeline de collision hybride (Swept AABB / SAT). Pour le reste du moteur, voir `architecture.md` et `components-reference.md`.

## Unités et conventions

| Grandeur | Unité | Remarque |
| --- | --- | --- |
| Positions, tailles de collider, `SpriteRenderer.Size` | **mètres** | `Camera.Position` aussi. |
| `FrameSize` (taille d'une frame sur la texture), `Camera.ViewWidth/Height`, UI | **pixels** | |
| Vitesses (`PhysicsBody.Velocity`, `MaxFallSpeed`) | **m/s** | |
| Gravité (`Physics.gravity`) | **−9,81 m/s²** | Y vers le haut. |
| Rotation (`Transform.Rotation`, `WorldRotation`) | **degrés**, **sens anti-horaire positif** | Comme Unity. |
| `PhysicsBody.AngularVelocity`, `AngularDrag` | **°/s**, **1/s** | En interne le solveur travaille en rad/s. |

- **Conversion mètres → pixels** : `Renderer.PixelsPerUnit` (statique, **100** par défaut) × `Camera.Zoom`. À `Zoom = 1`, 1 m = 100 px.
- **Axes** : le monde est **Y vers le haut** ; l'écran est Y vers le bas. La conversion se fait uniquement dans `Camera.WorldToScreen` / `ScreenToWorld`.
- **Rotation et rendu** : SDL tourne dans le sens horaire à l'écran. `SpriteRenderer` envoie donc **`-WorldRotation`** à `Renderer`, pour que le sprite tourne dans le même sens que la physique et le collider.
- **`Rectangle`** est un struct de `float` (mètres). `Rectangle.Y` est le bord **bas** (valeur minimale de Y) ; `Bottom = Y + Height` est donc le bord **haut** en espace monde (les noms viennent de l'ancienne convention Y vers le bas).

## `Collider`

| Membre | Rôle |
| --- | --- |
| `ColliderShapeType Shape` | `Box` (défaut), `Circle` ou `Capsule`. |
| `float Width, Height` | Dimensions locales en **mètres**, avant `WorldScale`. Défaut `1 × 1`. |
| `float OffsetX, OffsetY` | Décalage en mètres dans le repère de l'objet : **il suit la rotation** du `Transform`. |
| `string Tag`, `bool IsTrigger` | Un trigger détecte sans résoudre. |
| `WorldShape GetWorldShape()` | Forme réelle dans le monde (position, rotation, échelle appliquées). |
| `Rectangle Bounds` | **AABB englobant** la forme (exact pour une boîte non tournée). Sert à la phase large et à la sélection dans l'éditeur. |
| `float ComputeInertia(float mass)` | Moment d'inertie autour du centre du collider. |

Définition des formes à partir de `Width × Height` (après échelle) :

| Forme | Définition |
| --- | --- |
| `Box` | Rectangle `Width × Height`. Orienté (OBB) quand l'objet est tourné. |
| `Circle` | Diamètre = `min(Width, Height)`. |
| `Capsule` | Boîte englobante `Width × Height`. **Verticale** si `Height ≥ Width` (rayon = `Width / 2`), sinon **horizontale** (rayon = `Height / 2`). |

L'échelle non uniforme avec rotation n'est pas supportée (la forme ne se déforme pas en parallélogramme).

### Callbacks

`OnCollision(Collider other)` et `OnTriggerEnter(Collider other)` sont **virtuels** sur `Component`. `Collider` les renvoie vers les **autres composants du même `GameObject`** (un script peut donc les surcharger). Ils sont appelés **à chaque frame** où le chevauchement persiste, et ne transmettent ni normale ni point de contact.

## `PhysicsBody`

| Membre | Rôle |
| --- | --- |
| `Vector2 Velocity` | m/s. |
| `bool IsStatic` | N'intègre ni vitesse ni gravité ; masse infinie dans le solveur. |
| `float GravityScale` | Défaut `1`. |
| `float Friction` | Défaut `0`. Amortissement exponentiel de `Velocity.X` (pas une friction de contact). **À laisser à `0` sur un corps qui roule**, sinon il freine le roulement. |
| `float SurfaceFriction` | Défaut `0,4`. Coefficient de Coulomb de la surface. Au contact, les deux coefficients sont combinés : `μ = √(μA × μB)`. |
| `float Mass` | Défaut `1`. |
| `float Restitution` | Défaut `0` (pas de rebond), `1` = rebond parfait. Au contact : maximum des deux corps. |
| `float MaxFallSpeed` | Vitesse de chute maximale, en m/s. |
| `bool FreezeRotation` | Défaut **`true`** : aucune rotation physique (comportement historique). Décocher pour un corps rigide complet. |
| `float AngularVelocity` | °/s, positif = anti-horaire. |
| `float AngularDrag` | Défaut `0,05`. `AngularVelocity /= 1 + AngularDrag × dt`. |
| `ApplyForce(Vector2)` (÷ `Mass`) / `ApplyImpulse(Vector2)` | |

`Update(dt)` : `Physics.CalculatePhysics` (gravité, friction), clamp de chute, intégration `transform.Position += Velocity * dt`, puis, si `FreezeRotation` est faux, amortissement angulaire et rotation **autour du centre de masse** (le centre du collider ; la position du `Transform` est ajustée si l'offset n'est pas nul).

> `IsGrounded` n'existe plus. **Il n'y a pour l'instant aucune API de détection du sol** (les callbacks ne fournissent pas la normale de contact).

## Pipeline de collision

Chaque frame, `Scene.Update` exécute : composants hors `PhysicsBody` → `PhysicsBody` → `CollisionSystem.Update`. Paires O(n²). **Les deux colliders d'une paire doivent avoir un `PhysicsBody`** ; deux statiques sont ignorés.

Pour chaque paire, le chemin est choisi ainsi :

| Condition | Chemin |
| --- | --- |
| Deux **boîtes alignées sur les axes** (rotation multiple de 90°), et aucun corps dynamique avec `FreezeRotation = false` | **Swept AABB** (continu, inchangé). |
| Sinon : une forme tournée, un cercle, une capsule, ou un corps dynamique qui peut tourner | **SAT** (discret), avec points de contact et impulsions. |

### Chemin SAT

1. **Détection** (`ShapeCollision.Test`) : chaque forme est un noyau convexe (point, segment ou rectangle) épaissi d'un rayon. Les axes testés sont les normales d'arêtes des deux noyaux et, si une forme est arrondie, les axes sommet → point le plus proche. Résultat : normale de repoussage de A (de B vers A) et profondeur minimale.
2. **Points de contact** (`ContactManifold`, 1 ou 2 points) : clipping de l'arête incidente contre la face de référence quand une boîte a une face parallèle à la normale ; sinon un seul point, entre les points les plus proches des deux noyaux.
3. **Correction de position** : le corps dynamique est repoussé de la profondeur (statique + dynamique), ou répartition selon la masse (deux dynamiques).
4. **Impulsions** (`ContactSolver`, via `RigidBodyBridge`) : impulsions séquentielles avec accumulation, **10 itérations**, sans warm starting. Pour chaque point : impulsion normale (avec restitution si la vitesse d'impact dépasse 1 m/s), puis impulsion de friction bornée par `μ × impulsion normale`. Elles modifient `Velocity` et `AngularVelocity`.
5. Les triggers sont détectés par le même test mais ne résolvent rien.

### Inertie

Solide homogène, autour du centre du collider : boîte `m (w² + h²) / 12` ; cercle `m r² / 2` ; capsule : rectangle central plus deux demi-disques (formule exacte). Un corps `FreezeRotation` ou statique a une inertie infinie.

## Limites connues

- **Pas d'anti-tunneling sur le chemin SAT** : il est discret. Un objet qui avance de plus que l'épaisseur d'un collider fin en une frame (par exemple une chute à 50 m/s en une frame à 60 FPS = 0,8 m) peut le traverser.
- **Pénétrations profondes** : les points de contact ne sont fiables que pour de faibles pénétrations (de l'ordre de 10 cm pour des formes d'1 m). Au-delà, leur position peut être approximative.
- **Empilements** : pas de warm starting ; des piles de boîtes peuvent vibrer. Deux capsules parallèles en contact n'ont qu'un seul point.
- **Gravité × masse** : `CalculatePhysics` multiplie la gravité par `Mass`, donc un corps plus lourd tombe plus vite. Retirer ce facteur rend la chute indépendante de la masse (les impulsions de collision, elles, utilisent bien la masse).
- **Objets enfants** : `PhysicsBody` et `CollisionSystem` écrivent dans `transform.Position` (valeur locale) ; la rotation physique suppose elle aussi des objets racines.
- **Swept AABB** : ne produit ni point de contact ni rotation ; c'est pourquoi un corps qui doit pouvoir tourner passe toujours par le chemin SAT.