# KoreEngine — Référence des Components

> Mis à jour à partir du code source complet (`KoreEngine.rar`). Couvre désormais tous les composants du moteur.

## `Component` (classe de base, `KoreEngine.Core`)

| Membre | Rôle |
|---|---|
| `GameObject Owner` | `[HideInInspector]`. |
| `virtual void Update(float dt)` | Chaque frame de simulation (si Playing, non Paused, sauf Step). |
| `virtual void Render(Renderer, Camera?)` | Chaque frame de rendu, indépendamment de Playing. |
| `virtual void Start()` | Appelé une seule fois quand Playing passe à `true`. **Renommé depuis `OnStart()`** dans une itération récente — utiliser `Start()` dans tout nouveau composant. |
| `virtual void OnDestroy()` | Retrait du composant ou déchargement de scène. Libérer les ressources natives ici. |
| `virtual IEnumerable<InspectorField> GetInspectorFields()` | Vide par défaut → auto-draw par réflexion. |
| `virtual IEnumerable<string> Serialize()` / `virtual void Deserialize(List<string>)` | Sérialisation personnalisée (listes, types complexes). |

Attributs : `[NonSerialized]` (exclu sérialisation + Inspector), `[HideInInspector]` (exclu Inspector seulement).

## `GameObject` (`KoreEngine.Core`, `partial class`)

| Membre | Rôle |
|---|---|
| `string Name` | |
| `Vector2 LocalPosition` / `WorldPosition` (get) | Additive le long de la hiérarchie. |
| `Vector2 PreviousPosition` | Champ présent, usage non exploité dans le code actuel [à clarifier]. |
| `float LocalRotation` / `WorldRotation` (get) | Degrés, additive. |
| `Vector2 LocalScale` (défaut `(1,1)`) / `WorldScale` (get) | Multiplicative. |
| `Vector2 Position` | Alias → `LocalPosition`. Encore largement utilisé (`PhysicsBody`, `CollisionSystem`) — voir limite dans `architecture.md`. |
| `GameObject? Parent`, `IReadOnlyList<GameObject> Children` | |
| `void SetParent(GameObject?, Scene?)` | Préserve la position monde. Suppose parent sans rotation/scale. |
| `bool IsDescendantOf(GameObject)` | |
| `List<Component> Components`, `Scene? Scene` | |
| `AddComponent<T>` / `RemoveComponent<T>`/`RemoveComponent(Component)` (appelle `OnDestroy()`) / `GetComponent<T>()`/`GetComponent(Type)` | |
| `virtual Update(float dt)` / `Render(...)` | Récursif sur les enfants. |

## `Scene` (`KoreEngine.Core`)

| Membre | Rôle |
|---|---|
| `List<GameObject> RootObjects`, `IEnumerable<GameObject> AllObjects` (aplati) | |
| `CollisionSystem Collisions`, `Camera? Camera` | |
| `void FindCamera()` | Premier `Camera` trouvé dans `AllObjects`. |
| `void Add(GameObject)` / `Remove(GameObject)` | `Remove` appelle `DestroyRecursive` puis détache + désenregistre colliders. |
| `void Start()` | Appelle `Start()` sur tous les composants (renommé depuis `OnStart()`). |
| `void Update(float dt)` | 3 passes ordonnées : composants hors `PhysicsBody` → `PhysicsBody` → `Collisions.Update`. |
| `void Render(Renderer)`, `void RefreshColliders()`, `void DestroyAll()` | |

## `Camera` (`KoreEngine.Components`)

| Membre | Rôle |
|---|---|
| `float Zoom`, `int ViewWidth, ViewHeight` | |
| `virtual Vector2 Position` | Délègue à `Owner.WorldPosition`/`LocalPosition` si attachée, sinon `_position` (cas `EditorCamera`). |
| `WorldToScreen(Vector2)` / `ScreenToWorld(Vector2)` | Conversion Y-haut (monde) ↔ Y-bas (écran), centrée sur `Position`, échelle `Zoom`. |
| `Rectangle Bounds` | Rectangle de vue centré sur `Position`. |
| `GetInspectorFields()` | Position/ViewW/H en lecture seule, Zoom éditable. |

### `EditorCamera` (`KoreEngine.Editor`, hérite de `Camera`)
Jamais attachée à un `GameObject`. `Reset()` → position `(0,0)`, zoom `1`.

## `RectRenderer` (`KoreEngine.Components`)

`Vector2 Size`, `Color Color` (défaut blanc). `Render()` : rectangle plein centré sur `WorldPosition`, mis à l'échelle par `WorldScale`. Pas de rotation supportée (limite SDL `RenderFillRect`).

## `SpriteRenderer` (`KoreEngine.Components`)

