using ImGuiNET;
using SDL3;
using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;

namespace KoreEngine.Editor;

public class ProjectPanel
{
    // 1. Rend la liste des dossiers racines statique/accessible pour les Pickers et l'Inspecteur
    private static readonly List<ProjectFolderRoot> folderRoots = new();
    public static IReadOnlyList<ProjectFolderRoot> FolderRoots => folderRoots;

    // Dossier actuellement sélectionné dans l'arbre
    public static string selectedDir = "";

    // Fichiers du dossier sélectionné
    List<string> currentFiles = new();
    List<string> currentDirs = new();

    // Taille des icônes en grille
    float iconSize = 72f;
    float padding = 8f;

    // Pending actions
    string? pendingDelete = null;
    string pendingImportFile = "";

    SDL.DialogFileCallback dialogCallback;

    // Popups
    string newItemName = "";
    bool newItemError = false;
    bool openNewScript = false;
    bool openNewScene = false;
    bool openNewFolder = false;
    bool openRename = false;
    string? renameTargetPath = null;
    bool renameTargetIsDir = false;
    string renameNewName = "";
    bool renameError = false;

    float lastRescan = -10f;
    const float RescanInterval = 2f;

    public ProjectPanel()
    {
        folderRoots.Clear();
        string projectRoot = FindProjectRoot();

        // Déclarer vos dossiers racines de manière centralisée
        RegisterRoot("Assets", Path.Combine(projectRoot, "Assets"), readOnly: false, defaultOpen: true);
        RegisterRoot("Packages", Path.Combine(projectRoot, "Packages"), readOnly: true);

        // Sélection par défaut sur le premier dossier valide (Assets)
        var defaultRoot = folderRoots.FirstOrDefault()?.FullPath ?? Directory.GetCurrentDirectory();
        selectedDir = defaultRoot;

        RefreshCurrentDir();
        dialogCallback = new SDL.DialogFileCallback(OnFileDialogResult);
    }

    public void RegisterRoot(string displayName, string fullPath, bool readOnly = true, bool defaultOpen = false)
    {
        if (!Directory.Exists(fullPath))
        {
            try { Directory.CreateDirectory(fullPath); }
            catch (Exception e) { Logger.Error($"Impossible de créer le dossier racine '{displayName}': {e.Message}"); }
        }

        if (Directory.Exists(fullPath))
        {
            folderRoots.Add(new ProjectFolderRoot(displayName, fullPath, readOnly, defaultOpen));
        }
    }

    /// <summary>
    /// Scanne TOUTES les racines configurées dans le projet (Assets, Packages, etc.)
    /// et retourne l'ensemble des fichiers trouvés.
    /// </summary>
    public static List<string> GetAllProjectFiles()
    {
        var allFiles = new List<string>();
        foreach (var root in folderRoots)
        {
            if (Directory.Exists(root.FullPath))
            {
                allFiles.AddRange(TextureCache.ScanAssets(root.FullPath));
            }
        }
        return allFiles;
    }

