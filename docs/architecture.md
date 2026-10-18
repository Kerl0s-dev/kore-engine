# KoreEngine — Architecture

> Mis à jour à partir du code source complet le plus récent (archive du 18/10/2026). Remplace la version du 09/09/2026, qui décrivait encore un Hub WPF, `GameObject.LocalPosition`, `InputManager`, `RectRenderer` et une physique à gravité globale — tout cela a changé depuis.

## Vue d'ensemble

KoreEngine est un moteur de jeu 2D écrit en C#/.NET 10, avec un éditeur intégré façon Unity (component-based, hot-reload de scripts, scènes sérialisées, prefabs) et un launcher façon Unity Hub pour gérer plusieurs projets.

**Stack technique :**

- **Rendu/fenêtrage** : SDL3 via le binding `SDL3-CS` (paquets natifs Windows et Linux, sélectionnés par `Condition="'$(OS)' == 'Windows_NT'"`).
- **Audio** : SDL3_mixer, **images** : SDL3_image, **texte** : SDL3_ttf (`FontManager`).
- **UI éditeur** : ImGui.NET 1.91.6.1, avec un backend maison (`ImGuiBackend`) vers SDL3.
- **UI Hub** : mini-toolkit maison directement sur `SDL_Renderer` + SDL3_ttf (**plus de WPF**, pas d'ImGui) — voir `docs/hub-and-build.md`.
- **Scripting/hot-reload** : Roslyn (`Microsoft.CodeAnalysis.CSharp`, référencé par `KoreEngine.Runtime`) + `AssemblyLoadContext` collectible.

## Structure des projets

Le moteur est scindé en **trois projets**, tous référencés par `KoreEngine.slnx` :

```text
KoreEngine/
├── KoreEngine.slnx
├── Directory.Build.props             # VersionPrefix unique (1.0.0) → AssemblyVersion / FileVersion
├── KoreEngine.Runtime/               # Library — Engine/, Components/, Core/, Physics/, Input/, Animation/
├── KoreEngine.Editor/                # Library — éditeur ImGui, ProjectReference vers Runtime
├── KoreEngine.Hub/                   # WinExe SDL — launcher multi-projets (AssemblyName "Kore Engine Hub")
└── docs/
```

- `KoreEngine.Runtime.csproj` : packages SDL3-CS (base + Image + Mixer + Shadercross + TTF, variantes Windows/Linux) et Roslyn. `CopyLocalLockFileAssemblies=true`.
- `KoreEngine.Editor.csproj` : `ImGui.NET` + `ProjectReference` vers Runtime (SDL3-CS arrive transitivement).
- `KoreEngine.Hub.csproj` : SDL3-CS + Image + TTF uniquement. **Aucune référence** à Runtime ni Editor : il scaffolde des fichiers texte et pilote `dotnet` en sous-processus. La police du Hub (`Fonts/RobotoMono-VariableFont_wght.ttf`) est copiée à côté de l'exécutable.

Chaque projet de jeu généré par le Hub est **son propre éditeur** : `Program.cs` fait `new EditorWindow(name, 1280, 720).Run()`. Le bouton Play/Pause/Stop bascule un mode de simulation dans la même fenêtre. Un sous-projet `Player/` headless (sans ImGui) existe pour produire un exécutable sans éditeur.

### Versionnement

`Directory.Build.props` définit `VersionPrefix`. Le Hub (`ProjectInfoService.GetEngineVersion`) affiche, pour chaque projet, le `FileVersion` du `KoreEngine.Runtime.dll` copié dans **son propre** `bin/Debug/net10.0` — donc la version réellement utilisée, pas celle du moteur source.

## Séparation Runtime / Editor / Hub

`KoreEngine.Runtime` ne référence jamais `KoreEngine.Editor` — dépendance à sens unique stricte. `SceneManager` (Runtime) expose l'événement `OnSceneChanging`, consommé par l'Editor pour réagir (vider la sélection) sans que le Runtime ne connaisse l'Editor.

Les types sont presque tous dans le namespace plat `KoreEngine` ; exceptions : `KoreEngine.UI` (composants UI), `KoreEngine.Editor` (panneaux), `KoreEngine.Hub.*`.

## Distribution du moteur dans un projet généré

Un projet généré référence le moteur **en binaire** (`<Reference><HintPath>` vers `KoreEngine.Runtime.dll` et `KoreEngine.Editor.dll`), jamais via `ProjectReference`. `ProjectScaffolder.CopyEngineDependencies` copie les dépendances runtime (ImGui.NET, Roslyn, SDL3…) et les binaires natifs de l'OS courant (`runtimes/{rid}/native/*`, copiés **à la racine** pour que `SDL3` soit toujours trouvé) à côté de l'exe.

**Cette copie ne se met jamais à jour toute seule.** Elle est refaite par `ProjectScaffolder.RefreshDependencies`, appelée par `EditorLauncher.SyncEngineDependencies` **avant chaque lancement** depuis le Hub. Prérequis : le moteur doit avoir été recompilé avant.

## Boucle de l'éditeur (`EditorWindow.Run`)

Ordre par frame :

1. **Input** : `Input.NewFrame()`, `SDL.PollEvent`, événements transmis à ImGui ; transmis au jeu (`Input.HandleEvent`) seulement si ImGui ne capture pas le clavier. Fermeture → popup de confirmation.
2. **Taille de fenêtre**, **dt** (clampé entre `0.0001` et `1/30` s), `ScriptWatcher.Update`.
3. **ImGui NewFrame**, raccourcis clavier (voir `editor-guide.md`).
4. `SceneManager.ApplyPendingScene()` + `ScriptWatcher.FinalizeUnloadIfPending()`.
5. **Simulation** (si `Playing`) : `GamePanel.BindUiSize()` puis `SceneManager.Update(dt)` — sauf en pause, hors demande de Step.
6. **Rendu** : Scene view → `RenderTexture` avec l'`EditorCamera` ; Game view → `RenderTexture` avec la caméra de la scène ; puis écran réel : clear, dockspace, panneaux, ImGui, `Present`.

### Deux viewports : Scene et Game

- **Scene** (`ViewportPanel`) : toujours l'`EditorCamera`, toujours en mode édition (gizmos de transform, gizmos de caméra, sélection au clic), y compris pendant le Play. Pan = clic molette, zoom = Ctrl+molette (`0.05`–`20`).
- **Game** (`GamePanel`) : toujours `scene.Camera`, rendu avec la couleur de fond de la caméra. Liste déroulante de résolution : *Free* (la texture suit la taille du panneau), `1920x1080`, `1280x720`, `1024x768` (ratio conservé, bandes noires).
- **État global partagé** : `SceneManager.ViewportWidth/Height` (échelle de `UICanvas`) et `ViewportMouseX/Y/InBounds` (lus par `UIButton`). **Le Game view est le seul propriétaire de la souris** ; le Scene view garde sa souris locale. Avant la simulation, `GamePanel.BindUiSize()` replace la taille UI sur celle du Game view (le Scene view l'a modifiée pendant son rendu).
- La scène est rendue **deux fois par frame**. `Render()` n'ayant pas de `dt`, c'est sans effet, mais un `Render()` avec effets de bord s'exécuterait deux fois.
- `GamePanel.RenderScene` met à jour `Camera.ViewWidth/ViewHeight` de la caméra de scène à la taille du Game view, **même en mode édition**. Ces champs sont sérialisés dans le `.kscene` : ils changent donc avec la taille du panneau (bruit dans les diffs, sans conséquence fonctionnelle puisqu'ils sont recalés à chaque frame).

## Pipeline de hot-reload des scripts

**Composants impliqués :** `ScriptWatcher` → `ScriptCompiler` → `SceneManager` → `InspectorPanel`.

1. `ScriptWatcher` surveille `Assets/**/*.cs` (`FileSystemWatcher`), debounce de 1.5 s (`BuildDelay`).
2. `ScriptCompiler.CompileAsync` compile avec Roslyn sur un thread de fond (références = assemblies chargés, `OptimizationLevel.Debug`).
3. Succès → nouvel `AssemblyLoadContext` collectible (`ScriptsContext`) ; l'ancien est mis en attente (`_pendingUnloadContext`).
4. `OnCompileSuccess` : sauvegarde de la scène courante et préparation du rechargement (`LoadSceneFromFile` remplit `next`).
5. Thread principal : `ApplyPendingScene()` bascule `current = next`, puis `FinalizeUnloadIfPending()` tente de décharger l'ancien contexte (jusqu'à 10 `GC.Collect()`, avertissement loggé si échec).
6. `InspectorPanel.componentTypes` est invalidé sur `OnCompileSuccess` (rescan du menu *Add Component*, dédoublonné par nom).

**Erreurs de compilation** : chaque diagnostic d'erreur est loggé via `Logger.Error(message, fichier, ligne)` ; dans la Console, un double-clic ouvre le fichier à la ligne fautive (`ExternalEditor.OpenFileAtLine`, Visual Studio via `vswhere`/`devenv`, sinon ouverture par défaut de l'OS).

## Système de scènes

### `SceneManager` (Runtime, statique)

- `AssetsDirectory` : par défaut `AppContext.BaseDirectory/Assets` (jeu buildé) ; l'Editor l'écrase au démarrage avec le vrai dossier du projet.
- `ScanAndRegisterScenes()` : registre nom → chemin de tous les `.kscene` sous `Assets/` ; deux scènes de même nom → avertissement, la dernière gagne.
- `LoadScene(name)` / `LoadSceneFromFile(path)` : **sauvegardent d'abord la scène courante** (si `SaveLocked` est faux), puis désérialisent la nouvelle dans `next`. `ApplyPendingScene()` la rend courante et appelle `FindCamera()`.
- `OnSceneChanging` (pas `OnSceneChanged`), `UnloadScene`, `NotifyPathRenamed(old, new)`, `SaveLocked` (verrouille la sauvegarde pendant le Play), `CurrentScenePath`.

### `Scene`

`RootObjects` (objets sans parent), `AllObjects` (aplati), `Collisions`, `Camera` (**le getter lève une exception si la scène n'a pas de caméra**). `FindCamera()` prend la première `Camera` trouvée, sinon **crée un objet « Main Camera »**. `Update` en trois passes : composants hors `PhysicsBody` → `PhysicsBody` → `Collisions.Update`. `Remove` (appelle `OnDestroy` récursivement, détache, désenregistre les colliders), `DestroyAll`, `RefreshColliders`, `RegisterColliders(obj)`.

### Format `.kscene` et sérialisation (`SceneSerializer`)

Blocs à accolades (`Object: obj_N { … Component: Type { … } }`), nombres en `InvariantCulture`.

- **Champs sérialisés automatiquement** : champs publics d'instance non `readonly` de type `int`, `float`, `double`, `bool`, `string`, `byte`, `long`, `Vector2`, `Rectangle`, `Color`, ou enum — sauf `[NonSerialized]`. Les chaînes contenant un espace sont entre guillemets.
- **Références** : champs de type `GameObject` ou `Component` écrits `@obj_2` / `@obj_2:Camera` et résolus après création de tous les objets. Une référence vers un objet hors de la scène (ou du sous-arbre cloné) est omise.
- **Convention `XxxPath` → `Xxx`** : une chaîne `TexturePath` charge automatiquement le champ `IntPtr Texture` via `TextureCache` au chargement.
- **`KoreEvent`** : tout champ public de type `KoreEvent` est sérialisé par le sérialiseur lui-même, une ligne par listener : `OnClick.Listener: Cible|Composant|Méthode|Argument` (chaque partie échappée par `Uri.EscapeDataString`). Aucun code spécifique dans le composant.
- **Hooks personnalisés** : `Component.Serialize()` / `Deserialize(List<string>)` pour les listes et types complexes (ex. `Animator`).
- **Ordre de chargement** (`LoadObjectsInto`) : (1) création des objets, **sans composant** ; (2) création/attache de tous les composants et `ApplyFields` ; (3) garde-fou : un `Transform` par défaut est ajouté aux objets qui n'en ont pas ; (4) hiérarchie rétablie via `Transform.AttachToParentKeepLocal` — les positions du fichier sont des valeurs **locales**, `SetParent` (qui conserve la position monde) ne doit donc **pas** être utilisé ici ; (5) résolution des références.

## Prefabs (`PrefabManager`) et `GameObject.Instantiate`

Un `GameObject` et sa hiérarchie peuvent être sérialisés dans un `.kprefab` (même format que les scènes, sans en-tête `Scene:`, via `SerializeObjectTree` / `DeserializeObjectTree`). `PrefabManager` : `PrefabFiles()`, `Save(obj, path)`, `Instantiate(path, scene, parent?, position?)`.

`GameObject.Instantiate(Scene, Vector2? position)` fonctionne par le même aller-retour de sérialisation. **Limite** : les champs qui référencent un objet hors de la hiérarchie clonée ne sont pas recopiés. Dans l'éditeur, *Duplicate* (menu contextuel de la Hierarchy) s'appuie sur `Instantiate`.

## `ProjectSettings`

`Assets/ProjectSettings.txt` (format texte minimal) : pour l'instant uniquement `StartupScene`, la scène chargée au démarrage d'un jeu buildé. Se règle dans l'éditeur via *Set as Startup Scene* (clic droit sur un `.kscene`).

## Cycle de vie des composants

- `Start()` : une seule fois quand `Playing` passe à `true` (`Scene.Start()`) ; en jeu buildé, juste après le chargement de la scène de démarrage.
- `OnDestroy()` : retrait du composant (`RemoveComponent`), `Scene.Remove`, `DestroyAll`. **Peut être appelé plusieurs fois** sur un même composant lors d'un `GameObject.Destroy()` (appel direct, puis `Scene.Remove` → `DestroyRecursive`) : le rendre idempotent (c'est le cas d'`AudioSource`, qui remet ses handles à zéro).

## `Transform`, hiérarchie et coordonnées

`Transform` est un **composant** (`[UnremovableComponent]`) présent sur tout `GameObject` (`gameObject.transform`). Champs locaux : `Position`, `Rotation` (degrés), `Scale` (défaut `(1,1)`). Propriétés en lecture : `WorldPosition` (tient compte de l'échelle et de la rotation du parent), `WorldRotation`, `WorldScale`, `Parent`. `SetParent(parent, scene)` **préserve** position/rotation/échelle monde ; `IsDescendantOf`, `Translate`, `Rotate`.

Convention : **Y vers le haut** en espace monde ; la conversion vers l'écran (Y vers le bas) se fait uniquement dans `Camera.WorldToScreen` / `ScreenToWorld`. Les unités sont des pixels (`Zoom` = facteur d'échelle monde → écran). `Vector2.Up = (0, 1)`.

**Limite connue** : `PhysicsBody` et `CollisionSystem` écrivent dans `transform.Position` (valeur **locale**), alors que `Collider.Bounds` lit `WorldPosition`. Pour un objet physique **enfant** d'un autre objet, l'intégration et la résolution de collision ignorent la position du parent.

## Physique

### `PhysicsBody`

`Velocity`, `IsStatic`, `GravityScale` (1), `Friction` (0), `Mass` (20), `Restitution` (0 = pas de rebond), `MaxFallSpeed` (1000), `IsGrounded`, `PreviousPosition` (masqué). `ApplyForce` (÷ masse) / `ApplyImpulse`.

`Update(dt)` : reset de `Velocity.Y` si au sol, `Physics.CalculatePhysics` (gravité + friction), clamp de chute, puis **seule intégration de la position** (`transform.Position += Velocity * dt`).

### `Physics`

`Physics.gravity = -9.81f` (statique). `CalculatePhysics` applique `Velocity.Y += gravity * GravityScale * Mass * dt` : **l'accélération dépend donc de la masse**, contrairement à une vraie gravité. Friction : `Velocity.X *= Pow(1 − Friction, dt)`.

### `CollisionSystem`

Paires O(n²), résolution par **Swept AABB**. **Les deux colliders d'une paire doivent avoir un `PhysicsBody`** (un mur statique doit donc avoir un `PhysicsBody` avec `IsStatic`), sinon la paire est ignorée — y compris pour les triggers. Deux statiques : ignorés. Statique + dynamique : le dynamique est repoussé. Deux dynamiques : répartition selon la masse. `Restitution` = max des deux corps. `CheckGrounded` positionne `IsGrounded`.

`Collider` : `Width/Height` (int), `OffsetX/OffsetY`, `Tag`, `IsTrigger`, `Bounds` (AABB centrée sur `WorldPosition + Offset`, × `WorldScale`, pas de rotation).

**⚠ Callbacks de collision non branchés** : `Collider.OnCollision(Collider)` et `OnTriggerEnter(Collider)` existent et sont appelés par `CollisionSystem`, mais ce sont des méthodes vides **non virtuelles** — un script ne peut pas les surcharger. `CollisionInfo` (`Other`, `Normal`, `Penetration`) n'est utilisée nulle part.

## Animation

`AnimationFrame` (`SourceRect`, `Duration`), `Animation` (`Name`, `Frames`, `Loop`, `FromStrip(...)`), `AnimationStateMachine` (états + transitions `Func<bool>`, `Update`, `GoTo`, `CurrentFrame`, `IsFinished`). `Animator` reconstruit la machine depuis `Definitions` / `Transitions` éditées dans l'Inspector. **⚠** `SpriteRenderer.Render` écrase actuellement le `SourceRect` de la frame courante par `(0, 0, FrameSize)` : la machine d'états avance bien, mais l'affichage reste sur la première frame de la feuille (voir `components-reference.md`).

## UI (`KoreEngine.UI`)

- **`UIElement`** (abstrait) : `Rect` (coordonnées de référence), `Visible`, `Canvas`. `ScreenX/Y/Width/Height` = valeurs × `Canvas.ScaleX/Y`. `Render` délègue à `Draw` si `Visible`.
- **`UICanvas`** : `ReferenceWidth/Height` (1920×1080), `ScaleX/Y` = `SceneManager.ViewportWidth/Height ÷ Reference`. `Add<T>(element)` crée un `GameObject` enfant ; le canvas doit déjà être dans la scène. `Canvas` est une référence sérialisée (`@obj_N:UICanvas`).
- **`UIButton`** : trois textures (normal/hover/pressed) + chemins ; `KoreEvent OnClick` (listeners persistants configurables dans l'Inspector + abonnés par code via `AddListener`). Souris via `SceneManager.ViewportMouseX/Y/InBounds` et `SDL.GetMouseState`.
- **`UIImage`** : texture + `Filtering` (défaut `Linear`).

Dans le jeu buildé, `GameLoop.UpdateViewportState()` renseigne cet état chaque frame (voir plus bas) ; sans cela, `ScaleX` vaudrait 0 et les éléments UI seraient invisibles.

### `KoreEvent` (style UnityEvent)

`KoreEvent.Listeners` : liste de `PersistentListener` (`TargetObject` = **nom** du GameObject, `ComponentType`, `MethodName`, `Argument` encodé en chaîne). Méthodes appelables : publiques, d'instance, `void`, 0 ou 1 paramètre `string`/`int`/`float`/`bool`. `Invoke()` exécute les listeners persistants puis les abonnés code. La résolution se fait **par nom à chaque appel** (robuste au hot-reload) ; deux objets de même nom → le premier est appelé.

## Input

`Input` (statique, `KoreEngine`) : `NewFrame()`, `HandleEvent(SDL.Event)`, `GetKey`, `GetKeyDown`, `GetKeyUp`, `GetAnyKey`, `GetAnyKeyDown` ; `KeyCode` mappe les touches SDL. **⚠ Comportement actuel** : `GetKey(k)` n'est vrai que pour la frame où un événement de touche enfoncée est reçu (et se répète au rythme de l'auto-répétition de l'OS), tandis que `GetKeyDown(k)` reste vrai **indéfiniment** après la première pression (l'ensemble `keysDown` n'est jamais vidé) et `GetKeyUp(k)` reste faux. Les noms ne suivent donc pas la sémantique d'Unity — voir `components-reference.md`.

## Boucle de jeu buildé (`GameLoop`)

Zéro dépendance à ImGui/Editor, pas de mode édition, rendu direct à l'écran. Le constructeur crée la fenêtre, le `Renderer` et appelle `TextureCache.Init` : **il doit être appelé avant `SceneManager.LoadScene`**, sinon les textures chargées pendant la désérialisation restent à zéro.

`Run()` : `ApplyPendingScene` → `NotifyStart` → boucle : input → resize → `ApplyPendingScene` → **`UpdateViewportState()`** (taille de fenêtre → `SceneManager.Viewport*`, souris, `ViewWidth/Height` de la caméra) → `SceneManager.Update(dt)` (toujours) → `Clear(Camera.Color)` / `Render` / `Present`.

## Rendu et ressources

- **`Renderer`** : `Clear`, `Present`, `Flush`, `DrawRect` / `DrawRectOutline`, `DrawTexture`, `DrawTexturePart` (surcharges avec ou sans `Camera`, flip, rotation).
- **`RenderTexture`** : `BeginRender` / `EndRender` flushent et réinitialisent le clip rect ; `Resize` est sans effet si la taille est identique.
- **`TextureCache`** : cache chemin → texture ; **un échec de chargement est mis en cache** (`IntPtr.Zero`) ; `Get` renvoie `Zero` (sans mise en cache) tant que `Init` n'a pas été appelé. `Resolve(path)` ré-enracine les chemins contenant `/Assets/` ou `/Packages/` sur les dossiers du projet courant (`SceneManager.AssetsDirectory` et son voisin `Packages`), pour qu'un jeu buildé charge ses propres copies.
- **`FontManager`** : SDL3_ttf, cache de polices par `(chemin, taille)` ; `DrawText` recrée une texture à chaque appel.
- **`AudioManager`** : wrapper SDL3_mixer (`LoadAudio`, pistes, volume, pitch, position 3D) ; `Init()` / `Quit()` doivent être appelés par le programme (l'éditeur le fait, `GameLoop` non).

## Types de base

- **`Vector2`** (struct, `float`) : opérateurs `+ − * /` (vecteur et scalaire), `Zero/One/NegativeOne/Up(0,1)/Down(0,−1)/Left/Right`, `Length`, `LengthSquared`, `Normalize`, `Distance`, `Dot`, `Cross`, `Lerp`, `LerpUnclamped`, égalité.
- **`Rectangle`** (struct, **`int`**) : `X, Y, Width, Height`, `Left/Right/Top/Bottom`, `Intersects`.
- **`Color`** (classe) : `R, G, B` (int), `Black/White/Red/Green/Blue` ; pas d'alpha (paramètre `byte a` du `Renderer`).

## Pipeline de build joueur

Voir `docs/hub-and-build.md` : trois implémentations coexistent, une seule est branchée à une UI (bouton **Build** de l'éditeur).
