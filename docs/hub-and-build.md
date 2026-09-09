# KoreEngine — Hub et pipeline de build

## Le Hub (`KoreEngine.Hub`)

Application WPF, style Unity Hub : liste de projets récents, création, import, lancement. Ne référence ni `KoreEngine.Runtime.dll` ni `KoreEngine.Editor.dll` — il scaffolde des fichiers texte et pilote `dotnet` en sous-processus.

### Détection du moteur (`EngineLocator`)
`AutoDetect()` remonte l'arborescence depuis `AppContext.BaseDirectory` à la recherche de `KoreEngine.slnx`. `IsValidEngineDir(dir)` vérifie la présence de `KoreEngine.Runtime.csproj` et `KoreEngine.Editor.csproj`. Le dossier retenu est mémorisé dans `HubSettingsStore` (JSON local) ; sinon l'utilisateur est invité à le sélectionner manuellement (`OpenFolderDialog`).

### Création de projet (`ProjectScaffolder.Create`)
Vérifie d'abord que `KoreEngine.Runtime.dll` et `KoreEngine.Editor.dll` existent déjà compilées (`bin/Debug/net10.0/`) — sinon exception explicite demandant de compiler le moteur au préalable. Génère ensuite, dans le dossier cible :

| Fichier | Rôle |
|---|---|
| `Program.cs` | `new EditorWindow(name, 1280, 720).Run()` |
| `{name}.csproj` | Jeu/éditeur — référence `KoreEngine.Runtime.dll` + `KoreEngine.Editor.dll` en `HintPath`, exclut `Assets/**/*.cs` et `Player/**/*.cs` de la compilation (scripts compilés séparément par Roslyn ; `Player/` a son propre `Main`) |
| `{name}.Scripts.csproj` | Édition des scripts avec IntelliSense (référence uniquement `Runtime.dll`, pas d'ImGui/Editor) |
| `{name}.sln` | Contient les trois projets : jeu, Scripts, Player |
| `Player/Program.cs` + `Player/{name}.Player.csproj` | Projet headless (`GameLoop`, aucune dépendance ImGui/Roslyn) — voir plus bas |
| `imgui.ini`, icônes éditeur | Copiés depuis le moteur |

`CopyEngineDependencies` copie ensuite les DLL dépendantes (ImGui.NET, binaires natifs SDL3, Roslyn, etc.) à côté de l'exe généré — une référence `HintPath` ne résout pas les dépendances transitives comme le ferait un `PackageReference`.

### Resynchronisation des dépendances
`RefreshDependencies` (menu contextuel "Refresh Dependencies" dans le Hub) et `EditorLauncher.SyncEngineDependencies` (appelée **avant chaque lancement**, pas juste à la création) refont cette copie — utile après avoir ajouté une dépendance au moteur (ex : `Microsoft.CodeAnalysis.CSharp` a été ajoutée à `KoreEngine.Runtime` après coup).

### Lancement d'un projet (`EditorLauncher.OpenProject`)
1. Resynchronise les dépendances moteur.
2. `dotnet clean` sur la `.sln` (échec non bloquant — juste loggé).
3. `dotnet build` sur la `.sln` (échec bloquant, annule le lancement).
4. Lance l'exe généré (`bin/Debug/net10.0/{name}.exe`).

Systématique à chaque ouverture — garantit qu'on ne tourne jamais avec des `KoreEngine.Runtime/Editor.dll` périmées ni des binaires de script résiduels. `DotnetRunner` (partagé) exécute la commande en process caché et streame stdout/stderr ligne par ligne vers `BuildLogWindow` (fenêtre de logs affichée pendant le clean/build).

## Pipeline de build joueur — état actuel

**Il existe trois implémentations distinctes de "build joueur" dans le code, non encore unifiées :**

### 1. `KoreEngine.Engine.BuildService` (Runtime) — celle qui est réellement branchée
Appelée par le bouton **Build** de la toolbar de l'éditeur (`EditorWindow.cs`). Publie `Player/{name}.Player.csproj` en `Release`, `win-x64`, **self-contained: true**, dans `{project}/Build/`, puis copie `Assets/` à côté (en excluant explicitement les fichiers `.cs`).

### 2. `KoreEngine.Hub.Services.BuildService` — définie mais non branchée à l'UI
Publie la même `Player/{name}.Player.csproj`, mais **self-contained: false**, sans `PublishSingleFile`. Aucun bouton du Hub ne l'appelle actuellement (recherché dans `MainWindow.xaml.cs` : aucune référence).

### 3. `KoreEngine.Hub.Services.ProjectBuilder` — approche plus ancienne, également non branchée
Génère un projet `Player.csproj` **éphémère** dans `.player-build/` (différent du sous-dossier permanent `Player/` scaffoldé par `ProjectScaffolder`), publie, puis copie `Assets/` + `{name}.Scripts.dll` (déjà compilée par la solution normale) à côté de l'exe. Contrairement aux deux `BuildService`, ce chemin **fonctionne réellement** pour charger les scripts, car `{name}.Scripts.dll` existe bel et bien dès qu'on a ouvert le projet une fois dans l'éditeur.

### Incohérence à connaître : chargement des scripts dans le Player publié
Le `Player/Program.cs` généré par `ProjectScaffolder.WritePlayerProject` cherche à charger `GameScripts.dll` :

```csharp
string scriptsDllPath = Path.Combine(AppContext.BaseDirectory, "GameScripts.dll");
if (File.Exists(scriptsDllPath))
    Assembly.LoadFrom(scriptsDllPath);
```

**Mais rien dans le code actuel ne produit un fichier nommé `GameScripts.dll`** (recherché dans tout le dépôt — seule occurrence, dans ce commentaire et cette ligne). Ni `KoreEngine.Engine.BuildService` (le chemin réellement branché au bouton Build) ni `KoreEngine.Hub.Services.BuildService` ne compilent ni ne copient un tel fichier. Le commentaire de `KoreEngine.Hub.Services.BuildService` évoque même une intention différente ("les scripts sont copiés en `.cs`, recompilés au lancement via Roslyn"), qui ne correspond pas non plus au `Program.cs` généré (qui n'appelle jamais `ScriptCompiler`).

**Conséquence concrète** : un build produit via le bouton "Build" de l'éditeur donne un exécutable qui démarre, charge la scène de démarrage, mais **n'a aucun de tes composants de script utilisateur enregistrés** — `SceneSerializer.FindType` ne les retrouvera pas par réflexion à la désérialisation. Seul `ProjectBuilder.cs` (non branché à une UI pour l'instant) résout correctement ce point, en copiant la vraie `{name}.Scripts.dll`.

À corriger dans une prochaine session, par exemple :
- soit adapter `Player/Program.cs` pour charger `{name}.Scripts.dll` (nom réel produit par la solution) au lieu du nom générique `GameScripts.dll` inexistant,
- soit faire produire par `BuildService` une copie/renommage de `{name}.Scripts.dll` vers `GameScripts.dll` dans le dossier de sortie.

### Différences self-contained entre les deux `BuildService`
`KoreEngine.Engine.BuildService` (branchée) publie en `--self-contained true` (l'exe embarque le runtime .NET, ~60-100 Mo de plus, mais tourne sans installation préalable). `KoreEngine.Hub.Services.BuildService` (non branchée) publie en `--self-contained false`. À trancher/unifier avant de brancher l'un ou l'autre définitivement.

### Pas de trimming
Aucun des chemins de build n'active `PublishTrimmed` : `SceneSerializer` et `ScriptCompiler` s'appuient massivement sur la réflexion (`Activator.CreateInstance`, scan de tous les types chargés) pour retrouver les composants de scripts utilisateur compilés dynamiquement — le trimming casserait ça silencieusement sans configuration explicite des racines.

### `ProjectSettings.StartupScene`
Le `Player/Program.cs` scaffoldé lit `ProjectSettings.StartupScene` (voir `architecture.md`) ; à défaut, prend la première scène trouvée par ordre alphabétique (`SceneManager.SceneFiles().FirstOrDefault()`) ; sinon lève une exception explicite ("Aucune scène trouvée"). `ProjectBuilder.cs` (le chemin non branché) prend une approche différente : le nom de la scène de démarrage est passé en paramètre direct à `Build(...)`, pas lu depuis `ProjectSettings`.
