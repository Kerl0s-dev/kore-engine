# KoreEngine — Guide de l'éditeur

> Mis à jour à partir du code source complet le plus récent (archive du 18/10/2026).

## Créer un projet

Depuis **Kore Engine Hub** (recommandé) : bouton **Nouveau projet**, choisir un nom et un dossier parent. Le nom doit être un identifiant C# valide (`[A-Za-z_][A-Za-z0-9_]*`). Le Hub scaffolde le projet — voir `docs/hub-and-build.md` pour le détail de ce qui est généré — puis le projet apparaît dans la liste ; **Ouvrir** (ou double-clic) le compile et lance l'éditeur.

Prérequis : `KoreEngine.Runtime` et `KoreEngine.Editor` doivent avoir été compilés au moins une fois (`dotnet build KoreEngine.slnx`) — le Hub refuse de créer un projet si leurs DLL sont introuvables.

Chaque projet généré **est son propre éditeur** : le bouton Play/Pause/Stop bascule un mode de simulation dans la même fenêtre. Un export « joueur » (exécutable sans éditeur) passe par le bouton **Build** de la toolbar.

## Interface générale

- **Hierarchy** : arbre des `GameObject` de la scène courante, nom de la scène en en-tête.
- **Inspector** : détail de l'objet sélectionné — nom, liste de composants (le `Transform` en premier), bouton *Add Component*.
- **Project** : navigateur de fichiers — racines **Assets** (modifiable) et **Packages** (lecture seule), arbre de dossiers + grille de fichiers avec icônes.
- **Console** : logs filtrables par niveau (Info / Warning / Error / Sucess), champ de recherche, *Auto-scroll*, bouton *Clear*. Les erreurs de compilation de script sont cliquables (double-clic → ouvre Visual Studio à la ligne exacte).
- **Scene** (centre) : vue d'édition — `EditorCamera`, gizmos, sélection par clic.
- **Game** : ce que verra le joueur — caméra de la scène, avec sa couleur de fond.

Les panneaux sont dockables ; un nouveau panneau apparaît d'abord flottant (la disposition est mémorisée dans `imgui.ini`).

## Toolbar

Barre horizontale sous la menu bar : bouton **World/Local** à gauche (espace des gizmos), icônes Play / Pause / Step au centre, bouton **Build** à droite. Pendant une recompilation de scripts, l'état s'affiche en jaune à côté du bouton World/Local.

- **Play** : sauvegarde la scène courante, démarre la simulation (`Start()` sur les composants). La sauvegarde est verrouillée pendant le Play.
- **Stop** (le bouton Play devient Stop, surligné) : recharge la scène depuis son fichier sauvegardé, remet Playing/Pause à faux.
- **Pause** : suspend `Update()` sans arrêter le rendu — la scène reste visible et modifiable.
- **Step** : disponible uniquement en pause, avance la simulation d'exactement une frame.
- **Build** : publie un exécutable joueur (Release, plateforme courante, self-contained) dans `{projet}/Build/` et copie `Assets/` et `Packages/` à côté. Le libellé passe à « Building... » (désactivé) pendant l'opération ; succès/échec dans la Console.

## Raccourcis clavier

