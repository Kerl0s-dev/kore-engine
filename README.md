# KoreEngine

KoreEngine est un moteur de jeu 2D écrit en C#/.NET, avec un éditeur intégré façon Unity : composants, hiérarchie de GameObjects, hot-reload de scripts, scènes sérialisées, prefabs, et un launcher de type Unity Hub pour gérer plusieurs projets.

Construit sur **SDL3** (rendu, audio, texte), **ImGui.NET** (interface éditeur) et **Roslyn** (compilation à la volée des scripts de gameplay), en .NET 10, ciblant Windows.

## Structure du dépôt

```
KoreEngine.slnx
├── KoreEngine.Runtime/    # Cœur du moteur : Components, Core, Engine, Physics, Input, Animation
├── KoreEngine.Editor/     # Éditeur ImGui (panels, gizmos, hot-reload, compilation)
└── KoreEngine.Hub/        # Launcher WPF façon Unity Hub (créer/ouvrir/gérer des projets)
```

`KoreEngine.Runtime` et `KoreEngine.Editor` sont deux bibliothèques (`OutputType=Library`) référencées par `KoreEngine.slnx`. `KoreEngine.Runtime` ne référence jamais `KoreEngine.Editor` — dépendance strictement à sens unique. Un projet de jeu généré par le Hub référence les deux DLL compilées en binaire (jamais le code source du moteur), et embarque en plus un sous-projet `Player/` headless (sans ImGui/Roslyn) pour un build distribuable.

## Démarrer

1. Compiler le moteur : `dotnet build KoreEngine.slnx` (ou ouvrir `KoreEngine.slnx` dans Visual Studio).
2. Lancer `KoreEngine.Hub` — il détecte automatiquement le dossier racine du moteur (recherche de `KoreEngine.slnx` en remontant depuis l'exécutable), ou demande de le sélectionner manuellement.
3. Depuis le Hub : **New Project** pour scaffolder un nouveau projet, ou **Import** pour ouvrir un projet existant. Double-clic sur un projet pour le lancer (clean + build + run automatique).

Voir `docs/` pour la documentation détaillée :
- [`docs/architecture.md`](docs/architecture.md) — structure interne, pipelines (scènes, hot-reload, build)
- [`docs/components-reference.md`](docs/components-reference.md) — référence de tous les composants du moteur
- [`docs/editor-guide.md`](docs/editor-guide.md) — utilisation de l'éditeur
- [`docs/hub-and-build.md`](docs/hub-and-build.md) — le launcher Hub et le pipeline de build/export

## État du projet

Développement actif, mono-développeur. Les systèmes suivants sont fonctionnels : rendu (sprites, rects, UI, texte), physique 2D (Swept AABB), audio, animation (state machine par transitions), scripting avec hot-reload, sérialisation de scènes et de prefabs, gizmos de transform, et le Hub de gestion de projets. Le pipeline d'export d'un jeu "joueur" autonome (hors éditeur) est en cours de stabilisation — voir `docs/hub-and-build.md` pour l'état exact et les limitations connues.