    public void Draw()
    {
        // Rescan périodique léger
        lastRescan += ImGui.GetIO().DeltaTime;
        if (lastRescan > RescanInterval) { RefreshCurrentDir(); lastRescan = 0f; }

        // Import en attente (callback depuis un autre thread)
        if (!string.IsNullOrEmpty(pendingImportFile))
        {
            if (!IsPathReadOnly(selectedDir))
            {
                string dest = Path.Combine(selectedDir, Path.GetFileName(pendingImportFile));
                try { File.Copy(pendingImportFile, dest, overwrite: true); }
                catch (Exception e) { Logger.Error($"Import: {e.Message}"); }
            }
            else
            {
                Logger.Warning("Impossible d'importer dans un dossier en lecture seule.");
            }
            pendingImportFile = "";
            RefreshCurrentDir();
        }

        // Delete en attente
        if (pendingDelete != null)
        {
            if (!IsPathReadOnly(pendingDelete))
            {
                try
                {
                    if (File.Exists(pendingDelete)) File.Delete(pendingDelete);
                    else if (Directory.Exists(pendingDelete)) Directory.Delete(pendingDelete, true);
                }
                catch (Exception e) { Logger.Error($"Delete: {e.Message}"); }
            }
            else
            {
                Logger.Warning("Impossible de supprimer un élément dans un dossier en lecture seule.");
            }
            pendingDelete = null;
            RefreshCurrentDir();
        }

        ImGui.Begin("Project");

        // Barre du haut : chemin courant + bouton refresh
        string rel = GetRelativePath(selectedDir);
        ImGui.TextDisabled(rel);
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh")) RefreshCurrentDir();

        ImGui.Separator();

        // Layout deux colonnes
        float totalW = ImGui.GetContentRegionAvail().X;
        float leftW = totalW * 0.25f;
        float rightW = totalW * 0.75f - 4f;

        // --- Colonne gauche : arbre de dossiers ---
        ImGui.BeginChild("##dir_tree", new System.Numerics.Vector2(leftW, 0));
        foreach (var root in folderRoots)
        {
            DrawDirTree(root.FullPath, root);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // --- Colonne droite : grille de fichiers ---
        ImGui.BeginChild("##file_grid", new System.Numerics.Vector2(rightW, 0));
        DrawFileGrid();
        ImGui.EndChild();

        // Popups
        DrawNewScriptPopup();
        DrawNewScenePopup();
        DrawNewFolderPopup();
        DrawRenamePopup();

        ImGui.End();
    }

    // ---------------------------------------------------------------
    // Arbre de dossiers
    // ---------------------------------------------------------------

    void DrawDirTree(string dir, ProjectFolderRoot rootInfo)
    {
        bool isRootNode = string.Equals(dir, rootInfo.FullPath, StringComparison.OrdinalIgnoreCase);
        string name = isRootNode ? rootInfo.DisplayName : Path.GetFileName(dir);
        bool isSelected = string.Equals(dir, selectedDir, StringComparison.OrdinalIgnoreCase);

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;

        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;
        if (isRootNode && rootInfo.DefaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        bool hasSubDirs = Directory.Exists(dir) && Directory.GetDirectories(dir).Length > 0;
        if (!hasSubDirs) flags |= ImGuiTreeNodeFlags.Leaf;

        bool open = ImGui.TreeNodeEx($"{name}##tree_{dir}", flags);

        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            selectedDir = dir;
            RefreshCurrentDir();
        }

        bool isProtected = isRootNode && rootInfo.ReadOnly;
        if (!isProtected && ImGui.BeginPopupContextItem($"##tree_ctx_{dir}"))
        {
            if (ImGui.MenuItem("Rename"))
            {
                renameTargetPath = dir;
                renameTargetIsDir = true;
                renameNewName = Path.GetFileName(dir);
                openRename = true;
            }
            if (ImGui.MenuItem("Show in Explorer")) OpenInExplorer(dir);
            ImGui.Separator();
            if (ImGui.MenuItem("Delete")) pendingDelete = dir;
            ImGui.EndPopup();
        }

        if (open)
        {
            if (Directory.Exists(dir))
            {
                foreach (var sub in Directory.GetDirectories(dir).OrderBy(d => d))
                {
                    DrawDirTree(sub, rootInfo);
                }
            }

            ImGui.TreePop();
        }
    }

    // ---------------------------------------------------------------
    // Grille de fichiers
    // ---------------------------------------------------------------

    void DrawFileGrid()
    {
        float cellW = iconSize + padding;
        float panelW = ImGui.GetContentRegionAvail().X;
        int cols = Math.Max(1, (int)(panelW / cellW));
        int col = 0;

        if (ImGui.BeginPopupContextWindow("##grid_ctx",
            ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawContextMenuEmpty();
            ImGui.EndPopup();
        }

        foreach (var dir in currentDirs)
        {
            DrawGridItem(Path.GetFileName(dir), dir, isDir: true, ref col, cols);
        }

        foreach (var file in currentFiles)
        {
            DrawGridItem(Path.GetFileNameWithoutExtension(file), file, isDir: false, ref col, cols);
        }
    }

    void DrawGridItem(string label, string fullPath, bool isDir, ref int col, int cols)
    {
        float cellW = iconSize + padding;

        ImGui.BeginGroup();

        // 1. Icône / Miniature
        if (isDir)
        {
            IntPtr folderIcon = EditorIcons.Folder;
            if (folderIcon != IntPtr.Zero)
            {
                SDL.SetTextureScaleMode(folderIcon, SDL.ScaleMode.Linear);
                ImGui.Image(folderIcon, new System.Numerics.Vector2(iconSize, iconSize));
            }
            else
                DrawIconPlaceholder(iconSize, 0xFF3A7BD5, "📁");
        }
        else
        {
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg")
            {
                IntPtr tex = TextureCache.Get(fullPath);
                if (tex != IntPtr.Zero)
                {
                    SDL.SetTextureScaleMode(tex, SDL.ScaleMode.Linear);
                    ImGui.Image(tex, new System.Numerics.Vector2(iconSize, iconSize));
                }
                else
                    DrawIconPlaceholder(iconSize, 0xFF555555, "?");
            }
            else
            {
                IntPtr icon = EditorIcons.Get(ext);
                if (icon != IntPtr.Zero)
                {
                    SDL.SetTextureScaleMode(icon, SDL.ScaleMode.Linear);
                    ImGui.Image(icon, new System.Numerics.Vector2(iconSize, iconSize));
                }
                else
                    DrawIconPlaceholder(iconSize, 0xFF666666, ext.TrimStart('.').ToUpper());
            }
        }

        // 2. Texte sous l'icône
        string shortLabel = label.Length > 12 ? label[..12] + "..." : label;
        float textW = ImGui.CalcTextSize(shortLabel).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (iconSize - textW) * 0.5f);
        ImGui.TextUnformatted(shortLabel);

        ImGui.EndGroup();

        // 3. Interactions
        bool isHovered = ImGui.IsItemHovered();
        bool dblClicked = isHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

        if (isHovered) ImGui.SetTooltip(label);

        if (dblClicked)
        {
            if (isDir) { selectedDir = fullPath; RefreshCurrentDir(); }
            else HandleFileOpen(fullPath);
        }

        if (ImGui.BeginPopupContextItem($"##item_ctx_{fullPath.GetHashCode()}"))
        {
            DrawContextMenuItem(fullPath, isDir);
            ImGui.EndPopup();
        }

        col++;
        if (col < cols) ImGui.SameLine(col * cellW);
        else col = 0;
    }

    void DrawIconPlaceholder(float size, uint color, string text)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new System.Numerics.Vector2(size, size));
        dl.AddRectFilled(pos, new System.Numerics.Vector2(pos.X + size, pos.Y + size), color, 6f);
        var ts = ImGui.CalcTextSize(text);
        dl.AddText(
            new System.Numerics.Vector2(pos.X + (size - ts.X) * 0.5f, pos.Y + (size - ts.Y) * 0.5f),
            0xFFFFFFFF, text);
    }

    // ---------------------------------------------------------------
    // Menus contextuels & Popups (inchangés)
    // ---------------------------------------------------------------

    void DrawContextMenuEmpty()
    {
        bool isReadOnly = IsPathReadOnly(selectedDir);

        if (isReadOnly)
        {
            ImGui.TextDisabled("Dossier en lecture seule");
            ImGui.Separator();
        }
        else
        {
            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("Folder")) { newItemName = "New Folder"; openNewFolder = true; }
                if (ImGui.MenuItem("C# Script")) { newItemName = "NewScript"; openNewScript = true; }
                if (ImGui.MenuItem("Scene")) { newItemName = "New Scene"; openNewScene = true; }
                ImGui.EndMenu();
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Import"))
            {
                SDL.ShowOpenFileDialog(dialogCallback, IntPtr.Zero,
                    EditorWindow.Current!.WindowHandle, null, 0,
                    selectedDir, true);
            }

            ImGui.Separator();
        }

        if (ImGui.MenuItem("Refresh")) RefreshCurrentDir();
    }

    void DrawContextMenuItem(string path, bool isDir)
    {
        bool isReadOnly = IsPathReadOnly(path);

        if (!isDir)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".kscene" && ImGui.MenuItem("Open Scene"))
                SceneManager.LoadSceneFromFile(path);
            if (ext == ".kscene" && ImGui.MenuItem("Set as Startup Scene"))
            {
                ProjectSettings.StartupScene = Path.GetFileNameWithoutExtension(path);
                ProjectSettings.Save();
            }
            if (ext == ".kprefab" && ImGui.MenuItem("Instantiate"))
                InstantiatePrefab(path);
            if (ext == ".cs" && ImGui.MenuItem("Edit"))
                OpenInEditor(path);

            ImGui.Separator();
        }

        if (!isReadOnly)
        {
            if (ImGui.MenuItem("Rename"))
            {
                renameTargetPath = path;
                renameTargetIsDir = isDir;
                renameNewName = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
                openRename = true;
            }
        }

        if (ImGui.MenuItem("Show in Explorer"))
            OpenInExplorer(isDir ? path : Path.GetDirectoryName(path)!);

        if (!isReadOnly)
        {
            ImGui.Separator();
            if (ImGui.MenuItem("Delete"))
                pendingDelete = path;
        }
    }

    void DrawNewScriptPopup()
    {
        if (openNewScript) { ImGui.OpenPopup("##new_script_popup"); openNewScript = false; newItemError = false; }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 120), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##new_script_popup")) return;

        ImGui.Text("Script name");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##nsname", ref newItemName, 64);
        if (newItemError) ImGui.TextColored(new Vector4(1, .3f, .3f, 1), "Already exists.");

        ImGui.Spacing();
        float w = (ImGui.GetContentRegionAvail().X - 4f) * .5f;
        if (ImGui.Button("Create", new System.Numerics.Vector2(w, 0)))
        {
            string path = Path.Combine(selectedDir, $"{newItemName.Trim()}.cs");
            if (File.Exists(path)) { newItemError = true; }
            else
            {
                File.WriteAllText(path,
$@"using KoreEngine;

[UserScript]
public class {newItemName.Trim()} : Component
{{
    public override void Start()
    {{
        
    }}

    public override void Update(float dt)
    {{
        
    }}
}}");
                RefreshCurrentDir();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(w, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    void DrawNewScenePopup()
    {
        if (openNewScene) { ImGui.OpenPopup("##new_scene_popup"); openNewScene = false; newItemError = false; }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 120), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##new_scene_popup")) return;

        ImGui.Text("Scene name");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##nscname", ref newItemName, 64);
        if (newItemError) ImGui.TextColored(new Vector4(1, .3f, .3f, 1), "Already exists.");

        ImGui.Spacing();
        float w = (ImGui.GetContentRegionAvail().X - 4f) * .5f;
        if (ImGui.Button("Create", new System.Numerics.Vector2(w, 0)))
        {
            string path = Path.Combine(selectedDir, $"{newItemName.Trim()}.kscene");
            if (File.Exists(path)) { newItemError = true; }
            else
            {
                var empty = new Scene { Name = newItemName.Trim() };
                SceneSerializer.SaveScene(empty, path);
                SceneManager.ScanAndRegisterScenes();
                RefreshCurrentDir();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(w, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    void DrawNewFolderPopup()
    {
        if (openNewFolder) { ImGui.OpenPopup("##new_folder_popup"); openNewFolder = false; newItemError = false; }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 120), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##new_folder_popup")) return;

        ImGui.Text("Folder name");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##nfname", ref newItemName, 64);
        if (newItemError) ImGui.TextColored(new Vector4(1, .3f, .3f, 1), "Already exists.");

        ImGui.Spacing();
        float w = (ImGui.GetContentRegionAvail().X - 4f) * .5f;
        if (ImGui.Button("Create", new System.Numerics.Vector2(w, 0)))
        {
            string path = Path.Combine(selectedDir, newItemName.Trim());
            if (Directory.Exists(path)) { newItemError = true; }
            else { Directory.CreateDirectory(path); RefreshCurrentDir(); ImGui.CloseCurrentPopup(); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(w, 0))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    void DrawRenamePopup()
    {
        if (openRename) { ImGui.OpenPopup("##rename_popup"); openRename = false; renameError = false; }

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(280, 120), ImGuiCond.Always);
        if (!ImGui.BeginPopup("##rename_popup")) return;

        ImGui.Text("New name");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.InputText("##rnname", ref renameNewName, 64);
        if (renameError) ImGui.TextColored(new Vector4(1, .3f, .3f, 1), "Already exists.");

        ImGui.Spacing();
        float w = (ImGui.GetContentRegionAvail().X - 4f) * .5f;
        if (ImGui.Button("Rename", new System.Numerics.Vector2(w, 0)))
        {
            if (CommitRename()) ImGui.CloseCurrentPopup();
            else renameError = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(w, 0)))
        {
            renameTargetPath = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    bool CommitRename()
    {
        if (renameTargetPath == null) return false;

        string trimmed = renameNewName.Trim();
        if (trimmed.Length == 0) return false;

        string parentDir = Path.GetDirectoryName(renameTargetPath)!;
        string newPath;

        if (renameTargetIsDir)
        {
            newPath = Path.Combine(parentDir, trimmed);
            if (string.Equals(newPath, renameTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                renameTargetPath = null;
                return true;
            }
            if (Directory.Exists(newPath)) return false;

            Directory.Move(renameTargetPath, newPath);

            if (string.Equals(selectedDir, renameTargetPath, StringComparison.OrdinalIgnoreCase))
                selectedDir = newPath;
            else if (selectedDir.StartsWith(renameTargetPath + Path.DirectorySeparatorChar,
                         StringComparison.OrdinalIgnoreCase))
                selectedDir = newPath + selectedDir.Substring(renameTargetPath.Length);
        }
        else
        {
            string ext = Path.GetExtension(renameTargetPath);
            newPath = Path.Combine(parentDir, trimmed + ext);
            if (string.Equals(newPath, renameTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                renameTargetPath = null;
                return true;
            }
            if (File.Exists(newPath)) return false;

            File.Move(renameTargetPath, newPath);

            if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                string oldClassName = Path.GetFileNameWithoutExtension(renameTargetPath);
                TryRenameClassDeclaration(newPath, oldClassName, trimmed);
            }
        }

        SceneManager.NotifyPathRenamed(renameTargetPath, newPath);
        SceneManager.ScanAndRegisterScenes();

        renameTargetPath = null;
        RefreshCurrentDir();
        return true;
    }

    void TryRenameClassDeclaration(string csPath, string oldName, string newName)
    {
        if (oldName == newName) return;
        if (!IsValidIdentifier(newName))
        {
            Logger.Warning($"[ProjectPanel] '{newName}' n'est pas un identifiant C# valide — classe non renommée.");
            return;
        }

        string content;
        try { content = File.ReadAllText(csPath); }
        catch (Exception e) { Logger.Error($"[ProjectPanel] Lecture {csPath}: {e.Message}"); return; }

        var declPattern = new Regex($@"\b(class|struct|record)\s+{Regex.Escape(oldName)}\b");
        var matches = declPattern.Matches(content);

        if (matches.Count == 0)
        {
            Logger.Warning($"[ProjectPanel] Aucune déclaration '{oldName}' trouvée dans {Path.GetFileName(csPath)}.");
            return;
        }

        if (matches.Count > 1)
        {
            Logger.Warning($"[ProjectPanel] Plusieurs déclarations '{oldName}' trouvées dans {Path.GetFileName(csPath)}.");
            return;
        }

        string updated = Regex.Replace(content, $@"\b{Regex.Escape(oldName)}\b", newName);

        try
        {
            File.WriteAllText(csPath, updated);
            Logger.Sucess($"[ProjectPanel] Classe '{oldName}' renommée en '{newName}' dans {Path.GetFileName(csPath)}.");
        }
        catch (Exception e)
        {
            Logger.Error($"[ProjectPanel] Écriture {csPath}: {e.Message}");
        }
    }

    static bool IsValidIdentifier(string name)
    {
        if (name.Length == 0) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    void RefreshCurrentDir()
    {
        if (!Directory.Exists(selectedDir))
            selectedDir = folderRoots.FirstOrDefault()?.FullPath ?? Directory.GetCurrentDirectory();

        currentDirs = Directory.Exists(selectedDir)
            ? Directory.GetDirectories(selectedDir).OrderBy(d => d).ToList()
            : new List<string>();

        currentFiles = Directory.Exists(selectedDir)
            ? Directory.GetFiles(selectedDir).OrderBy(f => f).ToList()
            : new List<string>();
    }

    string GetRelativePath(string dir)
    {
        var match = folderRoots.FirstOrDefault(r =>
            dir.Equals(r.FullPath, StringComparison.OrdinalIgnoreCase) ||
            dir.StartsWith(r.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        if (match == null) return dir;

        if (dir.Equals(match.FullPath, StringComparison.OrdinalIgnoreCase))
            return match.DisplayName;

        string rel = Path.GetRelativePath(match.FullPath, dir);
        return $"{match.DisplayName}/{rel}";
    }

    void HandleFileOpen(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".kscene") SceneManager.LoadSceneFromFile(path);
        else if (ext == ".kprefab") InstantiatePrefab(path);
        else OpenInEditor(path);
    }

    void InstantiatePrefab(string path)
    {
        if (SceneManager.Current == null) return;
        var obj = PrefabManager.Instantiate(path, SceneManager.Current);
        EditorSelection.Selected = obj;
    }

    void OnFileDialogResult(IntPtr userdata, IntPtr filelist, int filter)
    {
        var list = SDL.PointerToStringArray(filelist) ?? Array.Empty<string>();
        if (list.Length > 0 && !string.IsNullOrEmpty(list[0]))
            pendingImportFile = list[0];
    }

    public static string FindProjectRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6 && dir != null; i++)
        {
            if (Directory.GetFiles(dir, "*.csproj").Length > 0 ||
                Directory.GetFiles(dir, "*.sln").Length > 0)
                return dir;
            dir = Directory.GetParent(dir)?.FullName!;
        }
        return Directory.GetCurrentDirectory();
    }

    static void OpenInEditor(string path) => ExternalEditor.OpenFileAtLine(path, 1);

    static void OpenInExplorer(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    ArgumentList = { path },
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { path },
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[ProjectPanel] Impossible d'ouvrir le dossier : {ex.Message}");
        }
    }

    bool IsPathReadOnly(string path)
    {
        var root = folderRoots.FirstOrDefault(r =>
            path.Equals(r.FullPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(r.FullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        return root != null && root.ReadOnly;
    }
}

public class ProjectFolderRoot
{
    public string DisplayName { get; set; }
    public string FullPath { get; set; }
    public bool ReadOnly { get; set; }
    public bool DefaultOpen { get; set; }

    public ProjectFolderRoot(string displayName, string fullPath, bool readOnly = true, bool defaultOpen = false)
    {
        DisplayName = displayName;
        FullPath = fullPath;
        ReadOnly = readOnly;
        DefaultOpen = defaultOpen;
    }
}