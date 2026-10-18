# KoreEngine — Référence des Components

> Mis à jour à partir du code source complet le plus récent (archive du 18/10/2026). Couvre tous les composants du moteur et les APIs Runtime les plus utilisées depuis un script. Tous les types sont dans le namespace `KoreEngine`, sauf les composants UI (`KoreEngine.UI`).
>
> **Changements majeurs depuis la version du 09/09/2026** : `Transform` est maintenant un composant (plus de `LocalPosition`/`WorldPosition` sur `GameObject`), `Owner` est devenu `gameObject`, `RectRenderer` n'existe plus, `InputManager`/`InputAction`/`InputAxis`/`InputMap` ont été remplacés par `Input` + `KeyCode`, `UIButton.OnClick` est un `KoreEvent`, et la physique a une restitution et une gravité `-9.81`.

## `Component` (classe de base)

| Membre | Rôle |
| --- | --- |
| `GameObject gameObject` | Objet propriétaire (setter interne). |
| `Transform transform` | Raccourci vers `gameObject.transform`. |
| `virtual void Update(float dt)` | Chaque frame de simulation (si Playing, non Paused, sauf Step). |
| `virtual void Render(Renderer, Camera?)` | Chaque frame de rendu, indépendamment de Playing. |
| `virtual void Start()` | Une seule fois quand Playing passe à `true`. |
| `virtual void OnDestroy()` | Retrait du composant ou déchargement de scène. Libérer les ressources natives ici ; **peut être appelé plusieurs fois**, à écrire de façon idempotente. |
| `virtual IEnumerable<InspectorField> GetInspectorFields()` | Vide par défaut → auto-draw par réflexion. |
| `virtual IEnumerable<string> Serialize()` / `virtual void Deserialize(List<string>)` | Sérialisation personnalisée (listes, types complexes). |

### Attributs (`Core/Attributes/`)

| Attribut | Effet |
| --- | --- |
| `[NonSerialized]` | Exclu de la sérialisation **et** de l'auto-draw de l'Inspector. |
| `[HideInInspector]` | Exclu de l'Inspector seulement (auto-draw). |
| `[Range(min, max)]` | Slider borné dans l'Inspector pour `float`, `int`, `byte` (auto-draw uniquement ; n'impose pas les bornes à l'exécution). |
| `[UnremovableComponent]` | Le menu *Remove Component* est grisé (`Transform`). |

## `GameObject`

| Membre | Rôle |
| --- | --- |
| `string Name` | |
| `Transform transform` | Lève `InvalidOperationException` si l'objet n'a pas de `Transform`. |
| `List<Component> Components`, `Scene? Scene` | |
| `IReadOnlyList<GameObject> Children` | La liste `children` est publique ; le parent se lit via `transform.Parent`. |
| `GameObject(string? name = null)` | Crée l'objet **avec** un `Transform`. |
| `GameObject()` | Constructeur sans paramètre : **sans** `Transform` (utilisé par le chargement de scène, qui l'ajoute depuis le fichier). |
| `AddComponent<T>(T)` / `AddComponent<T>()` | N'ajoute jamais un second `Transform` (renvoie l'existant). |
| `RemoveComponent(Component)` | Appelle `OnDestroy()`. |
| `GetComponent<T>()` / `GetComponent(Type)` | Premier composant assignable. |
| `Update(dt)` / `Render(...)` | Récursifs sur les enfants. |
| `Instantiate(Scene, Vector2? position = null)` | Clone l'objet et ses enfants (voir plus bas). |
| `Destroy()` | `OnDestroy` des composants, destruction des enfants, retrait de la scène. |

## `Transform` (composant, `[UnremovableComponent]`)

| Membre | Rôle |
| --- | --- |
| `Vector2 Position` | **Locale** (relative au parent). |
| `float Rotation` | Locale, en degrés. |
| `Vector2 Scale` | Locale, défaut `(1, 1)`. |
| `Vector2 WorldPosition` / `float WorldRotation` / `Vector2 WorldScale` | En lecture ; composent l'échelle et la rotation du parent. |
| `GameObject? Parent` | Lecture seule. |
| `SetParent(GameObject?, Scene?)` | **Préserve** position/rotation/échelle monde. `scene` est nécessaire pour (dé)rattacher aux racines. |
| `IsDescendantOf(GameObject)`, `Translate(Vector2)`, `Rotate(float)` | |

Inspector : X, Y, Rotation, Scale X, Scale Y (champs locaux).

## `Scene`

