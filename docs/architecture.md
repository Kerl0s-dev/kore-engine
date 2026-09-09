# KoreEngine — Architecture

> Mis à jour à partir du code source complet le plus récent (archive du 09/09/2026). Remplace la version précédente, qui ne couvrait pas encore `KoreEngine.Hub` ni le pipeline de build joueur.

## Vue d'ensemble

KoreEngine est un moteur de jeu 2D écrit en C#/.NET, avec un éditeur intégré façon Unity (component-based, hot-reload de scripts, scènes sérialisées, prefabs) et un launcher façon Unity Hub pour gérer plusieurs projets.

**Stack technique :**
- **Rendu/fenêtrage** : SDL3 via le binding `edwardgushchin/SDL3-CS`
- **UI éditeur** : ImGui.NET, backend custom (`ImGuiBackend`) pontant ImGui vers SDL3
- **UI Hub** : WPF (`KoreEngine.Hub`, séparé de l'éditeur ImGui)
- **Scripting/hot-reload** : Roslyn (`Microsoft.CodeAnalysis.CSharp`) + `AssemblyLoadContext` collectible
- **Audio** : SDL3_mixer via le même binding
- **Police/texte** : SDL3_ttf via `FontManager`

## Structure réelle des projets

Le moteur est scindé en **trois projets**, tous référencés par `KoreEngine.slnx` :

```
KoreEngine/
├── KoreEngine.slnx
├── KoreEngine.Runtime/
│   └── KoreEngine.Runtime.csproj     # Library — Engine/, Components/, Core/, Physics/, Input/, Animation/
├── KoreEngine.Editor/
│   └── KoreEngine.Editor.csproj      # Library — Editor ImGui, référence Runtime via <ProjectReference>
└── KoreEngine.Hub/
    └── KoreEngine.Hub.csproj         # WPF — launcher multi-projets, ne référence NI Runtime NI Editor
```

- `KoreEngine.Runtime.csproj` : référence directement les packages SDL3-CS (rendu, audio, texte).
- `KoreEngine.Editor.csproj` : référence `ImGui.NET` + `Microsoft.CodeAnalysis.CSharp`, et `KoreEngine.Runtime.csproj` via `<ProjectReference>` (SDL3-CS arrive transitivement).
- `KoreEngine.Hub.csproj` (WPF) : **totalement indépendant** du moteur au niveau des références de projet — il ne fait que scaffolder/lancer des processus `dotnet` externes et lire des fichiers texte (voir `docs/hub-and-build.md`). Il ne référence ni `KoreEngine.Runtime.dll` ni `KoreEngine.Editor.dll`.

Chaque projet de jeu généré (via le Hub, `ProjectScaffolder`) est **son propre éditeur** : `Program.cs` instancie directement `new EditorWindow(projectName, 1280, 720).Run()`. Il n'y a pas de séparation entre "l'éditeur" et "le jeu" au niveau du projet principal généré — le bouton Play/Pause/Stop bascule un mode de simulation dans la même fenêtre. Un sous-projet `Player/` séparé (voir plus bas) existe spécifiquement pour produire un exécutable sans éditeur.

## Séparation Runtime / Editor / Hub

`KoreEngine.Runtime` ne référence jamais `KoreEngine.Editor` — dépendance à sens unique stricte. `SceneManager` (Runtime) expose l'événement `OnSceneChanging`, consommé par `EditorSelection`/`EditorWindow` (Editor) pour réagir aux changements de scène sans que le Runtime ne connaisse l'existence de l'Editor.

`KoreEngine.Hub` est encore plus découplé : c'est une application WPF distincte qui ne charge jamais le moteur en mémoire. Elle scaffolde des fichiers texte (`.csproj`, `.sln`, `Program.cs`) et pilote `dotnet build`/`clean`/`publish` en sous-processus. Voir `docs/hub-and-build.md`.

## Distribution du moteur dans un projet généré

Un jeu généré par `ProjectScaffolder` référence le moteur en binaire uniquement (`<Reference><HintPath>` vers `KoreEngine.Runtime.dll` et `KoreEngine.Editor.dll`), jamais via `ProjectReference` vers les sources. `ProjectScaffolder.CopyEngineDependencies` copie les dépendances runtime (ImGui.NET.dll, binaires natifs SDL3, `Microsoft.CodeAnalysis.CSharp.dll`, etc.) depuis les dossiers de sortie du moteur, à côté de l'exe généré — nécessaire car un simple `HintPath` ne copie que la DLL elle-même, jamais ses propres dépendances (contrairement à `ProjectReference`/`PackageReference`).

**Cette copie ne se met jamais à jour automatiquement.** Deux mécanismes la déclenchent :
- `RefreshDependencies` (Hub, menu contextuel "Refresh Dependencies" sur un projet).
- `EditorLauncher.SyncEngineDependencies`, appelé **avant chaque lancement** d'un projet depuis le Hub — pas seulement à la création.

## Pipeline de hot-reload des scripts

**Composants impliqués :** `ScriptWatcher` → `ScriptCompiler` → `SceneManager` → `InspectorPanel`.

1. `ScriptWatcher` surveille `Assets/**/*.cs` via `FileSystemWatcher`, debounce de 1.5s (`BuildDelay`).
2. `ScriptCompiler.CompileAsync` compile avec Roslyn sur un thread de fond (`Task.Run`).
3. Succès → nouvel `AssemblyLoadContext` collectible (`ScriptsContext`), l'ancien passe en attente (`_pendingUnloadContext`), pas déchargé immédiatement.
4. `OnCompileSuccess` (sur le thread de fond) : sauvegarde de la scène courante, préparation du rechargement (`LoadSceneFromFile` → remplit `next`).
5. Sur le **thread principal** : `ApplyPendingScene()` bascule `current = next`.
6. Juste après : `ScriptWatcher.FinalizeUnloadIfPending()` → `ScriptCompiler.FinalizeUnload()` tente de décharger l'ancien contexte (`GC.Collect()` en boucle, jusqu'à 10 tentatives, avertissement loggé si échec persistant).