| Raccourci | Action |
| --- | --- |
| `Ctrl+S` | Sauvegarder la scène courante |
| `Ctrl+R` | Reset Camera (position + zoom de l'`EditorCamera`) |
| `W` / `E` / `R` | Gizmo Move / Rotate / Scale |
| `F` | Centrer l'`EditorCamera` sur l'objet sélectionné |
| `Suppr` | Supprimer l'objet sélectionné |

Ces raccourcis ne s'appliquent que si aucun champ texte n'a le focus. La fenêtre **Help > Keyboard Shortcuts** n'en liste que cinq (`Ctrl+R`, `Ctrl+S`, `W`, `E`, `R`) ; `F` et `Suppr` n'y figurent pas encore.

## Scene view — navigation

- **Clic molette + drag** : pan.
- **Ctrl + molette** : zoom (de `0.05` à `20`), centré sur la souris.
- **Ctrl+R** : réinitialise la caméra.

Le Scene view utilise toujours l'`EditorCamera`, y compris pendant le Play : les gizmos restent actifs et l'on peut déplacer des objets en cours de simulation.

## Game view

Rend la scène avec la caméra de la scène. Une liste déroulante choisit la résolution :

| Option | Effet |
| --- | --- |
| **Free** | La texture suit la taille du panneau (comme un jeu buildé redimensionnable). |
| **1920x1080 / 1280x720 / 1024x768** | Résolution fixe ; l'image est mise à l'échelle en conservant le ratio (bandes noires). Avec 1920×1080, un `UICanvas` de référence 1920×1080 a une échelle de 1. |

Le Game view est aussi celui qui fournit la position de la souris à l'UI du jeu : les `UIButton` réagissent à la souris **sur le Game view**, pendant le Play. Hors Play, l'UI s'affiche mais n'est pas interactive.

## Sélection d'objets

- **Clic dans la Hierarchy** : sélectionne l'objet.
- **Clic dans le Scene view** : sélectionne l'objet sous le curseur. Cliquable s'il a un `Collider` ou un `SpriteRenderer` — les autres objets (logiques, caméras, UI) ne se sélectionnent que depuis la Hierarchy.
- **Clic dans le vide** : désélectionne.

## Gizmos

### Gizmos de transform

Actifs sur l'objet sélectionné, dans le Scene view.

- **Mode** : `W` (Move), `E` (Rotate), `R` (Scale).
- **Espace** : bouton World/Local de la toolbar. En Local, les flèches suivent la rotation de l'objet.
- **Move** : flèche rouge = axe X, verte = axe Y, poignée centrale blanche = déplacement libre.
- **Scale** : mêmes couleurs, poignées carrées ; la poignée centrale scale uniformément.
- **Rotate** : anneau orange.

### Gizmos de caméra

Chaque objet avec un composant `Camera` affiche dans le Scene view :

- un **cadre** montrant exactement ce que voit la caméra (taille du Game view, `Zoom` pris en compte) — gris pour une caméra normale, **blanc et plus épais** pour la caméra de l'objet sélectionné ;
- une **icône** de caméra à sa position (taille fixe à l'écran), teintée comme le cadre ; si `camera.png` est absent d'`Editor/Icons/`, une croix la remplace ;
- le **nom** de l'objet dans le coin supérieur gauche.

Le cadre suit l'objet quand on le déplace. Il n'est pas cliquable : sélectionner la caméra se fait depuis la Hierarchy.

## Hierarchy — actions

- **Clic droit sur un objet** : renommer, sous-menu **Create** (enfant de cet objet), **Duplicate** (clone l'objet et ses enfants, rattaché au même parent), **Create Prefab**, **Delete**.
- **Clic droit sur l'espace vide** : **Create** (à la racine), **Instantiate Prefab** (liste les `.kprefab` sous `Assets/`), **Save Scene**.
- **Drag-and-drop** : glisser un objet sur un autre pour le reparenter ; dans l'espace vide pour le ramener à la racine.
- **Menu Create** : Empty, Camera, Sprite, Physics Object (`PhysicsBody` + `Collider`), UI Canvas, UI Button, UI Image.

Supprimer un parent supprime aussi ses enfants.

## Prefabs

Un prefab capture un `GameObject` et toute sa hiérarchie d'enfants dans un fichier `.kprefab` (même format texte que les scènes) :

- **Créer** : clic droit sur un objet → **Create Prefab** (écrit `Assets/Prefabs/{Name}.kprefab`).
- **Instancier** : clic droit sur l'espace vide de la Hierarchy → **Instantiate Prefab**, ou double-clic / clic droit → *Instantiate* sur un `.kprefab` dans le Project Panel.

## Inspector

- **Nom** de l'objet (champ en haut).
- **Transform** : X, Y (position locale), Rotation (degrés), Scale X/Y. Il ne peut pas être supprimé (l'entrée du menu est grisée).
- **En-tête de composant** : clic pour déplier/replier ; clic droit → **Remove Component** (grisé pour les composants `[UnremovableComponent]`).
- **Add Component** : fenêtre de recherche listant tous les types de `Component` instanciables (moteur + scripts utilisateur), sauf `Transform` ; rescannée après chaque hot-reload.
- **Champs** : un composant qui décrit ses champs (`GetInspectorFields`) est affiché tel quel ; sinon, tous ses champs publics sont affichés par réflexion. `[HideInInspector]` / `[NonSerialized]` masquent un champ ; `[Range(min, max)]` le transforme en slider borné.
- **Pickers** : textures (miniatures), clips audio, références de `GameObject` / `Component` (avec recherche).
- **Colonnes** : les libellés sont alignés à gauche, les widgets occupent la partie droite.

### Éditer un `KoreEvent` (ex. `UIButton.OnClick`)

Un champ `KoreEvent` s'affiche comme une liste de listeners. Pour chaque listener :

1. **Object** : le `GameObject` cible (picker).
2. **Component** : un des composants de cet objet.
3. **Method** : une méthode publique `void` à 0 ou 1 paramètre (`string`, `int`, `float`, `bool`) — les méthodes héritées de `object` sont exclues.
4. **Arg** : la valeur, si la méthode prend un paramètre.

Boutons **Remove** (par listener) et **+ Add Listener**. La cible est identifiée **par le nom** de l'objet : deux objets de même nom → le premier est appelé, et renommer la cible casse le listener. Pour déclencher un chargement de scène ou quitter le jeu depuis un bouton, il faut un composant exposant par exemple `LoadScene(string)` ou `Quit()`.

## Project Panel — gestion des fichiers

- **Arbre de dossiers** (gauche) : clic droit → renommer, supprimer, afficher dans l'explorateur. **Packages** est en lecture seule (ni création, ni import, ni renommage, ni suppression).
- **Grille de fichiers** (droite) : icône par type, **double-clic** pour ouvrir — scène → charge dans l'éditeur, prefab → instancie dans la scène courante, script → ouvre dans l'éditeur externe, autre → application par défaut.
- **Menu contextuel d'un fichier** : *Open Scene*, **Set as Startup Scene** (écrit `ProjectSettings.txt`), *Instantiate* (prefab), *Edit* (script), Rename, Show in Explorer, Delete.
- **Renommer** : un script `.cs` tente aussi de renommer la déclaration de classe à l'intérieur (uniquement si le fichier contient exactement une classe/struct/record du nom attendu — sinon renommage ignoré avec avertissement). Renommer la scène courante met à jour son chemin.
- **Import** : dialogue de fichier natif pour copier un asset externe dans le dossier courant.
- **Create** (clic droit sur l'espace vide) : Folder, **C# Script** (template avec `[UserScript]`), Scene. **Refresh** relit le dossier.

## Scripts et hot-reload

Toute sauvegarde d'un `.cs` sous `Assets/` déclenche (après ~1.5 s de silence) : recompilation Roslyn → si succès, sauvegarde + rechargement de la scène courante (les types de composants nouveaux ou modifiés sont immédiatement utilisables) → rescan du menu *Add Component*. **Les erreurs de compilation apparaissent dans la Console**, avec fichier et ligne, cliquables pour ouvrir le fichier dans Visual Studio.

## Audio

`AudioSource` expose : clip (picker), Volume, Pitch, Loop, Spatial 2D, Play On Start, une ligne de statut, et des boutons Play / Stop utilisables dans l'Inspector pour prévisualiser un son sans lancer le jeu.

## Fermeture de l'éditeur

Un popup de confirmation (« Are you sure you want to quit Kore Engine? ») s'affiche à la fermeture de la fenêtre. Pas d'indicateur de modifications non sauvegardées pour l'instant.
