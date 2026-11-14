using ImGuiNET;
using System.Numerics;

namespace KoreEngine
{
    public class Widgets
    {
        public static bool IconButton(string iconName, float size, bool highlighted = false)
        {
            if (highlighted)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.59f, 0.98f, 0.6f));

            bool clicked;
            IntPtr icon = EditorIcons.Get(iconName);

            if (icon != IntPtr.Zero)
            {
                clicked = ImGui.ImageButton($"##{iconName}", icon, new System.Numerics.Vector2(size, size));
            }
            else
            {
                // Fallback si l'icône n'existe pas encore sur disque
                clicked = ImGui.Button(iconName.Substring(0, 1).ToUpper(), new System.Numerics.Vector2(size, 0));
            }

            if (highlighted)
                ImGui.PopStyleColor();

            return clicked;
        }

        public static bool IconTextButton(string iconName, float size, bool highlighted = false)
        {
            bool clicked = false;

            // 1. Gestion de la couleur de fond si l'élément est en surbrillance (sélectionné)
            if (highlighted)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.26f, 0.59f, 0.98f, 0.6f));

            // Sauvegarde de la position de départ du curseur à l'écran
            System.Numerics.Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();

            // Calcul de la largeur totale nécessaire (Icône + Espacement + Texte)
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float textWidth = ImGui.CalcTextSize(iconName).X;
            float totalWidth = size + spacing + textWidth;
            float totalHeight = Math.Max(size, ImGui.GetTextLineHeight());

            // 2. Affichage visuel (Icône + Texte)
            IntPtr icon = EditorIcons.Get(iconName);
            if (icon != IntPtr.Zero)
            {
                ImGui.Image(icon, new System.Numerics.Vector2(size, size));
            }
            else
            {
                // Fallback si l'icône n'existe pas : on affiche une lettre
                string fallbackText = !string.IsNullOrEmpty(iconName) ? iconName.Substring(0, 1).ToUpper() : "?";
                ImGui.Button(fallbackText, new System.Numerics.Vector2(size, size));
            }

            ImGui.SameLine();

            // Centrage vertical optionnel du texte par rapport à l'icône
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (size - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.Text(iconName);

            // 3. Zone cliquable globale par-dessus tout le bloc
            ImGui.SetCursorScreenPos(cursorScreenPos);

            // InvisibleButton va capturer les clics, le survol (hover) et l'état actif sur toute la largeur
            if (ImGui.InvisibleButton($"##btn_{iconName}", new System.Numerics.Vector2(totalWidth, totalHeight)))
            {
                clicked = true;
            }

            // Effet visuel optionnel : dessiner un fond au survol (hover) ou lorsqu'il est cliqué
            if (ImGui.IsItemHovered() || highlighted)
            {
                // Vous pouvez dessiner un fond personnalisé ici si vous le souhaitez via ImGui.GetWindowDrawList()
            }

            if (highlighted)
                ImGui.PopStyleColor();

            return clicked;
        }
    }
}
