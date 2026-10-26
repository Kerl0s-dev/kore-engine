# KoreEngine

KoreEngine est un moteur de jeu 2D écrit en C#/.NET, avec un éditeur intégré : composants, hiérarchie de GameObjects avec `Transform`, hot-reload de scripts, scènes sérialisées, prefabs, vues **Scene** et **Game** séparées, événements configurables dans l'Inspector (`KoreEvent`), et un Hub pour gérer plusieurs projets.

Construit sur **SDL3** (rendu, audio, texte, images), **ImGui.NET** (interface de l'éditeur) et **Roslyn** (compilation à la volée des scripts de gameplay), en .NET 10. Cible principale : Windows ; les paquets natifs et les identifiants de plateforme (RID) Linux sont gérés par le Hub, le Runtime et l'éditeur, mais ce support est récent.

## Structure du dépôt

```text
KoreEngine.slnx
├── Directory.Build.props  # version unique du moteur (appliquée à tous les projets)
├── KoreEngine.Runtime/    # Cœur du moteur : Components, Core, Engine, Physics, Input, Animation
├── KoreEngine.Editor/     # Éditeur ImGui (panneaux Scene/Game/Hierarchy/Inspector/Project/Console, gizmos, hot-reload)
├── KoreEngine.Hub/        # Launcher SDL façon Unity Hub (créer/ouvrir des projets), sans WPF ni ImGui
└── docs/                  # Documentation détaillée
```

`KoreEngine.Runtime` et `KoreEngine.Editor` sont deux bibliothèques (`OutputType=Library`) ; `KoreEngine.Runtime` ne référence jamais `KoreEngine.Editor` — dépendance strictement à sens unique. `KoreEngine.Hub` ne référence ni l'un ni l'autre. Un projet de jeu généré par le Hub référence les deux DLL compilées en binaire (jamais le code source du moteur) et embarque en plus un sous-projet `Player/` headless (sans ImGui) pour produire un build distribuable.

## Démarrer

1. Compiler le moteur : `dotnet build KoreEngine.slnx` (ou ouvrir `KoreEngine.slnx` dans Visual Studio). Cette étape est obligatoire : le Hub refuse de créer un projet si les DLL du moteur sont introuvables.
2. Lancer `KoreEngine.Hub` — il détecte automatiquement le dossier racine du moteur (recherche de `KoreEngine.slnx` en remontant depuis l'exécutable).
3. Depuis le Hub : **Nouveau projet** (nom + dossier parent) pour scaffolder un projet, puis **Ouvrir** (ou double-clic) : le Hub resynchronise les dépendances, compile les scripts, publie l'éditeur du projet et le lance.

Voir `docs/` pour la documentation détaillée :

- [`docs/architecture.md`](docs/architecture.md) — structure interne, boucle de l'éditeur, scènes et sérialisation, physique, UI, pipelines
- [`docs/components-reference.md`](docs/components-reference.md) — référence de tous les composants et des APIs utilisables depuis un script
- [`docs/physics.md`](docs/physics.md) — unités (mètres), colliders, rotation physique et pipeline de collision
- [`docs/editor-guide.md`](docs/editor-guide.md) — utilisation de l'éditeur (panneaux, gizmos, raccourcis, Inspector)
- [`docs/hub-and-build.md`](docs/hub-and-build.md) — le launcher Hub, la création de projet et le pipeline de build/export

## État du projet

Développement actif, mono-développeur. Les systèmes suivants sont fonctionnels : rendu (sprites, UI, texte), physique 2D (Swept AABB et SAT, formes boîte/cercle/capsule, rotation de corps rigides, friction, restitution), audio, animation (state machine par transitions), scripting avec hot-reload, sérialisation de scènes et de prefabs (hiérarchies parent/enfant, `KoreEvent`), gizmos de transform et de caméra, vues Scene et Game, et le Hub de gestion de projets.

Limitations connues, détaillées dans `docs/` :

- **Animation de spritesheet** : `SpriteRenderer` affiche toujours la première frame de la feuille.
- **Détection du sol** : `PhysicsBody.IsGrounded` a été supprimé et aucune API ne le remplace pour l'instant.
- **Input** : `Input.GetKeyDown` / `GetKeyUp` ne suivent pas la sémantique d'Unity (voir `docs/components-reference.md`).
- **Physique sur objets enfants** : `PhysicsBody` ignore la position du parent.