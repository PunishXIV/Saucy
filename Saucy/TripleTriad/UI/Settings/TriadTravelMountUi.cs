using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
namespace Saucy.TripleTriad;

internal static class TriadTravelMountUi
{
    public static void Draw()
    {
        ImGui.TextWrapped("Mount used before vnavmesh pathing to Triple Triad NPCs.");
        ImGui.Dummy(new(0, 4));

        var selectedMountId = C.TriadCollection.TravelMountId;
        ImGui.SetNextItemWidth(280f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("##TriadTravelMount", GetPreviewLabel(selectedMountId)))
        {
            if (ImGui.Selectable("Mount roulette", selectedMountId == 0))
            {
                C.TriadCollection.TravelMountId = 0;
                C.Save();
            }

            foreach (var mount in GetOwnedMounts())
            {
                if (ImGui.Selectable(mount.Name, selectedMountId == mount.Id))
                {
                    C.TriadCollection.TravelMountId = mount.Id;
                    C.Save();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Default uses the game's Mount Roulette general action. Pick a mount to always summon that one before map navigation.");
    }

    private static string GetPreviewLabel(uint mountId)
    {
        if (mountId == 0)
        {
            return "Mount roulette";
        }

        var mountSheet = Svc.Data.GetExcelSheet<Mount>();
        var row = mountSheet?.GetRowOrDefault(mountId);
        if (row == null)
        {
            return $"Mount #{mountId} (unavailable)";
        }

        var name = row.Value.Singular.ExtractText();
        if (!TravelMountHelper.IsMountUnlocked(mountId))
        {
            return $"{name} (unavailable)";
        }

        return name;
    }

    private static (uint Id, string Name)[] GetOwnedMounts()
    {
        var mountSheet = Svc.Data.GetExcelSheet<Mount>();

        return
        [
            .. mountSheet
                .Where(mount => mount.RowId != 0 && TravelMountHelper.IsMountUnlocked(mount.RowId))
                .Select(mount => (Id: mount.RowId, Name: mount.Singular.ExtractText()))
                .Where(mount => !string.IsNullOrWhiteSpace(mount.Name))
                .OrderBy(mount => mount.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }
}
