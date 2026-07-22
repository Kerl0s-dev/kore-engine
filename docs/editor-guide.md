# KoreEngine — Guide de l'éditeur

## Créer un projet

```
ProjectCreator.exe "D:\KoreEngine" "D:\Projects\" "MonJeu"
```

- 1er argument : racine du moteur (contient `KoreEngine.Runtime/` et `KoreEngine.Editor/`, chacun avec son propre `.csproj`, tous deux **déjà compilés** au préalable — `dotnet build` sur `KoreEngine.slnx`).
- 2e argument : dossier parent du nouveau projet.
- 3e argument : nom du projet.

Génère `MonJeu.csproj` (référence `KoreEngine.Runtime.dll` **et** `KoreEngine.Editor.dll` en binaire), `MonJeu.Scripts.csproj` (édition des scripts, référence uniquement `Runtime.dll`), `MonJeu.sln` (contient les deux), un `Program.cs` minimal (`new EditorWindow("MonJeu", 1280, 720).Run()`), copie `imgui.ini` et les icônes éditeur, et copie les dépendances runtime des deux assemblies moteur — aucune référence au code source du moteur nulle part.

Chaque projet généré **est son propre éditeur** : il n'y a pas de séparation entre "éditeur" et "jeu buildé" à ce stade — le bouton Play/Stop bascule un mode de simulation dans la même fenêtre. Un vrai export "release" (exécutable sans l'éditeur intégré, via `GameLoop`) n'est pas encore automatisé.

Ouvre `MonJeu.sln` dans Visual Studio pour éditer les scripts avec IntelliSense complet sur l'API du moteur.

## Interface générale

- **Hierarchy** (gauche) : arbre des `GameObject` de la scène courante, avec le nom de la scène affiché en en-tête.
- **Inspector** (droite) : détail de l'objet sélectionné — Transform (Position/Rotation/Scale), liste des composants, bouton Add Component.
- **Project** (bas gauche) : navigateur de fichiers `Assets/`, arbre de dossiers + grille de fichiers avec icônes.
- **Console** (bas droite) : logs filtrables par niveau (Info/Warning/Error/Success), avec recherche texte. Les erreurs de compilation de script sont cliquables (double-clic → ouvre Visual Studio à la ligne exacte).
- **Viewport** (centre) : rendu de la scène, gizmos, sélection par clic.

## Toolbar

Barre horizontale sous la menu bar : icônes Play / Stop / Pause / Step, centrées.

- **Play** : sauvegarde la scène courante puis démarre la simulation.
- **Stop** : recharge la scène depuis son état sauvegardé, remet Playing/Pause à faux.
- **Pause** : suspend `Update()` sans arrêter le rendu — la scène reste visible et inspectable/modifiable, comme en mode édition.
- **Step** : disponible uniquement en pause, avance la simulation d'exactement une frame.

## Raccourcis clavier

