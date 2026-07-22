# KoreEngine — Architecture

> Mis à jour à partir du code source complet (`KoreEngine.rar`). Remplace la version précédente basée uniquement sur les échanges de conversation.

## Vue d'ensemble

KoreEngine est un moteur de jeu 2D écrit en C#/.NET, avec un éditeur intégré façon Unity (component-based, hot-reload de scripts, scènes sérialisées).

**Stack technique :**
- **Rendu/fenêtrage** : SDL3 via le binding `edwardgushchin/SDL3-CS`
- **UI éditeur** : ImGui.NET, backend custom (`ImGuiBackend`) pontant ImGui vers SDL3
- **Scripting/hot-reload** : Roslyn (`Microsoft.CodeAnalysis.CSharp`) + `AssemblyLoadContext` collectible
- **Audio** : SDL3_mixer via le même binding
- **Police/texte** : SDL3_ttf via `FontManager`

## Structure réelle des projets

Contrairement à un unique projet `KoreEngine.csproj`, le moteur est aujourd'hui scindé en **deux projets bibliothèque distincts**, tous deux référencés par `KoreEngine.slnx` :

```
KoreEngine/
├── KoreEngine.slnx                         # référence les deux projets ci-dessous
├── KoreEngine.csproj                       # ⚠ voir note "projet orphelin" plus bas
├── KoreEngine.Runtime/
│   └── KoreEngine.Runtime.csproj           # Library — Engine/, Components/, Core/, Physics/, Input/, Animation/
└── KoreEngine.Editor/
    └── KoreEngine.Editor.csproj            # Library — Editor/, référence Runtime via <ProjectReference>
```

- `KoreEngine.Runtime.csproj` : `OutputType=Library`, référence directement les packages SDL3-CS (rendu, audio, texte).
- `KoreEngine.Editor.csproj` : `OutputType=Library`, référence `ImGui.NET` + `Microsoft.CodeAnalysis.CSharp`, et `KoreEngine.Runtime.csproj` via `<ProjectReference>` (SDL3-CS arrive transitivement, pas besoin de le redéclarer).

**Projet orphelin confirmé supprimable** : `KoreEngine.csproj` à la racine était un résidu de l'ancienne structure mono-projet d'avant le split Runtime/Editor. Confirmé sans effet — aucun fichier ni aucune solution ne le référence. À supprimer.

**Aucun point d'entrée (`Program.cs`/`Main`) n'a été trouvé** dans les fichiers extraits, ni dans `KoreEngine.Runtime` ni `KoreEngine.Editor`. Le lanceur de l'éditeur (probablement un projet exécutable séparé qui instancie `EditorWindow`) n'était pas inclus dans cette archive, ou vit ailleurs.

### `ProjectCreator` — confirmé à jour pour la structure à deux assemblies

