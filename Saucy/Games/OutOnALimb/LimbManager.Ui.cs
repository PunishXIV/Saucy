using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using ECommons.Automation.UIInput;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System.Linq;
using static ECommons.GenericHelpers;
namespace Saucy.OutOnALimb;

public unsafe partial class LimbManager
{
    public void DrawSettings()
    {
        var save = false;

        var enabled = C.IsModuleEnabled(ModuleNames.OutOnALimb);
        if (ImGui.Checkbox("Enable", ref enabled))
        {
            if (enabled && !IsAnyLimbMachineInRange())
            {
                DuoLog.Warning("No Out on a Limb machine nearby (maybe get closer if in front of one).");
            }
            else
            {
                if (enabled)
                {
                    GoldSaucerRunSettingsUi.CommitDraftMatchCount(Machine);
                }

                ToggleModule(enabled);
                save |= true;
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Walk up to the Out on a Limb machine, set how many games to play, and Saucy runs them.");

        ImGui.Dummy(new(0, 4));

        GoldSaucerRunSettingsUi.Draw(
            GoldSaucerArcadeMachine.Limb,
            "Runs automatically when enabled at the Out on a Limb machine.");

        SaucyTheme.DrawCard("Options", null, () =>
        {
            ImGui.Checkbox("Stop at next double-down", ref Exit);
            ImGui.TextDisabled("Cashes out at the next double-down and disables automation after the reward.");
            ImGui.TextDisabled("Duty Finder ready also cashes out but leaves automation enabled.");
        });

        SaucyTheme.DrawCard("Tuning", null, () =>
        {
            ImGui.SetNextItemWidth(120f);
            save |= ImGuiEx.EnumCombo("Difficulty", ref Cfg.LimbDifficulty);

            ImGui.SetNextItemWidth(120f);
            save |= ImGui.DragInt("Step", ref Cfg.Step, 0.05f);
            ImGui.SameLine();
            if (ImGui.Button("Default##step"))
            {
                Cfg.Step = new LimbConfig().Step;
                save = true;
            }

            ImGui.SetNextItemWidth(120f);
            save |= ImGui.DragInt("Min seconds for another round", ref Cfg.MinSecondsForAnotherRound, 0.5f);
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "Always double down while the minigame timer is above this. Cash out when there is not enough time left for another round.");
        });

        if (save)
        {
            C.Save();
        }
    }

    public void DrawDebug()
    {
        {
            if (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var addon) && IsAddonReady(addon))
            {
                var reference = addon->GetNodeById(NodeIDs[Cfg.LimbDifficulty]);
                var cursor = addon->GetNodeById(39);
                var iCursor = 400 - cursor->Height;
                if (iCursor > reference->Y && iCursor < reference->Y + Heights[Cfg.LimbDifficulty])
                {
                    ImGuiEx.Text("Yes");
                }

                ImGuiEx.Text($"Reference: {reference->Y}");
                ImGuiEx.Text($"Cursor: {cursor->Height}");
            }
        }
        {
            if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
            {
                var button = addon->GetComponentButtonById(24);
                var cursor = GetCursor();
                ImGuiEx.Text($"Cursor: {cursor}");
                ImGui.Checkbox("Only request", ref OnlyRequest);
                ImGui.SetNextItemWidth(100f);
                ImGui.InputInt("Request input", ref RequestInput);
                ImGui.SameLine();
                if (ImGui.Button("Request"))
                {
                    Request = RequestInput;
                }

                ImGui.SameLine();
                if (ImGui.Button("Reset"))
                {
                    Request = null;
                }

                ImGuiEx.Text($"Button enabled: {button->IsEnabled}");
                ImGuiEx.Text($"Seconds remaining: {LimbArcadeTimer.TryGetSecondsRemaining()?.ToString() ?? "n/a"}");
                {
                    var na = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.GoldSaucerArcadeMachine);
                    if (na != null)
                    {
                        var parts = new string[8];
                        for (var i = 0; i < 8; i++)
                        {
                            parts[i] = $"[{i}]={na->IntArray[i]}";
                        }
                        ImGuiEx.Text($"NumberArray.GoldSaucerArcadeMachine: {string.Join(", ", parts)}");
                    }
                }
                ImGuiEx.Text($"Health: {GetHealth(addon)}");
                ImGuiEx.Text($"Hit pending: {GetHitPending(addon)}");
                ImGuiEx.Text($"Pending: {PendingCursor}, previous health: {PreviousHealth}");
                if (ImGui.Button("Click"))
                {
                    if (button->IsEnabled)
                    {
                        PendingCursor = cursor;
                        PreviousHealth = GetHealth(addon);
                        button->ClickAddonButton(addon);
                    }
                }

                ImGuiEx.Text($"Next: {Next}, MinIndex: {MinIndex}, rec={RecordMinIndex}");
                ImGuiEx.Text($"Starting points:\n{StartingPoints.Print()}");
                ImGuiEx.Text($"Results:\n{Results.Select(x => $"{x.Position}={x.Power}").Print("\n")}");
            }
        }
        {
            if (SelectStringHelper.TryGetArcadeMenu(out var startMenu))
            {
                var entryCount = SelectStringHelper.TryGetArcadeMenuEntryCount(startMenu, out var count) ? count : -1;
                ImGuiEx.Text(
                    $"SelectString arcade yes/no={SelectStringHelper.IsArcadeYesnoMenu(startMenu)} entries={entryCount}");
            }

            ImGuiEx.Text($"Play X: {GoldSaucerArcadeRunSession.PlayXTimes(Machine)}");
            ImGuiEx.Text($"Interact pending: {ArcadeMachineSession.IsInteractPending(Machine)}");
            if (ArcadeMachineSession.GetInteractPendingAge(Machine) is { } interactAge)
            {
                ImGuiEx.Text($"Interact pending age: {interactAge.TotalSeconds:F1}s");
            }
            ImGuiEx.Text($"Pending shutdown: {ArcadeMachineSession.IsPendingShutdown(Machine)}");
            ImGuiEx.Text($"Playing final round: {ArcadeMachineSession.IsPlayingFinalRound(Machine)}");
            ImGuiEx.Text($"Remaining: {GoldSaucerArcadeRunSession.GetRemaining(Machine)}");
            ImGuiEx.Text($"Stop at double down (Exit): {Exit}");
            ImGuiEx.Text($"Can automate yesno: {CanAutomateLimbYesno()}");
            ImGuiEx.Text($"Duty finder defer: {GoldSaucerArcadeRunSession.IsStopForDutyFinder(Machine)}");
            if (SelectYesnoHelper.TryGetVisible(out var yesno))
            {
                var isDoubleDown = SelectYesnoHelper.IsArcadeDoubleDownYesno(yesno);
                ImGuiEx.Text($"SelectYesno visible — double-down={isDoubleDown}");
            }
            else
            {
                ImGuiEx.Text("SelectYesno: not visible");
            }
        }
    }
}