| Membre | Rôle |
|---|---|
| `AnimationStateMachine? StateMachine` | Voir section Animation plus bas. |
| `IntPtr Texture`, `string TexturePath` | Via `TextureCache`, éditable (`TextureField`). |
| `Vector2 Size` (taille finale avant `WorldScale`), `Vector2 FrameSize` (taille d'une frame spritesheet) | |
| `bool FlipX, FlipY` | |
| `FilteringMode Filtering` (Nearest/PixelArt/Linear) | Appliqué immédiatement au changement. |
| `Render(...)` | Centré sur `WorldPosition`, scale + rotation (`WorldRotation`) appliqués. Log `Warning` si `StateMachine` null. |

## `Collider` (`KoreEngine.Components`)

`int Width, Height`, `int OffsetX, OffsetY`, `string Tag`, `bool IsTrigger`. Callbacks `Action<Collider, CollisionInfo>? OnCollision`, `OnTriggerEnter`. `Rectangle Bounds` : AABB centrée sur `WorldPosition + Offset`, dimensions × `WorldScale`. Ne suit pas la rotation (AABB, pas OBB).

## `PhysicsBody` (`KoreEngine.Components`)

| Membre | Rôle |
|---|---|
| `Vector2 Velocity` | |
| `bool IsStatic` | Corps statique : n'intègre ni vélocité ni gravité (mur/sol fixe). |
| `float GravityScale` | Multiplicateur de `GlobalGravity` (`static float`, `800f` par défaut, partagé). |
| `float Friction` | Amortissement exponentiel de `Velocity.X` (`Pow(1-Friction, dt)`). |
| `float Mass` | Répartition d'impulsion entre corps dynamiques en collision. |
| `float MaxFallSpeed` | Clamp `Velocity.Y` si `GravityScale > 0`. |
| `bool IsGrounded` | Reset à `false` chaque frame, rétabli par `CollisionSystem`. |
| `ApplyForce(Vector2)` / `ApplyImpulse(Vector2)` | |

`Update(dt)` : reset vélocité Y si au sol, reset `IsGrounded`, intègre gravité (clampée), applique friction, déplace `Owner.Position` (⚠ alias `LocalPosition`, pas `WorldPosition` — voir limite dans `architecture.md` pour un corps enfant d'un autre objet).

## Physique — `CollisionSystem` / `CollisionInfo` (`KoreEngine.Physics`)

- **`CollisionInfo`** (struct) : `Collider Other`, `Vector2 Normal`, `float Penetration` (toujours `0` dans le code actuel, jamais calculé).
- **`CollisionSystem`** : détection par paires O(n²), résolution par **Swept AABB** (calcule le temps d'impact le long du mouvement relatif, évite le tunneling à vitesse élevée). Gère : deux statiques (ignorés), statique+dynamique (le dynamique est repoussé au point de contact), deux dynamiques (répartition proportionnelle à la masse). `CheckGrounded` détecte le sol via une marge verticale entre les `Bounds`. Les triggers déclenchent `OnTriggerEnter` sans résolution.

## Animation (`Animation`, `AnimationFrame`, `AnimationStateMachine`)

- **`AnimationFrame`** (struct) : `Rectangle SourceRect`, `float Duration`.
- **`Animation`** : `Name`, `AnimationFrame[] Frames`, `Loop`. `Animation.FromStrip(name, frameW, frameH, row, startCol, frameCount, frameDuration, loop)` génère les frames d'une bande spritesheet uniforme.
- **`AnimationStateMachine`** : dictionnaire nom→`Animation` + liste de transitions (`From`, `To`, `Func<bool> Condition`). `Update(dt)` teste les transitions de l'état courant (première vraie l'emporte), avance le timer de frame, boucle ou marque `IsFinished`.

## `Animator` (`KoreEngine.Components`)

| Type | Rôle |
|---|---|
| `AnimationDefinition` | `Name`, `SizeX/Y`, `Row`, `StartCol`, `FrameCount`, `FrameDuration`, `Looping`. |
| `AnimationCondition` (enum) | `Always`, `IsGrounded`, `IsNotGrounded`, `VelocityXAbsGreaterThan/LessThan`, `VelocityYGreaterThan/LessThan`. |
| `AnimationTransition` | `From`, `To`, `Condition`, `Threshold`. |

`Definitions`/`Transitions` éditées via `ListField`. `Apply()` reconstruit la state machine (appelé dans `Start()` et via bouton "Apply" dans l'Inspector). `Serialize()`/`Deserialize()` personnalisés (`Def: ...`, `Trans: ...`). Cherche `PhysicsBody`/`SpriteRenderer` sur le même objet dans `Start()`.

## `AudioSource` (`KoreEngine.Components`)

> **Nom de classe réel : `AudioSource`**, pas `AudioSourceComponent` comme désigné précédemment dans les échanges — le fichier et la classe s'appellent `AudioSource.cs`/`AudioSource`.

`string ClipPath` (picker `AudioClipField`), `float Volume` (0–1), `float Pitch` (0.1–3), `bool Loop`, `bool Spatial2D` (position du track synchronisée sur `WorldPosition` chaque frame — listener SDL_mixer fixe à (0,0,0), voir `architecture.md`), `bool PlayOnStart`. `Play()`/`Stop()`/`Pause()`/`Resume()` (exposées aussi en `ActionField`). `Start()` (pas `OnStart()`) déclenche `Play()` si `PlayOnStart`. `OnDestroy()` libère `track`/`audio`.

## UI (`KoreEngine.Components.UI`)

- **`UIElement`** (abstrait) : `X, Y, Width, Height` (coordonnées de référence), `Visible`, `UICanvas? Canvas`. `ScreenX/Y/Width/Height` = valeurs × `Canvas.ScaleX/Y`. `Render()` délègue à `Draw(Renderer)` (abstrait) si `Visible`.
- **`UICanvas`** : `ReferenceWidth/Height` (défaut 1920×1080), `ScaleX/Y` calculés dynamiquement depuis `SceneManager.ViewportWidth/Height`. `Add<T>(element)` crée un `GameObject` enfant et y attache l'élément — **le GameObject du Canvas doit déjà être dans la scène** (exception explicite sinon).
- **`UIButton`** : `TextureNormal/Hover/Pressed` (+ chemins), `ActionType` (`None`/`LoadScene`/`Quit`, sérialisable) + `ActionParam`, `OnClick` (délégué, `[HideInInspector]`, non sérialisable). Hover/pressed testés directement via `SceneManager.ViewportMouseX/Y/InBounds` + `SDL.GetMouseState` (pas via `InputManager`).
- **`UIImage`** : `Texture`/`TexturePath`, dessine sans état d'interaction.

## Input (`KoreEngine.Input`, `KoreEngine.Engine.InputManager`)

- **`InputManager`** (statique, namespace `KoreEngine`) : `IsKeyDown` (maintenue), `IsKeyPressed` (une frame, vidé par `NewFrame()`), `IsAnyKeyDown`/`IsAnyKeyPressed`.
- **`InputAction`** : liaison nommée, `BindKey` (→`IsDown`), `BindKeyPressed` (→`IsPressed`), plusieurs touches par action.
- **`InputAxis`** : 1D (`BindKeys(neg, pos)`) ou 2D (`BindKeys(up, down, left, right)`), retourne `Vector2` normalisé ou non.
- **`InputMap`** : registre nommé d'actions/axes, façade `IsDown(name)`/`IsPressed(name)`/`GetAxis(name)`.

Coexiste avec l'accès direct à `InputManager` déjà utilisé ailleurs (`UIButton` interroge SDL directement pour la souris) — commodité optionnelle, pas une couche obligatoire.

## Types de champs Inspector (`InspectorField` et dérivés, `KoreEngine.Core`)

| Type | Usage |
|---|---|
| `TextField` | Lecture seule (`Func<string> Get`). |
| `FloatField` / `IntField` | `Get`/`Set`, `Speed`, `Min`/`Max` (si égaux, pas de clamp). |
| `BoolField` | Checkbox. |
| `StringField` | `InputText`, `MaxLength`. |
| `EnumField` | Combo, `Names` + `Get`/`Set` par index. |
| `TextureField` | Picker miniatures, scanne `Assets/`. |
| `AudioClipField` | Picker `.wav/.ogg/.mp3` sous `Assets/`. |
| `ComponentRefField` | Référence `Component`, picker filtré par type. |
| `ActionField` | Bouton d'action, `Tooltip` optionnel. |
| `ListField` | Liste éditable (ajout/suppression/champs par item), `ItemContextActions` en plus de "Remove". Utilisé par `Animator`. |

## Types de base (`KoreEngine.Core`)

- **`Vector2`** (struct) : `+`, `-` (unaire/binaire), `*`/`/` scalaire, `Zero`/`One`/`NegativeOne`/`Up` (=`(0,-1)`)/`Down` (=`(0,1)`), `Length()`, `Normalize()`, `Lerp`/`LerpUnclamped`.
- **`Rectangle`** (struct) : `X, Y, Width, Height`, `Left/Right/Top/Bottom` calculés, `Intersects(other)`.
- **`Color`** (classe) : `R, G, B` (int), constantes `Black/White/Red/Green/Blue`. Pas de canal alpha stocké (passé séparément en paramètre `byte a` par `Renderer`).