**Gestion d'erreur de compilation — confirmée fonctionnelle.** `ScriptCompiler.Compile()` gère bien la branche d'échec : itère sur `result.Diagnostics` (erreurs uniquement), extrait fichier/ligne via `diag.Location.GetLineSpan()`, appelle `Logger.Error(message, file, line)` pour chaque erreur puis `OnCompileError?.Invoke(...)` avec le détail complet. Les erreurs remontent bien dans la Console de l'éditeur et restent cliquables (`ExternalEditor.OpenFileAtLine`). *(Une régression avait été observée sur une archive précédente — corrigée depuis.)*

**`InspectorPanel.componentTypes`** : cache invalidé sur `OnCompileSuccess`, dédoublonné par nom (`GroupBy(t => t.Name).Select(g => g.Last())`).

## Système de scènes

**Format `.kscene`** : blocs à accolades, Position/Rotation/Scale sérialisés en `InvariantCulture`, références `@obj_X`/`@obj_X:Type`.

**`SceneManager` (Runtime)** :
- `AssetsDirectory` par défaut = `AppContext.BaseDirectory/Assets` (à côté de l'exécutable) — convient à un jeu buildé ; l'Editor écrase cette valeur au démarrage avec le vrai chemin du projet.
- `LoadSceneFromFile` sauvegarde automatiquement la scène courante avant de charger la nouvelle, si une scène est déjà chargée (`if (current != null) SaveCurrentScene();`) — sans confirmation.
- L'événement s'appelle `OnSceneChanging` (pas `OnSceneChanged`), déclenché à la fois par `LoadSceneFromFile` et `UnloadScene`.
- `NotifyPathRenamed(oldPath, newPath)` : suit un renommage de fichier/dossier dans `Assets/` pour que la scène courante (ou son dossier parent) reste correctement référencée — appelé par le Project Panel après un renommage.

**`Scene`** : `Start()` appelle `c.Start()` sur tous les composants ; trois passes ordonnées dans `Update` (composants hors `PhysicsBody` → `PhysicsBody` → `Collisions.Update`), `DestroyAll`/`DestroyRecursive`, `RefreshColliders`.

## Prefabs (`PrefabManager`)

Un `GameObject` (et toute sa hiérarchie d'enfants) peut être sérialisé dans un fichier `.kprefab`, réutilisable dans n'importe quelle scène — même format texte que les scènes, réutilise directement `SceneSerializer.SerializeObjectTree`/`DeserializeObjectTree`, sans l'en-tête `Scene:` (un prefab n'a pas de nom de scène propre).

- `PrefabManager.Save(obj, path)` : écrit sous `Assets/Prefabs/{Name}.kprefab` par convention côté éditeur (`HierarchyPanel.CreatePrefab`), mais le chemin n'est pas imposé par l'API.
- `PrefabManager.Instantiate(path, scene, parent?, position?)` : désérialise et ajoute l'objet à la scène, optionnellement sous un parent et/ou à une position donnée (sinon la position d'origine du prefab).
- Côté éditeur : clic droit sur un objet dans la Hierarchy → **Create Prefab**. Clic droit sur l'espace vide → **Instantiate Prefab** (liste tous les `.kprefab` trouvés) ; le Project Panel permet aussi de glisser/double-cliquer un `.kprefab` pour l'instancier directement dans la scène courante.

