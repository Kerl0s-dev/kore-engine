# KoreEngine — Guide de l'éditeur

## Créer un projet

Depuis **KoreEngine.Hub** (recommandé) : bouton **New Project**, choisir un nom et un emplacement. Le Hub détecte automatiquement le dossier racine du moteur (ou demande de le sélectionner) et scaffolde le projet, puis le lance immédiatement. Voir `docs/hub-and-build.md` pour le détail de ce qui est généré.

Prérequis : `KoreEngine.Runtime.csproj` et `KoreEngine.Editor.csproj` doivent avoir été compilés au moins une fois (`dotnet build KoreEngine.slnx`) — le Hub refuse de créer un projet si les DLL du moteur sont introuvables.

Chaque projet généré **est son propre éditeur** : il n'y a pas de séparation entre "éditeur" et "jeu buildé" à ce niveau — le bouton Play/Pause/Stop bascule un mode de simulation dans la même fenêtre. Un vrai export "joueur" (exécutable sans éditeur intégré) passe par le bouton **Build** de la toolbar — voir `docs/hub-and-build.md` pour son état actuel et ses limitations connues.

## Interface générale

- **Hierarchy** (gauche) : arbre des `GameObject` de la scène courante, nom de la scène en en-tête.
- **Inspector** (droite) : détail de l'objet sélectionné — Transform (Position/Rotation/Scale), liste des composants, bouton Add Component.
- **Project** (bas gauche) : navigateur de fichiers `Assets/`, arbre de dossiers + grille de fichiers avec icônes.
- **Console** (bas droite) : logs filtrables par niveau (Info/Warning/Error/Success), recherche texte. Les erreurs de compilation de script sont cliquables (double-clic → ouvre Visual Studio à la ligne exacte).
- **Viewport** (centre) : rendu de la scène, gizmos, sélection par clic.

## Toolbar

Barre horizontale sous la menu bar : icônes Play / Stop / Pause / Step (centrées), bouton **Build** à droite.

- **Play** : sauvegarde la scène courante puis démarre la simulation.
- **Stop** : recharge la scène depuis son état sauvegardé, remet Playing/Pause à faux.
- **Pause** : suspend `Update()` sans arrêter le rendu — la scène reste visible et modifiable, comme en mode édition.
- **Step** : disponible uniquement en pause, avance la simulation d'exactement une frame.
- **Build** : publie un exécutable joueur (Release, win-x64, self-contained) dans `{projet}/Build/`. Le libellé passe à "Building..." (désactivé) pendant l'opération ; succès/échec loggé dans la Console. ⚠ voir `docs/hub-and-build.md` pour une limitation connue sur le chargement des scripts dans l'exe produit.

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

- **Clic molette + drag** : pan.
- **Ctrl + molette** : zoom, centré sur la position de la souris.
- **Ctrl+R** : réinitialise la caméra.

## Sélection d'objets

- **Clic dans la Hierarchy** : sélectionne l'objet.
- **Clic dans le Viewport** : sélectionne l'objet sous le curseur (priorité au plus visuellement "au-dessus" en cas de chevauchement). Cliquable s'il a un `Collider`, un `RectRenderer`, ou un `SpriteRenderer` — un objet purement logique n'est sélectionnable que depuis la Hierarchy.
- **Clic sur du vide** : désélectionne.

## Gizmos de transform

Actifs sur l'objet sélectionné, dans le Viewport, en mode édition.

- **Mode** : `W` (Move), `E` (Rotate), `R` (Scale).
- **Espace** : bouton Local/World dans la toolbar. En Local, les flèches Move/Scale suivent la rotation de l'objet.
- **Move** : flèche rouge = axe X, verte = axe Y, poignée centrale blanche = déplacement libre.
- **Scale** : mêmes couleurs, poignées carrées ; la poignée centrale scale uniformément.
- **Rotate** : anneau orange.

Les poignées grandissent/rétrécissent avec le zoom de la caméra (taille en unités monde, pas en pixels fixes).

## Hierarchy — actions

- **Clic droit sur un objet** : renommer, créer un enfant (sous-menu Create), **Create Prefab** (sauvegarde l'objet et sa hiérarchie dans `Assets/Prefabs/{Name}.kprefab`), supprimer.
- **Clic droit sur l'espace vide** : créer un objet à la racine, **Instantiate Prefab** (liste tous les `.kprefab` trouvés sous `Assets/`), sauvegarder la scène.
- **Drag-and-drop** : glisser un objet sur un autre pour le reparenter ; glisser dans l'espace vide en bas de la liste pour le ramener à la racine.
- **Menu Create** : Empty, Camera, Rect, Sprite, Physics Object (PhysicsBody + Collider), UI Canvas/Button/Image.

## Prefabs

Un prefab capture un `GameObject` et toute sa hiérarchie d'enfants dans un fichier `.kprefab` (même format texte que les scènes), réutilisable ensuite dans n'importe quelle scène :

- **Créer** : clic droit sur un objet dans la Hierarchy → **Create Prefab**.
- **Instancier** : clic droit sur l'espace vide de la Hierarchy → **Instantiate Prefab**, ou double-clic/glisser un fichier `.kprefab` depuis le Project Panel directement dans la scène.

## Inspector — Transform

Champs éditables : Local X/Y, Rotation (degrés), Scale X/Y. Si l'objet a un parent, affiche aussi World X/Y et World Rotation en lecture seule.

## Inspector — Composants

- **Clic sur l'en-tête** d'un composant : déplie/replie ses champs.
- **Clic droit sur l'en-tête** : Remove Component.
- **Add Component** : recherche par nom, liste tous les types de `Component` détectés (moteur + scripts utilisateur), rescannée automatiquement après chaque hot-reload de script.

## Project Panel — gestion des fichiers

- **Arbre de dossiers** (gauche) : navigation, clic droit pour renommer/supprimer/afficher dans l'explorateur.
- **Grille de fichiers** (droite) : icône par type, double-clic pour ouvrir (scène → charge dans l'éditeur, prefab → instancie dans la scène courante, script → ouvre dans l'éditeur externe, autre → ouvre avec l'application par défaut).
- **Renommer** : disponible sur fichiers et dossiers. Renommer un script `.cs` tente aussi de renommer la déclaration de classe correspondante à l'intérieur (uniquement si le fichier contient exactement une classe/struct/record du nom attendu — sinon renommage ignoré avec avertissement en console).
- **Import** : dialogue de fichier natif pour copier un asset externe dans le dossier courant.
- **Create** (clic droit sur l'espace vide) : Folder, C# Script (template avec `[UserScript]`), Scene.

## Scripts et hot-reload

Toute sauvegarde d'un `.cs` sous `Assets/` déclenche automatiquement (après ~1.5s de silence) : recompilation Roslyn → si succès, sauvegarde + rechargement de la scène courante (les nouveaux/modifiés types de composants deviennent immédiatement utilisables) → rescan du menu Add Component. **Les erreurs de compilation apparaissent dans la Console**, avec fichier et numéro de ligne, cliquables pour ouvrir directement le fichier à la ligne fautive dans Visual Studio.

## Audio

`AudioSource` (menu Add Component) expose : sélection de clip via picker dédié, Volume, Pitch, Loop, Spatial 2D, Play On Start, et des boutons Play/Stop utilisables directement dans l'Inspector pour prévisualiser un son sans lancer le jeu.

## Fermeture de l'éditeur

Un popup de confirmation ("Are you sure you want to quit Kore Engine?") s'affiche à la fermeture de la fenêtre — pas d'indicateur de modifications non sauvegardées pour l'instant (fonctionnalité prévue, pas encore implémentée).