`ProjectCreator.cs` référence déjà correctement les deux DLL séparément :
- `{projectName}.csproj` (le jeu/éditeur généré) référence **à la fois** `KoreEngine.Runtime.dll` et `KoreEngine.Editor.dll` en `HintPath`, avec les mêmes `PackageReference` (ImGui.NET, Roslyn, SDL3-CS) déclarées en propre — nécessaire car une référence binaire simple ne résout pas les dépendances transitives.
- `{projectName}.Scripts.csproj` (édition des scripts, IntelliSense) ne référence que `KoreEngine.Runtime.dll` + le package `SDL3-CS` managé (pas les variantes natives Windows, inutiles à l'édition) — un script de gameplay n'a jamais besoin d'ImGui ni du code Editor.
- `CopyEngineRuntimeDependencies` copie désormais les dépendances runtime depuis **les deux** dossiers de sortie (`KoreEngine.Runtime/bin/...` et `KoreEngine.Editor/bin/...`), en excluant les DLL/PDB du moteur lui-même de la copie (déjà gérées via `HintPath`).

**Point de conception à noter** : le `Program.cs` généré instancie directement `new EditorWindow(projectName, 1280, 720).Run()` — chaque projet généré par `ProjectCreator` **est son propre éditeur**, il n'y a pas de séparation entre "l'éditeur" et "le jeu" au niveau des projets générés (cohérent avec le fonctionnement observé : Play/Pause/Stop bascule un mode de simulation dans la même fenêtre, pas un exe distinct). `GameLoop` (boucle sans dépendance Editor, pour un jeu buildé/exporté) existe dans `KoreEngine.Runtime` mais **n'est utilisé nulle part dans le `Program.cs` généré actuellement** — il n'y a donc pas encore de chemin automatisé pour produire un vrai exécutable de jeu "release" (sans l'éditeur intégré). C'est cohérent avec l'item "Build Release" resté sur la liste des fonctionnalités à ajouter.

## Séparation Runtime / Editor

`KoreEngine.Runtime` ne référence jamais `KoreEngine.Editor` — dépendance à sens unique stricte, maintenant renforcée au niveau du système de projets (et plus seulement par convention de dossiers). `SceneManager` (Runtime) expose l'événement `OnSceneChanging`, consommé par `EditorSelection`/`EditorWindow` (Editor) pour réagir aux changements de scène sans que le Runtime ne connaisse l'existence de l'Editor.

## Distribution du moteur — confirmé

Un jeu généré par `ProjectCreator` référence le moteur en binaire uniquement (`<Reference><HintPath>` vers `KoreEngine.Runtime.dll` et `KoreEngine.Editor.dll`), jamais via `ProjectReference` vers les sources — aucun accès au code source du moteur depuis un projet généré. `CopyEngineRuntimeDependencies` copie les dépendances runtime (ImGui.NET.dll, binaires SDL3, etc.) depuis les deux dossiers de sortie du moteur, à côté de l'exe généré.

## Pipeline de hot-reload des scripts

**Composants impliqués :** `ScriptWatcher` → `ScriptCompiler` → `SceneManager` → `InspectorPanel`.

1. `ScriptWatcher` surveille `Assets/**/*.cs` via `FileSystemWatcher`, debounce de 1.5s (`BuildDelay`).
2. `ScriptCompiler.CompileAsync` compile avec Roslyn sur un thread de fond (`Task.Run`).
3. Succès → nouvel `AssemblyLoadContext` collectible (`ScriptsContext`), l'ancien passe en attente (`_pendingUnloadContext`), pas déchargé immédiatement.
4. `OnCompileSuccess` (sur le thread de fond) : sauvegarde de la scène courante, préparation du rechargement (`LoadSceneFromFile` → remplit `next`).
5. Sur le **thread principal** : `ApplyPendingScene()` bascule `current = next`.
6. Juste après : `ScriptWatcher.FinalizeUnloadIfPending()` → `ScriptCompiler.FinalizeUnload()` tente de décharger l'ancien contexte (`GC.Collect()` en boucle, jusqu'à 10 tentatives, avertissement loggé si échec persistant).

**⚠️ Bug constaté — branche d'échec de compilation vide.** Dans `ScriptCompiler.Compile()`, la branche `else` (compilation échouée) ne contient plus que :
```csharp
else
{
    // ... inchangé
}
```
C'est un commentaire placeholder resté en place, **sans aucun code réel derrière**. Conséquences concrètes :
- `OnCompileError` n'est jamais invoqué → `ScriptWatcher.lastError` ne se repeuple jamais après une erreur (il se vide bien au changement de fichier suivant, mais ne se remplit plus).
- Aucun `Logger.Error(...)` n'est appelé → les erreurs de compilation **n'apparaissent plus du tout dans la Console**, et la fonctionnalité "double-clic pour ouvrir à la ligne fautive" (`ExternalEditor.OpenFileAtLine`) est de facto inatteignable puisqu'aucune entrée n'est créée pour en déclencher l'usage.
- `Status` reste sur son dernier état ("Compilation en cours...") au lieu d'indiquer un nombre d'erreurs.

Ce n'est presque certainement pas voulu — à corriger en restaurant l'itération sur `result.Diagnostics` (extraction fichier/ligne via `diag.Location.GetLineSpan()`, `Logger.Error(message, file, line)`, puis `OnCompileError?.Invoke(...)`).

**Cas particulier — suppression du dernier script :** confirmé, `Compile()` n'a plus de retour anticipé sur `csFiles.Count == 0` — le fix tient bien dans le code actuel.