## Instanciation dynamique à l'exécution (`GameObject.Instantiate`)

`GameObject.Instantiate(Scene targetScene, Vector2? position = null)` — équivalent d'`Object.Instantiate(prefab)` à la Unity, appelable depuis un script utilisateur pour faire apparaître dynamiquement des balles, ennemis, objets ramassables, etc. Fonctionne par round-trip de sérialisation (`SerializeObjectTree` → `DeserializeObjectTree`), comme les prefabs. **Limite à connaître** : les champs qui référencent un objet *hors* de la hiérarchie clonée (ex : un script qui garde une référence directe vers la Camera de la scène) ne sont pas recopiés sur le clone — à réassigner manuellement après coup si besoin.

## `ProjectSettings`

Réglages globaux du projet, format texte minimaliste dans `Assets/ProjectSettings.txt` (même esprit que `.kscene`) : pour l'instant uniquement `StartupScene` (nom de la scène à charger au démarrage d'un jeu buildé). Nécessaire car un jeu buildé (`GameLoop`, sans éditeur) n'a personne pour choisir une scène à la main comme dans l'éditeur.

## Cycle de vie des composants

`Component.Start()` (pas `OnStart()`) est appelé une seule fois quand `Playing` passe à `true` (`Scene.Start()`, `SceneManager.NotifyStart()`, override dans `AudioSource.Start()`). `Component.OnDestroy()` est appelé par `GameObject.RemoveComponent` et `Scene.DestroyAll()`/`DestroyRecursive` — à utiliser pour libérer des ressources natives.

## Rendu et convention de coordonnées

Y-haut en espace monde, conversion exclusivement dans `Camera.WorldToScreen`/`ScreenToWorld`, pivot centré sur `WorldPosition` pour `RectRenderer`/`SpriteRenderer`/`Collider.Bounds`.

**Limite non résolue, présente dans le code actuel** : `PhysicsBody.Update()` et `CollisionSystem` écrivent la position via `Owner.Position` (alias vers `LocalPosition`), pas `Owner.WorldPosition`. Pour un objet sans parent, ça ne change rien (Local == World). Mais pour un objet physique **enfant d'un autre objet**, la résolution de collision et l'intégration de vélocité ignorent la position du parent. À signaler si tu comptes utiliser des `PhysicsBody` sur des objets enfants.

## Physique (`KoreEngine.Physics`, `KoreEngine.Components.PhysicsBody`)

### `PhysicsBody`
| Membre | Rôle |
|---|---|
| `Vector2 Velocity` | |
| `bool IsStatic` | N'intègre ni vélocité ni gravité, sert de mur/sol fixe. |
| `float GravityScale` | Multiplicateur de `GlobalGravity` (statique, `800f` par défaut, partagé). |
| `float Friction` | Amortissement exponentiel de `Velocity.X` (`Pow(1 - Friction, dt)`). |
| `float Mass` | Répartition d'impulsion entre deux corps dynamiques en collision. |
| `float MaxFallSpeed` | Clamp de `Velocity.Y` quand `GravityScale > 0`. |
| `bool IsGrounded` | Reset à `false` en début de frame, rétabli par `CollisionSystem`. |
| `ApplyForce(Vector2)` / `ApplyImpulse(Vector2)` | Modifient `Velocity`. |

