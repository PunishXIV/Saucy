using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Saucy.IPC;
namespace Saucy.TripleTriad;

internal static class TriadNpcMapUi
{
    public static void DrawMapLocationRow(MapLinkPayload location, string showOnMapTooltip, TriadNpc? npc = null)
    {
        var label = $"{location.PlaceName} {location.CoordinateString}";
        var tooltip = BuildTooltip(location, showOnMapTooltip, npc);
        var leftClick = false;
        var rightClick = false;

        ImGui.AlignTextToFramePadding();
        var rowY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(rowY - ImGui.GetStyle().FramePadding.Y);

        ImGuiComponents.IconButton(FontAwesomeIcon.Map);
        CollectMapNavigationClick(ref leftClick, ref rightClick);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SetCursorPosY(rowY);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();

        using var link = ImGuiLayout.PlainTextLink();
        ImGui.Selectable(label);
        CollectMapNavigationClick(ref leftClick, ref rightClick);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        if (TriadBattleHall.ShouldBlockMapNavigation(npc, location))
        {
            if (leftClick || rightClick)
            {
                TriadBattleHall.PrintNavigationBlocked();
            }

            return;
        }

        if (rightClick)
        {
            TriadMapNavigation.HandleMapClick(location, npc, goal: TriadNavigationGoal.FarmMgp);
        }
        else if (leftClick)
        {
            TriadMapNavigation.HandleMapClick(location, npc, goal: TriadNavigationGoal.FarmCards);
        }
    }

    private static void CollectMapNavigationClick(ref bool leftClick, ref bool rightClick)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            leftClick = true;
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            rightClick = true;
        }
    }

    private static string BuildTooltip(MapLinkPayload location, string showOnMapTooltip, TriadNpc? npc = null)
    {
        if (TriadBattleHall.ShouldBlockMapNavigation(npc, location))
        {
            return $"{showOnMapTooltip}\nThe Battlehall is a Duty Finder instance.\nSaucy cannot path there.";
        }

        var unlockLine = TriadNpcUnlockHelper.TryGetTooltipLine(npc);
        if (unlockLine != null)
        {
            return $"{showOnMapTooltip}\n{unlockLine}";
        }

        if (!Vnavmesh.IsInstalled)
        {
            return $"{showOnMapTooltip}\nInstall vnavmesh to walk to this NPC.";
        }

        var lines = $"{showOnMapTooltip}\nLeft-click: path there and farm missing cards.";
        if (npc != null)
        {
            lines += "\nRight-click: path there and farm MGP.";
            lines += "\nEnables Triple Triad automation on arrival.";
            lines += "\nLeft-click uses MGP farm if you already have every card from this NPC.";
            lines += "\nLeft-click with missing cards builds an optimized deck even if that option is off.";
        }
        else
        {
            lines = $"{showOnMapTooltip}\nClick to path with vnavmesh.";
        }

        if (Lifestream.IsInstalled)
        {
            lines += "\nUses Lifestream for travel (aetheryte or aethernet shard).";
            var route = MultiAreaRouteRegistry.FindRoute(location);
            if (route?.TooltipHint != null)
            {
                lines += $"\n{route.TooltipHint}";
            }
        }

        return lines;
    }
}