| Membre | Rôle |
| --- | --- |
| `string Name`, `List<GameObject> RootObjects`, `IEnumerable<GameObject> AllObjects` | `AllObjects` est aplati. |
| `CollisionSystem Collisions` | |
| `Camera Camera` | **Lève une exception si la scène n'a pas de caméra.** Le setter met aussi à jour `Camera.Main`. |
| `FindCamera()` | Première `Camera` trouvée ; sinon crée un objet « Main Camera ». |
| `Add(GameObject)` / `Remove(GameObject)` | `Remove` : `OnDestroy` récursif, détache, désenregistre les colliders. |
| `Start()`, `Update(dt)`, `Render(Renderer)` | `Update` : composants hors `PhysicsBody` → `PhysicsBody` → `Collisions.Update`. |
| `RefreshColliders()`, `RegisterColliders(GameObject)`, `DestroyAll()` | |

## `SceneManager` (statique)

`Current`, `CurrentSceneName`, `CurrentScenePath`, `AssetsDirectory`, `SaveLocked`, `ScanAndRegisterScenes()`, `SceneFiles()`, `LoadScene(name)`, `LoadSceneFromFile(path)` (sauvegarde la scène courante d'abord), `SaveCurrentScene()`, `ApplyPendingScene()`, `Update(dt)`, `Render(renderer)`, `NotifyStart()`, `UnloadScene()`, `NotifyPathRenamed(old, new)`, `OnSceneChanging`. État viewport partagé : `ViewportWidth/Height`, `ViewportMouseX/Y`, `ViewportMouseInBounds` (renseignés par le Game view dans l'éditeur, par `GameLoop` dans un jeu buildé).

## `Camera`

| Membre | Rôle |
| --- | --- |
| `float Zoom` | Défaut `1`. L'Inspector le borne à `[1, 10]` (`[Range(1, 10)]`) ; le code peut le dépasser. |
| `int ViewWidth, ViewHeight` | Taille de vue en pixels. Recalées à chaque frame sur le Game view (éditeur) ou la fenêtre (jeu). |
| `Color Color` | Couleur de fond (défaut noir). |
| `virtual Vector2 Position` | `gameObject.transform.WorldPosition` si attachée à un objet, sinon une position interne (`EditorCamera`). Le setter écrit `transform.Position`. |
| `static Camera Main` | Dernière caméra assignée via `Scene.Camera`. |
| `WorldToScreen(Vector2)` / `ScreenToWorld(Vector2)` | Conversion Y-haut (monde) ↔ Y-bas (écran), centrée sur `Position`, échelle `Zoom`. |
| `Rectangle Bounds` | Rectangle de vue (`ViewWidth/Zoom` × `ViewHeight/Zoom`). |

### `EditorCamera` (`KoreEngine.Editor`, hérite de `Camera`)

Jamais attachée à un `GameObject`. `Reset()` → position `(0, 0)`, zoom `1`.

## `SpriteRenderer`

| Membre | Rôle |
| --- | --- |
| `IntPtr Texture`, `string TexturePath` | Via `TextureCache` (picker `TextureField`). |
| `Vector2 Size` | Taille finale avant `WorldScale` (défaut `16×16`). |
| `Vector2 FrameSize` | Taille d'une frame de spritesheet (défaut `16×16`). |
| `bool FlipX, FlipY` | |
| `FilteringMode Filtering` | `Nearest` (défaut), `PixelArt`, `Linear`. |
| `AnimationStateMachine? StateMachine` | Alimentée par l'`Animator`. |

`Render` : ne dessine rien si `Texture` est nulle. Centré sur `WorldPosition`, `WorldScale` et `WorldRotation` appliqués. Avec une `StateMachine`, dessine via `DrawTexturePart` ; sans, loggue un avertissement et dessine la texture entière.

**⚠ Animation de spritesheet** : `Render` remplace le `SourceRect` de la frame courante par `(0, 0, FrameSize.X, FrameSize.Y)` avant de dessiner. Le `SourceRect` calculé par `Animation` / `Animator` (ligne, colonne) n'est donc **pas utilisé** : le sprite affiche toujours la première frame de la feuille. À corriger pour que les animations s'affichent réellement.

## `Collider`

`int Width, Height`, `int OffsetX, OffsetY`, `string Tag`, `bool IsTrigger`. `Rectangle Bounds` : AABB centrée sur `WorldPosition + Offset`, dimensions × `WorldScale` (pas de rotation).

**⚠** `OnCollision(Collider)` et `OnTriggerEnter(Collider)` sont des méthodes **vides et non virtuelles** : `CollisionSystem` les appelle, mais un script ne peut pas les surcharger — les callbacks de collision ne sont pas encore utilisables.

## `PhysicsBody`

| Membre | Rôle |
| --- | --- |
| `Vector2 Velocity` | |
| `bool IsStatic` | N'intègre ni vélocité ni gravité (mur/sol fixe). |
| `float GravityScale` | Défaut `1`. |
| `float Friction` | Défaut `0` ; amortissement exponentiel de `Velocity.X`. |
| `float Mass` | Défaut `20`. |
| `float Restitution` | Défaut `0` (pas de rebond) ; `1` = rebond parfait. |
| `float MaxFallSpeed` | Défaut `1000`. |
| `bool IsGrounded` | Remis à `false` chaque frame, rétabli par `CollisionSystem`. |
| `ApplyForce(Vector2)` (÷ `Mass`) / `ApplyImpulse(Vector2)` | |