### `CollisionSystem`
Détection par paires `O(n²)`, résolution par **Swept AABB** (calcule le temps d'impact le long du mouvement relatif, évite le tunneling à vitesse élevée). Gère : deux statiques (ignorés), statique+dynamique (le dynamique est repoussé au point de contact), deux dynamiques (répartition proportionnelle à la masse inverse). `CheckGrounded` détecte le sol par une marge verticale entre les deux `Bounds`. Triggers (`IsTrigger`) déclenchent `OnTriggerEnter` sans résolution physique.

### `CollisionInfo` (struct)
`Other`, `Normal`, `Penetration` (toujours `0`, jamais calculé dans le code actuel).

## Animation

- `AnimationFrame` (struct) : `SourceRect`, `Duration`.
- `Animation` : `Name`, `Frames[]`, `Loop`. `Animation.FromStrip(...)` génère les frames d'une bande spritesheet uniforme.
- `AnimationStateMachine` : dictionnaire nom → `Animation`, transitions (`From`, `To`, `Condition`). Teste les transitions à chaque frame (première vraie l'emporte), boucle ou marque `IsFinished`.

Utilisée par `Animator` (reconstruit la state machine depuis `Definitions`/`Transitions` éditées dans l'Inspector) et `SpriteRenderer`.

## UI (`KoreEngine.Components.UI`)

- **`UIElement`** (abstrait) : coordonnées de référence + `Canvas.ScaleX/Y` pour l'échelle écran réelle.
- **`UICanvas`** : `ReferenceWidth/Height` (1920×1080 par défaut), `ScaleX/Y` calculés depuis `SceneManager.ViewportWidth/Height`. `Add<T>` exige que le GameObject du Canvas soit déjà dans la scène.
- **`UIButton`** : hover/pressed via `SceneManager.ViewportMouseX/Y/InBounds` + `SDL.GetMouseState` directement (pas via `InputManager`).
- **`UIImage`** : dessine sans état d'interaction.

## Input

`InputManager` (statique, namespace `KoreEngine`) expose `IsKeyDown`/`IsKeyPressed`. `InputAction`/`InputAxis`/`InputMap` forment une couche optionnelle de liaison nommée, qui coexiste avec l'accès direct à `InputManager` (ex : `UIButton` interroge SDL directement pour la souris).

## Boucle de jeu buildé (`GameLoop`)

Distincte d'`EditorWindow` : zéro dépendance à ImGui/Editor, pas de mode édition, pas de `RenderTexture` intermédiaire — rendu direct à l'écran. `Run()` : input → resize → `ApplyPendingScene()` → `SceneManager.Update(dt)` (toujours, pas de notion de Playing/Paused) → `Clear()`/`Render()`/`Present()` directs.

## Pipeline de build joueur — état actuel, y compris incohérences

Voir `docs/hub-and-build.md` pour le détail complet. Résumé : il existe **trois implémentations distinctes** de "build joueur" dans le code (`KoreEngine.Engine.BuildService`, `KoreEngine.Hub.Services.BuildService`, `KoreEngine.Hub.Services.ProjectBuilder`), pas encore unifiées, et seule la première (bouton "Build" dans la toolbar de l'éditeur) est actuellement branchée à une UI.

## `RenderTexture`

Render-to-texture pour le viewport éditeur. `BeginRender()`/`EndRender()` flush le renderer et réinitialisent le clip rect avant et après chaque switch de render target. `Resize()` flush et débind avant de détruire/recréer la texture SDL.

## `TextureCache` / `FontManager`

- **`TextureCache`** : cache global chemin → texture SDL, échec de chargement mis en cache aussi. `ScanAssets(root)` liste récursivement les images.
- **`FontManager`** : wrapper SDL3_ttf, cache de polices par `(path, size)`. `DrawText` recrée une texture à chaque appel (pas de cache de texte) — à surveiller pour du texte affiché en boucle chaque frame.

## Types de base (`KoreEngine.Core`)

- **`Vector2`** (struct) : `+`/`-`/`*`/`/`, `Zero`/`One`/`NegativeOne`/`Up` (=`(0,-1)`)/`Down` (=`(0,1)`), `Length()`, `Normalize()`, `Lerp`/`LerpUnclamped`.
- **`Rectangle`** (struct) : `X, Y, Width, Height`, `Left/Right/Top/Bottom`, `Intersects`.
- **`Color`** (classe, pas struct) : `R, G, B` (int), pas de canal alpha stocké (passé en paramètre `byte a` par `Renderer`).
