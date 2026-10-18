# KoreEngine — Hub et pipeline de build

> Mis à jour à partir du code source complet le plus récent (archive du 18/10/2026). Remplace la version qui décrivait un Hub WPF avec bouton *Import* et menu « Refresh Dependencies » : le Hub a été **réécrit en SDL**.

## Le Hub (`KoreEngine.Hub`)

Application **SDL3 pure** (SDL3, SDL3_image, SDL3_ttf), sans WPF ni ImGui : un mini-toolkit d'interface maison dessine directement sur un `SDL_Renderer`. Fenêtre redimensionnable (960×540, taille minimale), décorations natives de l'OS, police monospace embarquée (`Fonts/RobotoMono-VariableFont_wght.ttf`, copiée à côté de l'exécutable). Les paquets natifs Windows et Linux sont tous deux déclarés dans le `.csproj`. Le Hub ne référence ni `KoreEngine.Runtime.dll` ni `KoreEngine.Editor.dll` : il scaffolde des fichiers texte et pilote `dotnet` en sous-processus.

### Écran principal

- **Liste de projets** (`ProjectList`) : une ligne par projet récent — nom et chemin, version du moteur, taille du dossier, date de dernière ouverture. Défilement à la molette, sélection au clic, **ouverture au double-clic**. Un projet dont le dossier a disparu est retiré automatiquement de la liste. Taille et version sont calculées en tâche de fond.
- **Nouveau projet** et **Ouvrir** (activé quand un projet est sélectionné). Il n'y a pas, dans cette version, de bouton d'import d'un projet existant : la resynchronisation des dépendances se fait automatiquement à chaque ouverture (voir plus bas).
- Une ligne de statut affiche le résultat de la dernière action.

### Architecture de l'UI

`HubWindow` (fenêtre, boucle, file d'attente du thread principal) possède un `UiContext` (renderer, `TextCache`, `ImageCache`, entrées). Widgets (`Widget` : position, taille, `Visible`, `Enabled`, `Update`, `Draw`) : `Label`, `ImageBox`, `Button` (styles primaire/secondaire, événement `Click`), `TextInput` (une ligne ; Retour arrière, Suppr, flèches, Début/Fin, Ctrl+A, Ctrl+V ; suppose une police monospace), `ProjectList`, `NewProjectDialog`, `LoadingDialog`. Les opérations longues (création, build) tournent hors du thread de rendu et reviennent par `RunOnMain`.

### Stockage local

Dans le dossier `ApplicationData` de l'utilisateur, sous `KoreEngine/` :

- `hub_settings.json` (`HubSettingsStore`) : `EngineDir` (dossier du moteur) et `LastProjectsDir` (dernier dossier parent choisi).
- `recent_projects.json` (`RecentProjectsStore`) : liste des projets récents ; un fichier corrompu donne une liste vide.

### Détection du moteur (`EngineLocator`)

`EngineDir` des réglages, sinon `AutoDetect()` : remonte l'arborescence depuis `AppContext.BaseDirectory` à la recherche de `KoreEngine.slnx`. `IsValidEngineDir` vérifie la présence de `KoreEngine.Runtime.csproj` et `KoreEngine.Editor.csproj`. Si rien n'est trouvé, la création de projet affiche « Dossier du moteur introuvable ».

### Création de projet (`NewProjectDialog` → `ProjectScaffolder.Create`)

La boîte de dialogue demande un nom (identifiant C# valide `[A-Za-z_][A-Za-z0-9_]*`) et un dossier parent (bouton **Browse** → sélecteur de dossier natif). La création ne lance **pas** l'éditeur : le projet est ajouté à la liste, à ouvrir ensuite.

`Create` vérifie que `KoreEngine.Runtime.dll` et `KoreEngine.Editor.dll` existent déjà compilées (dossier de sortie résolu par `ResolveEngineOutputDir`, qui préfère Release à Debug) et que le dossier cible est vide, puis génère :

| Fichier | Rôle |
| --- | --- |
| `icon.ico` | Copié depuis le moteur. |
| `Program.cs` | `new EditorWindow(name, 1280, 720).Run()` |
| `{name}.csproj` | Jeu/éditeur — référence `KoreEngine.Runtime.dll` + `KoreEngine.Editor.dll` en `HintPath` ; SDL3-CS conditionné par l'OS (`SdlPackages.PlatformItemGroups`) ; exclut `Assets/**/*.cs` et `Player/**/*.cs` de la compilation (scripts compilés par Roslyn ; `Player/` a son propre `Main`). |
| `{name}.Scripts.csproj` | Édition des scripts avec IntelliSense (référence uniquement `Runtime.dll`). Produit `{name}.Scripts.dll`. |
| `{name}.sln` | Les trois projets : jeu, Scripts, Player. |
| `Player/Program.cs` + `Player/{name}.Player.csproj` | Projet headless (`GameLoop`, aucune dépendance ImGui) — voir plus bas. |
| `imgui.ini`, `Editor/Icons/*.png` | Copiés depuis le moteur (`Icons/` : `play`, `stop`, `pause`, `step`, `camera`, icônes de fichiers…). |

`CopyEngineDependencies` copie ensuite les DLL dépendantes (ImGui.NET, Roslyn, …) et les binaires natifs de l'OS courant (à la racine du dossier de sortie, à côté de l'exe) dans `bin/Debug/net10.0`.

**⚠ Dossier `Packages/`** : `ProjectScaffolder.WritePackages` existe (elle copie le dossier `Packages/` situé à la racine du moteur vers `{projet}/Packages/`), mais **elle n'est pas appelée par `Create`** dans cette archive : il faut ajouter `WritePackages(engineDir, projectPath, Log);` à la suite de `WriteEditorIcons(...)`. Tant que ce n'est pas fait, un nouveau projet n'a pas de `Packages/` (le panneau Projet en crée un vide à l'ouverture). Le dossier `Packages/` du moteur n'existe pas non plus dans l'archive.

### Lancement d'un projet (`EditorLauncher.OpenProject`)

Exécuté hors du thread de rendu, avec une fenêtre de chargement (`LoadingDialog` : nom du projet, étape en cours, barre indéterminée, temps écoulé, dernières lignes de log) :

1. **Synchronisation des dépendances** : `ProjectScaffolder.RefreshDependencies` (copie les DLL du moteur, à jour, dans le projet — et dans `Player/` s'il existe). Ignorée si le dossier moteur est inconnu.
2. `dotnet clean` sur la `.sln` (échec non bloquant).
3. `dotnet build -c Debug` sur `{name}.Scripts.csproj` (échec bloquant).
4. `dotnet publish -c Debug -r {rid} --self-contained true -o bin/Debug/net10.0` sur `{name}.csproj` — on ne fait **pas** `dotnet build` sur la `.sln` avec un RID (NETSDK1134) et un simple `build` peut laisser l'apphost dépendant du framework. Le RID est `win-x64`/`win-arm64`/`linux-x64`/`linux-arm64`, sinon `PlatformNotSupportedException`.
5. **Lancement** de `bin/Debug/net10.0/{name}[.exe]`, sortie redirigée. Si le processus se termine avec une erreur dans les 4 s, il est considéré comme planté au démarrage : la fenêtre de chargement affiche le **début** de sa sortie (une exception nomme souvent la DLL manquante). Sinon, la date de dernière ouverture est mise à jour.

Cette séquence est systématique : on ne tourne jamais avec des `KoreEngine.Runtime/Editor.dll` périmées ni des binaires de script résiduels. `DotnetRunner` exécute chaque commande en process caché et streame stdout/stderr vers la fenêtre de chargement.

## Pipeline de build joueur — état actuel

**Trois implémentations de « build joueur » coexistent, non unifiées.** Toutes copient maintenant `Assets/` **et** `Packages/` à côté de l'exécutable (ce qui est nécessaire : un jeu buildé lit ses ressources relativement à son propre exe, et `TextureCache.Resolve` ré-enracine les chemins `/Assets/…` et `/Packages/…` sur ces dossiers).

### 1. `KoreEngine.BuildService` (Runtime) — celle qui est branchée

Appelée par le bouton **Build** de la toolbar de l'éditeur. Publie `Player/{name}.Player.csproj` en `Release`, `--self-contained true`, pour le RID de la machine, dans `{projet}/Build/`, puis copie `Assets/` et `Packages/` (en excluant les fichiers `.cs`). Un build à la fois (`IsBuilding`).

### 2. `KoreEngine.Hub.Services.BuildService` — non branchée à l'UI

Même projet `Player`, mais `--self-contained false`, `PublishSingleFile=false`. Plus aucun bouton du Hub ne l'appelle.

### 3. `KoreEngine.Hub.Services.ProjectBuilder` — non branchée

Génère un `Player.csproj` éphémère dans `.player-build/`, publie en Release self-contained, puis copie `Assets/`, `Packages/` et `{name}.Scripts.dll` (+ `.pdb`). Son `Program.cs` généré charge `{name}.Scripts.dll` (le nom réel), crée le `GameLoop` **avant** de charger la scène, et initialise/ferme `AudioManager`. C'est le seul chemin qui charge correctement les scripts utilisateur.

### Le `Player/Program.cs` généré par le scaffolder

```csharp
SceneManager.ScanAndRegisterScenes();
ProjectSettings.Load();

var loop = new GameLoop("{name}", 1280, 720);   // AVANT le chargement de la scène

if (startupScene != null)      SceneManager.LoadScene(startupScene);
else if (firstScene != null)   SceneManager.LoadSceneFromFile(firstScene);
else throw new Exception("Aucune scène trouvée dans Assets/ — impossible de démarrer.");

AudioManager.Init();
loop.Run();
AudioManager.Quit();
```

L'ordre compte : `GameLoop` initialise `TextureCache` ; charger la scène avant lui laisserait toutes les textures à zéro (sprites, boutons et images invisibles, sans erreur). La scène de démarrage vient de `ProjectSettings.StartupScene` (défini dans l'éditeur via *Set as Startup Scene*), sinon de la première scène par ordre alphabétique.

### Comportement du jeu buildé (`GameLoop`)

`GameLoop.UpdateViewportState()` renseigne chaque frame `SceneManager.ViewportWidth/Height`, la souris (`ViewportMouseX/Y/InBounds`) et `ViewWidth/ViewHeight` de la caméra de la scène à la taille de la fenêtre. Sans cela, l'UI (`UICanvas.ScaleX = 0`) ne s'afficherait pas. Voir `docs/architecture.md`.

### self-contained : deux choix différents

`KoreEngine.BuildService` (branchée) publie en `self-contained true` (l'exe embarque le runtime .NET, nettement plus gros, mais tourne sans installation) ; `Hub.Services.BuildService` publie en `false`. À trancher avant d'en brancher un autre.

### Pas de trimming

Aucun chemin n'active `PublishTrimmed` : `SceneSerializer` et `ScriptCompiler` s'appuient sur la réflexion (`Activator.CreateInstance`, scan des types chargés) ; le trimming les casserait silencieusement sans configuration explicite.

### Chemins de ressources

Le sélecteur de texture enregistre un chemin **absolu** dans la scène. `TextureCache.Resolve` le ré-enracine sur `Assets/` ou `Packages/` du dossier courant (projet dans l'éditeur, dossier de l'exe dans un jeu buildé), ce qui fait fonctionner un build copié sur une autre machine. Les chemins relatifs commençant par `Packages/` ou `Assets/` fonctionnent aussi. Les clips audio (`AudioSource.ClipPath`) ne passent pas par ce résolveur.