`Update(dt)` : `Physics.CalculatePhysics` (gravité `Physics.gravity = -9.81` × `GravityScale` × `Mass`, friction), clamp de chute, puis intégration `transform.Position += Velocity * dt` (⚠ valeur **locale** — voir la limite pour les objets enfants dans `architecture.md`).

**Collisions** : `CollisionSystem` (`KoreEngine.Physics` n'existe plus comme namespace, les types sont dans `KoreEngine`) ignore toute paire dont un des deux objets n'a pas de `PhysicsBody` : **un mur doit avoir un `PhysicsBody` avec `IsStatic`**.

## Animation

- **`AnimationFrame`** (struct) : `Rectangle SourceRect`, `float Duration`.
- **`Animation`** : `Name`, `AnimationFrame[] Frames`, `Loop`. `Animation.FromStrip(...)` génère les frames d'une bande uniforme.
- **`AnimationStateMachine`** : `Add(Animation)`, `AddTransition(from, to, Func<bool>)`, `SetDefault`, `GoTo`, `Update(dt)`, `CurrentState`, `CurrentFrame`, `IsFinished`.

### `Animator`

| Type | Rôle |
| --- | --- |
| `AnimationDefinition` | `Name`, `SizeX/Y`, `Row`, `StartCol`, `FrameCount`, `FrameDuration`, `Looping`. |
| `AnimationCondition` (enum) | `Always`, `IsGrounded`, `IsNotGrounded`, `VelocityXAbsGreaterThan`, `VelocityXAbsLessThan`, `VelocityYGreaterThan`, `VelocityYLessThan`. |
| `AnimationTransition` | `From`, `To`, `Condition`, `Threshold`. |

`Definitions` / `Transitions` sont éditées via `ListField`. `Apply()` reconstruit la state machine (appelée dans `Start()` et par le bouton « Apply »). `Serialize()` / `Deserialize()` personnalisés. Cherche `PhysicsBody` et `SpriteRenderer` sur le même objet.

## `AudioSource`

`string ClipPath`, `float Volume` (0–1), `float Pitch` (0.1–3), `bool Loop`, `bool Spatial2D` (position de la piste synchronisée sur `WorldPosition` chaque frame ; listener SDL_mixer fixe à l'origine), `bool PlayOnStart`. `Play()` / `Stop()` / `Pause()` / `Resume()` (aussi en boutons dans l'Inspector, avec une ligne « Status »). `OnDestroy()` libère piste et audio, de façon idempotente.

## UI (`KoreEngine.UI`)

- **`UIElement`** (abstrait) : `Rectangle Rect` (coordonnées de référence), `bool Visible`, `UICanvas? Canvas`. `ScreenX/Y/Width/Height` = valeurs × `Canvas.ScaleX/Y`. `Render()` délègue à `Draw(Renderer)` si `Visible`.
- **`UICanvas`** : `ReferenceWidth/Height` (1920×1080), `ScaleX/Y` = `SceneManager.ViewportWidth/Height ÷ Reference`. `Add<T>(element)` crée un `GameObject` enfant et y attache l'élément — **le canvas doit déjà être dans la scène**.
- **`UIButton`** : `TextureNormal/Hover/Pressed` (+ chemins `…Path`), `KoreEvent OnClick`. Hover = souris dans le rectangle (via `SceneManager.ViewportMouse*`) ; clic déclenché à l'appui du bouton gauche (`SDL.GetMouseState`). Hover/pressed retombent sur la texture normale si absentes.
- **`UIImage`** : `Texture`/`TexturePath`, `Filtering` (défaut `Linear`) ; dessine sans interaction.

Menu *Create* de la Hierarchy : `UI Canvas`, `UI Button`, `UI Image` (composants vides, sans texture par défaut).

### `KoreEvent` / `PersistentListener` (style `UnityEvent`)

```csharp
public KoreEvent OnClick = new();           // déclaré comme champ public
// par code :
button.OnClick.AddListener(() => Console.WriteLine("clic"));
button.OnClick.Invoke();
```

| Membre | Rôle |
| --- | --- |
| `List<PersistentListener> Listeners` | Listeners persistants, édités dans l'Inspector et sérialisés. |
| `AddListener(Action)` / `RemoveListener(Action)` | Abonnés par code, **non** sérialisés. |
| `Invoke()` | Listeners persistants, puis abonnés code. |

`PersistentListener` : `TargetObject` (nom du GameObject), `ComponentType` (nom du type), `MethodName`, `Argument` (chaîne). Méthodes éligibles : publiques, d'instance, `void`, 0 ou 1 paramètre `string`/`int`/`float`/`bool`. Résolution par nom à chaque appel.

## Input (`KoreEngine`, statique)

| Membre | Comportement actuel |
| --- | --- |
| `NewFrame()` | À appeler en début de frame (fait par `EditorWindow`/`GameLoop`). |
| `HandleEvent(SDL.Event)` | Alimente les ensembles internes depuis les événements clavier. |
| `GetKey(KeyCode)` / `GetAnyKey()` | Vrai **seulement** quand un événement « touche enfoncée » a été reçu pendant la frame (suit l'auto-répétition de l'OS). |
| `GetKeyDown(KeyCode)` / `GetAnyKeyDown()` | ⚠ Reste vrai **indéfiniment** après la première pression : l'ensemble interne n'est jamais vidé au relâchement. |
| `GetKeyUp(KeyCode)` | ⚠ Faux après la première pression de la touche. |

Les noms ne suivent donc pas la convention d'Unity : en attendant un correctif, éviter `GetKeyDown`/`GetKeyUp` pour du gameplay. `KeyCode` mappe les codes de touches SDL.

## Types de champs Inspector (`InspectorField` et dérivés)

Tous ont `Label` (requis) et `ReadOnly`.

| Type | Usage |
| --- | --- |
| `TextField` | Lecture seule (`Func<string> Get`). |
| `FloatField` / `IntField` | `Get`/`Set`, `Value` (vitesse de drag), `Min`/`Max` (si égaux, pas de clamp), `Slider` (slider au lieu d'un drag, borné par `Min`/`Max`). |
| `BoolField` | Checkbox. |
| `StringField` | `InputText`, `MaxLength` (défaut 64). |
| `EnumField` | Combo : `Names` + `Get`/`Set` par index. |
| `TextureField` | Picker de textures, scanne `Assets/` et `Packages/`. |
| `AudioClipField` | Picker `.wav/.mp3/.ogg/.flac`. |
| `ComponentRefField` | Référence `Component`, picker filtré par type. |
| `GameObjectField` | Référence `GameObject` (lecture via `Get`). |
| `ButtonField` | Bouton d'action (`Action`), `Tooltip` optionnel. |
| `KoreEventField` | Éditeur de `KoreEvent` (liste de listeners). |
| `ListField` | Liste éditable (ajout/suppression/champs par item), `ItemContextActions`. Utilisé par `Animator`. |

Pour les composants **sans** `GetInspectorFields()`, l'auto-draw par réflexion gère : `float`, `int`, `byte`, `bool`, `string` (avec picker de texture pour les paires `XxxPath`/`Xxx`), `Vector2`, `Color`, `Rectangle`, enums, références `Component` et `GameObject`, et `KoreEvent`.

## Instanciation dynamique — `GameObject.Instantiate`

```csharp
GameObject clone = someObject.Instantiate(targetScene, position: null);
```

Round-trip de sérialisation (`SerializeObjectTree` → `DeserializeObjectTree`) : les composants, la hiérarchie d'enfants et les `KoreEvent` sont copiés. Les champs qui référencent un objet **hors** de la hiérarchie clonée ne sont **pas** recopiés — à réassigner après coup.

## Prefabs — `PrefabManager` (statique)

| Membre | Rôle |
| --- | --- |
| `PrefabFiles()` | Tous les `.kprefab` sous `Assets/`. |
| `Save(GameObject obj, string path)` | Sérialise l'objet et ses enfants. |
| `Instantiate(string path, Scene scene, GameObject? parent = null, Vector2? position = null)` | Désérialise et ajoute à `scene`, éventuellement sous `parent` et/ou à `position`. |

## `ProjectSettings` (statique)

`StartupScene` (`string?`), `Load()` / `Save()` sur `Assets/ProjectSettings.txt`.

## Types de base

- **`Vector2`** (struct, `float`) : `+ − * /`, `Zero`, `One`, `NegativeOne`, `Up (0, 1)`, `Down (0, −1)`, `Left`, `Right`, `Length()`, `LengthSquared()`, `Normalize()`, `Distance`, `DistanceSquared`, `Dot`, `Cross`, `Lerp`, `LerpUnclamped`.
- **`Rectangle`** (struct, **`int`**) : `X, Y, Width, Height`, `Left/Right/Top/Bottom`, `Intersects(other)`.
- **`Color`** (classe) : `R, G, B` (int), `Black/White/Red/Green/Blue`. Pas d'alpha (passé en `byte a` au `Renderer`).
- **`Logger`** (statique) : `Log`, `Warning`, `Error(message, file?, line?)`, `Sucess` (sic), `Clear`, `Entries`, `OnLog`. Niveaux : `Info`, `Warning`, `Error`, `Sucess`.