**`InspectorPanel.componentTypes`** : cache invalidé sur `OnCompileSuccess`, dédoublonné par nom (`GroupBy(t => t.Name).Select(g => g.Last())`) — confirmé présent tel quel.

## Système de scènes

**Format `.kscene`** : inchangé (blocs à accolades, Position/Rotation/Scale sérialisés en `InvariantCulture`, références `@obj_X`/`@obj_X:Type`).

**`SceneManager` (Runtime)** — points notables confirmés dans le code actuel :
- `AssetsDirectory` par défaut = `AppContext.BaseDirectory/Assets` (à côté de l'exécutable) — convient à un jeu buildé ; l'Editor écrase cette valeur au démarrage avec le vrai chemin du projet.
- `LoadSceneFromFile` **sauvegarde automatiquement la scène courante avant de charger la nouvelle**, si une scène est déjà chargée (`if (current != null) SaveCurrentScene();`) — comportement qui n'avait pas été discuté explicitement auparavant, à garder en tête : changer de scène sauvegarde toujours l'ancienne au préalable, sans confirmation.
- L'événement s'appelle `OnSceneChanging` (pas `OnSceneChanged`) — déclenché à la fois par `LoadSceneFromFile` et `UnloadScene`.
- Plus aucune référence à `GameWindow`/`EditorWindow` dans `SceneManager` — entièrement découplé, cohérent avec la séparation Runtime/Editor stricte des deux projets.

**`Scene`** — `Start()` (pas `OnStart()`) appelle `c.Start()` sur tous les composants ; voir section Component ci-dessous pour le renommage. Le reste (trois passes d'`Update`, `DestroyAll`/`DestroyRecursive`, `RefreshColliders`) est confirmé identique à ce qui avait été mis en place.

## Cycle de vie des composants — renommage `OnStart` → `Start`

Dans le code actuel, `Component.OnStart()` a été renommé en **`Component.Start()`**. `Scene.Start()`, `SceneManager.NotifyStart()` (`current?.Start()`), et `AudioSource.Start()` (override) utilisent tous ce nouveau nom. Toute référence à `OnStart()` dans une documentation ou un script plus ancien doit être mise à jour vers `Start()`.

`Component.OnDestroy()` reste inchangé (nom conservé), appelé par `GameObject.RemoveComponent` et `Scene.DestroyAll()`/`DestroyRecursive`.

## Rendu et convention de coordonnées

Confirmé inchangé : Y-haut en espace monde, conversion exclusivement dans `Camera.WorldToScreen`/`ScreenToWorld`, pivot centré sur `WorldPosition` pour `RectRenderer`/`SpriteRenderer`/`Collider.Bounds` (avec le point d'attention `pos + h/2` plutôt que `pos - h/2` en Y-haut).

**Limite non résolue, présente dans le code actuel** : `PhysicsBody.Update()` et `CollisionSystem` écrivent la position via `Owner.Position` (l'alias qui pointe vers `LocalPosition`), pas `Owner.WorldPosition`. Pour un objet **sans parent**, ça ne change rien (Local == World). Mais pour un objet physique **enfant d'un autre objet**, la résolution de collision et l'intégration de vélocité ignorent la position du parent — même classe de bug que celle corrigée dans `RectRenderer`/`Collider.Bounds` il y a plusieurs sessions, mais pas encore appliquée ici. À signaler si tu comptes utiliser des `PhysicsBody` sur des objets enfants.

## Physique (`KoreEngine.Physics`, `KoreEngine.Components.PhysicsBody`)

### `PhysicsBody`
| Membre | Rôle |
|---|---|
| `Vector2 Velocity` | |
| `bool IsStatic` | Un corps statique n'intègre ni vélocité ni gravité, sert de mur/sol fixe pour la résolution. |
| `float GravityScale` | Multiplicateur appliqué à `GlobalGravity` (statique, `800f` par défaut, partagé par tous les corps). |
| `float Friction` | Amortissement exponentiel de `Velocity.X` (`Pow(1 - Friction, dt)`). |
| `float Mass` | Utilisé pour la répartition d'impulsion entre deux corps dynamiques en collision. |
| `float MaxFallSpeed` | Clamp de `Velocity.Y` quand `GravityScale > 0`. |
| `bool IsGrounded` | Reset à `false` en début de frame, rétabli par `CollisionSystem`. |
| `ApplyForce(Vector2)` / `ApplyImpulse(Vector2)` | Modifient `Velocity` (force divisée par `Mass`, impulsion directe). |

`Update(dt)` : si au sol et vélocité verticale positive → reset à 0 ; reset `IsGrounded` ; intègre gravité (clampée) ; applique friction ; déplace `Owner.Position` (alias `LocalPosition` — voir limite ci-dessus).

### `CollisionSystem`
Détection par paires `O(n²)` sur tous les `Collider` enregistrés, via **Swept AABB** (`SweptAABB`) — calcule le temps d'impact `tFirst`/`tLast` le long du mouvement relatif entre deux `Rectangle`, plutôt qu'un simple test d'overlap statique (évite le tunneling à vitesse élevée). Gère séparément : deux corps statiques (ignorés), un statique + un dynamique (le dynamique est repoussé au point de contact), deux dynamiques (répartition proportionnelle à la masse inverse). `CheckGrounded` détecte le sol par une marge verticale de quelques pixels entre les deux `Bounds`. Triggers (`IsTrigger`) déclenchent `OnTriggerEnter` sans résolution physique.

### `CollisionInfo` (struct)
`Other` (le `Collider` opposé), `Normal` (direction de la collision), `Penetration` (non calculé actuellement — toujours `0` dans le code actuel, malgré le champ existant).

## Animation (`Animation`, `AnimationFrame`, `AnimationStateMachine`)

- `AnimationFrame` (struct) : `SourceRect` (zone dans la spritesheet), `Duration` (secondes).
- `Animation` : `Name`, `AnimationFrame[] Frames`, `Loop`. `Animation.FromStrip(...)` génère les frames d'une bande uniforme (ligne, colonne de départ, nombre de frames, taille de frame).
- `AnimationStateMachine` : dictionnaire nom → `Animation`, liste de transitions (`From`, `To`, `Func<bool> Condition`). `Update(dt)` avance le timer de la frame courante, teste les transitions applicables à l'état courant à chaque frame (la première dont la condition est vraie l'emporte), boucle ou marque `IsFinished` en fin d'animation non-loopée.

Utilisée par `Animator` (construit la state machine depuis `Definitions`/`Transitions` éditées dans l'Inspector) et `SpriteRenderer` (lit `StateMachine.CurrentFrame` pour le rectangle source à dessiner).

## UI (`KoreEngine.Components.UI`)

- **`UIElement`** (abstrait, hérite de `Component`) : `X, Y, Width, Height` en coordonnées de référence (résolution logique du Canvas), `Visible`, référence à son `UICanvas`. Expose `ScreenX/Y/Width/Height` = valeurs mises à l'échelle par `Canvas.ScaleX/Y`. `Render()` délègue à `Draw(Renderer)` (abstrait) si `Visible`.
- **`UICanvas`** : `ReferenceWidth/Height` (résolution de conception, 1920×1080 par défaut), `ScaleX/Y` calculés dynamiquement depuis `SceneManager.ViewportWidth/Height` — permet une UI qui s'adapte à la résolution réelle du viewport. `Add<T>(element)` crée un `GameObject` enfant du Canvas et y attache l'élément — **le GameObject du Canvas doit déjà être ajouté à la scène** avant d'appeler `Add`, sinon exception explicite.
- **`UIButton`** : `TextureNormal/Hover/Pressed` (+ chemins), `ActionType` (`None`/`LoadScene`/`Quit`, sérialisable) + `ActionParam`, callback `OnClick` non sérialisable (`[HideInInspector]`). Détection hover/pressed via `SceneManager.ViewportMouseX/Y/InBounds` et `SDL.GetMouseState` directement (pas via `InputManager`).
- **`UIImage`** : `Texture`/`TexturePath`, dessine directement sans état d'interaction.

## Input (`KoreEngine.Input`, `KoreEngine.Engine.InputManager`)

- **`InputManager`** (statique, namespace `KoreEngine` — pas `KoreEngine.Engine` malgré l'emplacement du fichier) : `keysDown`/`keysPressed` (HashSet de `SDL.Keycode`). `IsKeyDown` (maintenue), `IsKeyPressed` (une frame, vidé par `NewFrame()`).
- **`InputAction`** : liaison nommée de touches à un booléen logique — `BindKey` (test via `IsDown`), `BindKeyPressed` (test via `IsPressed`), plusieurs touches possibles par action (`Any`).
- **`InputAxis`** : axe 1D (`BindKeys(neg, pos)`) ou 2D (`BindKeys(up, down, left, right)`), retourne un `Vector2` normalisé ou non.
- **`InputMap`** : registre nommé d'`InputAction`/`InputAxis`, façade d'accès (`IsDown(name)`, `IsPressed(name)`, `GetAxis(name)`).

Ce système (Action/Axis/Map) coexiste avec l'accès direct à `InputManager.IsKeyDown/IsKeyPressed` déjà utilisé ailleurs (ex. `UIButton` interroge SDL directement pour la souris) — pas une couche obligatoire, plutôt une commodité optionnelle pour du gameplay qui préfère nommer ses contrôles.

## Boucle de jeu buildé (`GameLoop`)

Distincte d'`EditorWindow` : zéro dépendance à ImGui/Editor, pas de mode édition, pas de `RenderTexture` intermédiaire — rendu direct à l'écran avec la `Camera` de la scène. `Run()` : input → resize → `ApplyPendingScene()` → `SceneManager.Update(dt)` (toujours, pas de notion de Playing/Paused) → `Clear()`/`Render()`/`Present()` directs. C'est ce que consomme le `Program.cs` généré par `ProjectCreator` pour un jeu exporté (par opposition à `EditorWindow.Run()` pour l'éditeur).

## Rendu — types de base (`KoreEngine.Core`)

- **`Vector2`** (struct) : opérateurs `+`, `-` (unaire et binaire), `*`/`/` scalaire ; `Zero`, `One`, `NegativeOne`, `Up` (= `(0,-1)`), `Down` (= `(0,1)`) ; `Length()`, `Normalize()`, `Lerp`/`LerpUnclamped`.
- **`Rectangle`** (struct) : `X, Y, Width, Height`, `Left/Right/Top/Bottom` calculés, `Intersects(other)`.
- **`Color`** (classe, pas struct) : `R, G, B` (int), constantes `Black/White/Red/Green/Blue`. Pas de canal alpha (géré séparément en paramètre par les méthodes `Renderer.DrawRect(..., byte a)`).

## `RenderTexture`

Render-to-texture pour le viewport éditeur. `BeginRender()`/`EndRender()` flush le renderer et réinitialisent le clip rect **avant et après** chaque switch de render target — corrige un bug historique de glitches visuels où un clip rect ou des commandes en attente d'un draw précédent "fuitaient" sur la texture suivante. `Resize()` flush et débind avant de détruire/recréer la texture SDL (une texture ne doit jamais être détruite pendant qu'elle est bound comme target).

## `TextureCache` / `FontManager`

- **`TextureCache`** : cache global `chemin → IntPtr` de texture SDL, échec de chargement mis en cache aussi (`IntPtr.Zero`) pour ne pas retenter à chaque frame. `ScanAssets(root)` liste récursivement les images sous un dossier (extensions `.png/.bmp/.jpg/.jpeg`).
- **`FontManager`** : wrapper SDL3_ttf, cache de polices par `(path, size)`. `DrawText` rend le texte via une surface temporaire (`TTF.RenderTextBlended`) convertie en texture, dessinée puis immédiatement détruite — pas de cache de texture de texte (recrée une texture à chaque appel, potentiellement coûteux pour du texte affiché en boucle chaque frame ; à surveiller si utilisé abondamment).

## Systèmes encore non vus [à compléter]

- Contenu détaillé de `ViewportPanel.cs`, `ProjectPanel.cs`, `InspectorPanel.cs`, `ImGuiBackend.cs`, `ExternalEditor.cs`, `ConsolePanel.cs`, `EditorIcons.cs`, `HierarchyPanel.cs` a été vérifié par sondage (recherche de divergences spécifiques) plutôt que relu intégralement ligne par ligne.
- Pas de projet/template pour un build "release" utilisant `GameLoop` (voir note dans la section `ProjectCreator` ci-dessus) — reste à faire.