| Raccourci | Action |
|---|---|
| `Ctrl+R` | Reset Camera (position + zoom de l'`EditorCamera`) |
| `Ctrl+S` | Sauvegarder la scène courante |
| `W` | Gizmo Move |
| `E` | Gizmo Rotate |
| `R` | Gizmo Scale |

La liste complète et à jour est aussi visible dans le menu **Help > Keyboard Shortcuts**.

## Viewport — navigation caméra

- **Clic molette + drag** : pan (déplacement de la caméra éditeur).
- **Ctrl + molette** : zoom, centré sur la position de la souris (le point sous le curseur reste fixe à l'écran).
- **Ctrl+R** : réinitialise la caméra.

## Sélection d'objets

- **Clic dans la Hierarchy** : sélectionne l'objet.
- **Clic dans le Viewport** : sélectionne l'objet sous le curseur (priorité à l'objet le plus visuellement "au-dessus" en cas de chevauchement). Un objet est cliquable s'il a un `Collider`, un `RectRenderer`, ou un `SpriteRenderer` — un objet purement logique sans ces composants n'est sélectionnable que depuis la Hierarchy.
- **Clic sur du vide** : désélectionne.

## Gizmos de transform

Actifs sur l'objet sélectionné, dans le Viewport, en mode édition.

- **Mode** : `W` (Move), `E` (Rotate), `R` (Scale).
- **Espace** : bouton Local/World dans la toolbar (à gauche). En Local, les flèches Move/Scale suivent la rotation de l'objet.
- **Move** : flèche rouge = axe X, verte = axe Y, poignée centrale blanche = déplacement libre.
- **Scale** : mêmes couleurs, poignées carrées. La poignée centrale scale uniformément en conservant le ratio X/Y actuel.
- **Rotate** : anneau orange, cliquer-glisser dessus pour tourner.

Les poignées grandissent/rétrécissent avec le zoom de la caméra (taille définie en unités monde, pas en pixels fixes).

## Hierarchy — actions

- **Clic droit sur un objet** : renommer, créer un enfant (sous-menu Create), supprimer.
- **Clic droit sur l'espace vide** : créer un objet à la racine, sauvegarder la scène.
- **Drag-and-drop** : glisser un objet sur un autre pour le reparenter ; glisser dans l'espace vide en bas de la liste pour le ramener à la racine.
- **Menu Create** : Empty, Camera, Rect, Sprite, Physics Object (PhysicsBody + Collider), UI Canvas/Button/Image.

## Inspector — Transform

Champs éditables : Local X/Y, Rotation (degrés), Scale X/Y. Si l'objet a un parent, affiche aussi World X/Y et World Rotation en lecture seule.

## Inspector — Composants

- **Clic sur l'en-tête** d'un composant : déplie/replie ses champs.
- **Clic droit sur l'en-tête** : Remove Component.
- **Add Component** : recherche par nom, liste tous les types de `Component` détectés (moteur + scripts utilisateur), automatiquement rescannée après chaque hot-reload de script.

## Project Panel — gestion des fichiers

- **Arbre de dossiers** (gauche) : navigation, clic droit pour renommer/supprimer/afficher dans l'explorateur.
- **Grille de fichiers** (droite) : icône par type, double-clic pour ouvrir (scène → charge dans l'éditeur, script → ouvre dans l'éditeur externe, autre → ouvre avec l'application par défaut).
- **Renommer** : disponible sur fichiers et dossiers via menu contextuel. Renommer un script `.cs` tente aussi de renommer la déclaration de classe correspondante à l'intérieur (uniquement si le fichier contient exactement une classe/struct/record du nom attendu — sinon renommage ignoré avec avertissement en console, pour éviter de corrompre un fichier multi-classes).
- **Import** : dialogue de fichier natif pour copier un asset externe dans le dossier courant.
- **Create** (clic droit sur l'espace vide) : Folder, C# Script (template avec `[UserScript]`), Scene.

## Scripts et hot-reload

Toute sauvegarde d'un `.cs` sous `Assets/` déclenche automatiquement (après ~1.5s de silence) : recompilation Roslyn → si succès, sauvegarde + rechargement de la scène courante (les nouveaux/modifiés types de composants deviennent immédiatement utilisables) → rescan du menu Add Component. Les erreurs de compilation apparaissent dans la Console, cliquables pour ouvrir directement le fichier à la ligne fautive dans Visual Studio.

## Audio

`AudioSource` (menu Add Component) expose : sélection de clip via picker dédié, Volume, Pitch, Loop, Spatial 2D, Play On Start, et des boutons Play/Stop utilisables directement dans l'Inspector pour prévisualiser un son sans lancer le jeu.

## ⚠ Problème connu — erreurs de compilation invisibles

Dans l'état actuel du code (`ScriptCompiler.cs`), la branche qui gère un **échec** de compilation ne contient plus de logique réelle (juste un commentaire `// ... inchangé` laissé en place lors d'une édition précédente). Conséquence concrète pour l'utilisation de l'éditeur : **une erreur de syntaxe dans un script n'apparaît plus du tout dans la Console**, et le double-clic pour ouvrir le fichier à la ligne fautive ne peut jamais se déclencher puisqu'aucune entrée de log correspondante n'est créée. Le seul signe visible est que la scène ne se recharge pas après une sauvegarde de script (silencieusement).

À corriger dans `ScriptCompiler.Compile()` en restaurant l'itération sur `result.Diagnostics` (voir `architecture.md`, section hot-reload).

## Fermeture de l'éditeur

Un popup de confirmation ("Are you sure you want to quit Kore Engine?") s'affiche à la fermeture de la fenêtre — pas d'indicateur de modifications non sauvegardées pour l'instant (fonctionnalité prévue, pas encore implémentée).
