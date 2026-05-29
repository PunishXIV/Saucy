using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Saucy.IPC;
namespace Saucy.TripleTriad;

internal static class TriadNpcMapUi
{
    public static void DrawMapLocationRow(MapLinkPayload location, string showOnMapTooltip)
    {
        var label = $"{location.PlaceName} {location.CoordinateString}";
        var tooltip = BuildTooltip(showOnMapTooltip);

        ImGui.AlignTextToFramePadding();
        var rowY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(rowY - ImGui.GetStyle().FramePadding.Y);

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Map))
        {
            TriadMapNavigation.HandleMapClick(location);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SetCursorPosY(rowY);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();

        using (ImRaii.PushColor(ImGuiCol.Header, 0))
        {
            using (ImRaii.PushColor(ImGuiCol.HeaderHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered)))
            {
                using (ImRaii.PushColor(ImGuiCol.HeaderActive, ImGui.GetColorU32(ImGuiCol.ButtonActive)))
                {
                    if (ImGui.Selectable(label))
                    {
                        TriadMapNavigation.HandleMapClick(location);
                    }
                }
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static string BuildTooltip(string showOnMapTooltip)
    {
        if (!Vnavmesh.IsInstalled)
        {
            return $"{showOnMapTooltip}\nInstall vnavmesh to walk to this NPC.";
        }

        var lines = $"{showOnMapTooltip}\nClick to move with vnavmesh.";
        if (Lifestream.IsInstalled)
        {
            lines += "\nUses Lifestream to teleport to the nearest aetheryte first.";
        }

        return lines;
    }
}
