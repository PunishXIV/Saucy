using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using ECommons.Logging;
namespace Saucy.CuffACur;

public partial class CuffACurAutomation
{
    public static void DrawSettings()
    {
        var enabled = IsEnabled;
        if (ImGui.Checkbox("Enable", ref enabled))
        {
            if (enabled && !IsAnyCuffMachineInRange())
            {
                DuoLog.Warning("No Cuff-a-Cur machine nearby (maybe get closer if in front of one).");
            }
            else
            {
                if (enabled)
                {
                    GoldSaucerRunSettingsUi.CommitDraftMatchCount(GoldSaucerArcadeMachine.Cuff);
                    GoldSaucerArcadeMachineHelper.DisableConflictingModules(GoldSaucerArcadeMachine.Cuff);
                }

                C.SetModuleEnabled(ModuleNames.CuffACur, enabled);
                C.Save();
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Runs automatically at the Gold Saucer punching machine. Use Play X below to stop after a set number of games.");

        ImGui.Dummy(new(0, 4));

        GoldSaucerRunSettingsUi.Draw(
            GoldSaucerArcadeMachine.Cuff,
            "Runs automatically when enabled. Start the minigame at the Gold Saucer punching machine.");
    }
}
