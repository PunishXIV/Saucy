using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using Saucy.AirForce;
using Saucy.CuffACur;
using Saucy.JumboCactpot;
using Saucy.KeyboundBrawler;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;
namespace Saucy;

public unsafe partial class PluginUI
{
    private void DrawCuffPanel()
    {
        DrawPanelHeader(Loc.T("Cuff-a-Cur"), Loc.T("punch the cactuar"));
        if (C.ShowDebugUi)
        {
            ImGuiEx.EzTabBar("###Cuff",
                ($"{Loc.T("Main")}###SaucyCuffMain", CuffACurAutomation.DrawSettings, null, false),
                ($"{Loc.T("Debug")}###SaucyCuffDebug", CuffACurAutomation.DrawDebug, null, false));
        }
        else
        {
            CuffACurAutomation.DrawSettings();
        }
    }

    private void DrawLimbPanel()
    {
        DrawPanelHeader(Loc.T("Out on a Limb"), Loc.T("swing the hatchet"));
        if (C.ShowDebugUi)
        {
            ImGuiEx.EzTabBar("###Limb",
                ($"{Loc.T("Main")}###SaucyLimbMain", P.LimbManager.DrawSettings, null, false),
                ($"{Loc.T("Debug")}###SaucyLimbDebug", P.LimbManager.DrawDebug, null, false));
        }
        else
        {
            P.LimbManager.DrawSettings();
        }
    }

    private static void DrawSliceIsRightPanel()
    {
        DrawPanelHeader(Loc.T("Slice is Right"), Loc.T("dodge the falling slices"));
        var enabled = C.IsModuleEnabled(ModuleNames.SliceIsRight);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucySliceEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.SliceIsRight, enabled);
            C.Save();
        }

        ImGui.TextWrapped(Loc.T("Draws slice and AoE markers during the GATE."));

        if (enabled)
        {
            using var indent = ImRaii.PushIndent();
            var autoMove = C.GoldSaucerGates.SliceIsRightAutoMovement;
            if (ImGui.Checkbox($"{Loc.T("Automatic movement (Boss Mod VBM AI)")}###SaucySliceAuto", ref autoMove))
            {
                C.GoldSaucerGates.SliceIsRightAutoMovement = autoMove;
                C.Save();
            }

            if (autoMove)
            {
                SaucyTheme.TextMuted(Loc.T("Activates the VBM AI preset so Boss Mod's Slice is Right module can path you out of hazards."));
            }
        }

        ImGui.Dummy(new(0, 4));
        SaucyTheme.DrawCard(Loc.T("Dependencies"), Loc.T("Optional integrations"), DrawSliceIsRightDependencies);
    }

    private static void DrawGateRunnerPanel()
    {
        DrawPanelHeader(Loc.T(ModuleDisplayNames.GateRunner), Loc.T("travel to and play supported GATEs"));
        ImGui.TextWrapped(Loc.T(
            "Cycles through enabled GATEs: Air Force One, Slice is Right, and Any Way the Wind Blows. " +
            "Requires vnavmesh. Slice also needs Boss Mod and automatic movement. Wind needs automatic movement."));

        var activity = C.GoldSaucerGates.BetweenGates;
        if (ImGui.RadioButton($"{Loc.T("Cuff-a-Cur between GATEs")}###SaucyGateRunnerCuff", activity == BetweenGatesActivity.CuffACur))
        {
            C.GoldSaucerGates.BetweenGates = BetweenGatesActivity.CuffACur;
            C.Save();
        }

        if (ImGui.RadioButton($"{Loc.T("Out on a Limb between GATEs")}###SaucyGateRunnerLimb", activity == BetweenGatesActivity.OutOnALimb))
        {
            C.GoldSaucerGates.BetweenGates = BetweenGatesActivity.OutOnALimb;
            C.Save();
        }

        if (ImGui.RadioButton($"{Loc.T("Wait between GATEs")}###SaucyGateRunnerIdle", activity == BetweenGatesActivity.Idle))
        {
            C.GoldSaucerGates.BetweenGates = BetweenGatesActivity.Idle;
            C.Save();
        }

        if (GoldSaucerGateRunner.IsRunning)
        {
            if (ImGui.Button($"{Loc.T("Stop runner")}###SaucyGateRunnerStop"))
            {
                GoldSaucerGateRunner.Stop();
            }
        }
        else if (ImGui.Button($"{Loc.T("Start runner")}###SaucyGateRunnerStart"))
        {
            GoldSaucerGateRunner.Start();
        }

        ImGui.TextDisabled(GoldSaucerGateRunner.Status);
    }

    private static void DrawWindBlowsPanel()
    {
        DrawPanelHeader(Loc.T("Any Way the Wind Blows"), Loc.T("statistical safe spot"));
        var enabled = C.IsModuleEnabled(ModuleNames.AnyWayTheWindBlows);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucyWindEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.AnyWayTheWindBlows, enabled);
            C.Save();
        }

        ImGui.TextWrapped(Loc.T("Shows the statistical safe spot during the GATE."));

        if (enabled)
        {
            using var indent = ImRaii.PushIndent();
            var autoMove = C.GoldSaucerGates.WindBlowsAutoMovement;
            if (ImGui.Checkbox($"{Loc.T("Automatic movement (vnavmesh)")}###SaucyWindAuto", ref autoMove))
            {
                C.GoldSaucerGates.WindBlowsAutoMovement = autoMove;
                C.Save();
            }

            if (autoMove)
            {
                SaucyTheme.TextMuted(Loc.T("Pathfinds you onto the safe spot while you are off it."));
            }
        }

        ImGui.Dummy(new(0, 4));
        SaucyTheme.DrawCard(Loc.T("Dependencies"), Loc.T("Optional integrations"), DrawWindBlowsDependencies);
    }

    private static void DrawAirForcePanel()
    {
        DrawPanelHeader(Loc.T("Air Force One"), Loc.T("ride shooting minigame"));
        DrawAirForceMain();
    }

    private static void DrawKeyboundBrawlerPanel()
    {
        DrawPanelHeader(Loc.T(ModuleDisplayNames.KeyboundBrawler), Loc.T("word action minigame"));
        var enabled = C.IsModuleEnabled(ModuleNames.KeyboundBrawler);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucyBrawlerEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.KeyboundBrawler, enabled);
            C.Save();
        }

        var autoQueue = C.KeyboundBrawlerAutoQueue;
        if (ImGui.Checkbox($"{Loc.T("Enable auto-queue")}###SaucyBrawlerAutoQueue", ref autoQueue))
        {
            C.KeyboundBrawlerAutoQueue = autoQueue;
            C.Save();
        }

        if (autoQueue) ImGui.TextDisabled(Loc.T(KeyboundBrawlerAutomation.QueueStatus));

        DrawKeyboundBrawlerStopConditions();

        var requeueDelay = C.KeyboundBrawlerRequeueDelaySeconds;
        if (ImGui.InputInt($"{Loc.T("Delay before next queue (seconds)")}###SaucyBrawlerRequeueDelay", ref requeueDelay))
        {
            C.KeyboundBrawlerRequeueDelaySeconds = Math.Max(0, requeueDelay);
            C.Save();
        }

        var min = C.KeyboundBrawlerMinMsPerCharacter;
        if (ImGui.InputInt($"{Loc.T("Minimum typing time per character (ms)")}###SaucyBrawlerMinDelay", ref min))
        {
            C.KeyboundBrawlerMinMsPerCharacter = Math.Clamp(min, 100, 2000);
            C.KeyboundBrawlerMaxMsPerCharacter = Math.Clamp(C.KeyboundBrawlerMaxMsPerCharacter, C.KeyboundBrawlerMinMsPerCharacter, 2000);
            C.Save();
        }

        var max = C.KeyboundBrawlerMaxMsPerCharacter;
        if (ImGui.InputInt($"{Loc.T("Maximum typing time per character (ms)")}###SaucyBrawlerMaxDelay", ref max))
        {
            C.KeyboundBrawlerMaxMsPerCharacter = Math.Clamp(max, C.KeyboundBrawlerMinMsPerCharacter, 2000);
            C.Save();
        }
        if (KeyboundBrawlerAutomation.ScanFailed)
        {
            ImGui.TextDisabled(Loc.T("This game version is not supported by the current action signature."));
        }
    }

    private static void DrawKeyboundBrawlerStopConditions()
    {
        var stopAtCap = C.KeyboundBrawlerStopAtMgcCap;
        if (ImGui.Checkbox($"{Loc.T("Stop at the MGC cap of {0}", KeyboundBrawlerAutomation.MgcCap)}###SaucyBrawlerStopAtCap", ref stopAtCap))
        {
            C.KeyboundBrawlerStopAtMgcCap = stopAtCap;
            C.Save();
        }

        if (stopAtCap)
        {
            var currentMgc = InventoryManager.Instance()->GetInventoryItemCount(KeyboundBrawlerAutomation.MgcItemId, false, false, false);
            ImGui.TextDisabled($"{currentMgc}/{KeyboundBrawlerAutomation.MgcCap}");
        }

        var playXTimes = C.KeyboundBrawlerPlayXTimes;
        if (ImGui.Checkbox($"{Loc.T("Fixed match count")}###SaucyBrawlerFixedCount", ref playXTimes))
        {
            C.KeyboundBrawlerPlayXTimes = playXTimes;
            C.Save();
        }

        if (!playXTimes)
        {
            return;
        }

        ImGui.Text(Loc.T("How many times:"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(GoldSaucerRunSettingsUi.CompactCountInputWidth * ImGuiHelpers.GlobalScale);
        var count = Math.Max(1, C.KeyboundBrawlerMatchCount);
        if (ImGui.InputInt("###SaucyBrawlerPlayCount", ref count))
        {
            C.KeyboundBrawlerMatchCount = Math.Max(1, count);
            C.Save();
        }

        ImGui.TextDisabled(Loc.T("Matches left this session: {0}", KeyboundBrawlerAutomation.RemainingMatches));
    }

    private static void DrawAirForceMain()
    {
        var enabled = C.IsModuleEnabled(ModuleNames.AirForceOne);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucyAirForceEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.AirForceOne, enabled);
            if (!enabled)
            {
                AirForceAutomation.ClearRewardTracking();
            }

            C.Save();
        }

        ImGui.TextWrapped(Loc.T("Runs automatically when enabled. Plays the Air Force One ride-shooting minigame for you."));
    }

    private static void DrawMiniCactpotPanel()
    {
        DrawPanelHeader(Loc.T("Mini-Cactpot"), Loc.T("daily 3\u00d73 scratcher"));
        var enabled = C.IsModuleEnabled(ModuleNames.MiniCactpot);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucyMiniEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.MiniCactpot, enabled);
            C.Save();
            if (ModuleManager.GetModule<MiniCactpot.MiniCactpot>() is { } miniCactpot)
            {
                if (enabled && !miniCactpot.IsEnabled)
                {
                    miniCactpot.EnableInternal();
                }
                else if (!enabled && miniCactpot.IsEnabled)
                {
                    miniCactpot.DisableInternal();
                }
            }
        }

        ImGui.TextWrapped(Loc.T("Plays Mini Cactpot automatically when you open the daily scratcher at the Gold Saucer."));
    }

    private static void DrawJumboCactpotPanel()
    {
        DrawPanelHeader(Loc.T("Jumbo Cactpot"), Loc.T("weekly 4-digit raffle"));
        var enabled = C.IsModuleEnabled(ModuleNames.JumboCactpot);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucyJumboEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.JumboCactpot, enabled);
            C.Save();
            if (ModuleManager.GetModule<JumboCactpot.JumboCactpot>() is { } jumboCactpot)
            {
                if (enabled && !jumboCactpot.IsEnabled)
                {
                    jumboCactpot.EnableInternal();
                }
                else if (!enabled && jumboCactpot.IsEnabled)
                {
                    jumboCactpot.DisableInternal();
                }
            }
        }

        ImGui.TextWrapped(Loc.T(
            "Collect prizes at the Cactpot cashier yourself. Saucy then paths you to the Jumbo " +
            "broker and handles ticket purchase dialogue and confirms."));

        ImGui.Spacing();
        ImGui.TextUnformatted(Loc.T("Number selection"));
        var numberMode = C.JumboCactpot.NumberMode;
        var save = false;
        if (ImGui.RadioButton($"{Loc.T("Random")}###SaucyJumboNumbersRandom", numberMode == JumboCactpotNumberMode.Random))
        {
            numberMode = JumboCactpotNumberMode.Random;
            save = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton($"{Loc.T("Specific numbers")}###SaucyJumboNumbersSpecific", numberMode == JumboCactpotNumberMode.Specific))
        {
            numberMode = JumboCactpotNumberMode.Specific;
            save = true;
        }

        if (save)
        {
            C.JumboCactpot.NumberMode = numberMode;
            C.Save();
        }

        var specificEnabled = numberMode == JumboCactpotNumberMode.Specific;
        if (!specificEnabled)
        {
            ImGui.BeginDisabled();
        }

        var ticket1 = C.JumboCactpot.Ticket1Number;
        var ticket2 = C.JumboCactpot.Ticket2Number;
        var ticket3 = C.JumboCactpot.Ticket3Number;
        save |= DrawJumboTicketNumberField(Loc.T("Ticket 1 (100 MGP)"), ref ticket1);
        save |= DrawJumboTicketNumberField(Loc.T("Ticket 2 (150 MGP)"), ref ticket2);
        save |= DrawJumboTicketNumberField(Loc.T("Ticket 3 (200 MGP)"), ref ticket3);
        if (save)
        {
            C.JumboCactpot.Ticket1Number = ticket1;
            C.JumboCactpot.Ticket2Number = ticket2;
            C.JumboCactpot.Ticket3Number = ticket3;
        }

        if (!specificEnabled)
        {
            ImGui.EndDisabled();
        }

        if (save)
        {
            C.Save();
        }

        if (specificEnabled)
        {
            ImGui.TextDisabled(Loc.T("Leave a ticket blank to randomize that purchase."));
        }
    }

    private static bool DrawJumboTicketNumberField(string label, ref string value)
    {
        var buffer = value ?? string.Empty;
        if (buffer.Length > 4)
        {
            buffer = buffer[..4];
        }

        if (!ImGui.InputText(label, ref buffer, 4, ImGuiInputTextFlags.CharsDecimal))
        {
            return false;
        }

        buffer = buffer.Trim();
        if (string.Equals(buffer, value, StringComparison.Ordinal))
        {
            return false;
        }

        value = buffer;
        return true;
    }

    private static void DrawJumboCactpotDebugPanel()
    {
        ImGuiLayout.DrawCollapsingSection(Loc.T("Jumbo Cactpot input"), "SaucyJumboInputDebug", ImGuiTreeNodeFlags.DefaultOpen, () =>
        {
            if (!TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>(
                    "LotteryWeeklyInput",
                    out var addon) ||
                !IsAddonReady(addon) ||
                !addon->IsVisible)
            {
                ImGui.TextDisabled(Loc.T("Open the Jumbo ticket purchase window to inspect addon nodes."));
                return;
            }

            var lines = new List<string>();
            LotteryWeeklyInputHelper.CollectDebugLines(addon, lines);
            var listHeight = Math.Clamp(lines.Count * ImGui.GetTextLineHeightWithSpacing() + 8f, 60f, 260f);
            using var scroll = ImRaii.Child("##JumboInputDebug", new(0, listHeight), true);
            if (scroll)
            {
                foreach (var line in lines)
                {
                    ImGui.TextUnformatted(line);
                }
            }
        });
    }

    private static BannerInfo BuildBannerInfo()
    {
        var im = InventoryManager.Instance();
        var mgp = im != null ? im->GetInventoryItemCount(MgpItemId, false, false, false) : 0;

        string status;
        if (GoldSaucerGateRunner.IsRunning)
        {
            status = ModuleDisplayNames.GateRunner;
        }
        else if (TriadRunSession.ModuleEnabled)
        {
            status = ModuleDisplayNames.TripleTriad;
        }
        else if (GoldSaucerGameActivity.IsBrawlerSessionActive())
        {
            status = ModuleDisplayNames.KeyboundBrawler;
        }
        else if (CuffACurAutomation.IsEnabled)
        {
            status = ModuleDisplayNames.CuffACur;
        }
        else if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            status = ModuleDisplayNames.OutOnALimb;
        }
        else if (C.IsModuleEnabled(ModuleNames.SliceIsRight))
        {
            status = ModuleDisplayNames.SliceIsRight;
        }
        else if (C.IsModuleEnabled(ModuleNames.AnyWayTheWindBlows))
        {
            status = ModuleDisplayNames.AnyWayTheWindBlows;
        }
        else if (C.IsModuleEnabled(ModuleNames.AirForceOne))
        {
            status = ModuleDisplayNames.AirForceOne;
        }
        else if (C.IsModuleEnabled(ModuleNames.MiniCactpot))
        {
            status = ModuleDisplayNames.MiniCactpot;
        }
        else if (C.IsModuleEnabled(ModuleNames.JumboCactpot))
        {
            status = ModuleDisplayNames.JumboCactpot;
        }
        else
        {
            status = ModuleDisplayNames.Idle;
        }

        var sessionDelta = C.SessionStats.MGPWon + C.SessionStats.CuffMGP + C.SessionStats.LimbMGP +
                           C.SessionStats.AirForceMGP + C.SessionStats.BrawlerMGP;

        return new()
        {
            Mgp = mgp, SessionDelta = sessionDelta, ModuleStatus = status
        };
    }

    private static void DrawSliceIsRightDependencies() =>
        PluginDependenciesUi.Draw(
            Loc.T("Optional plugin for automatic dodging during the GATE. Overlays still work without it."),
            [
                PluginDependenciesUi.BossModPlugin(
                    Loc.T("Provides the Slice is Right boss module (hazard zones) and the VBM AI preset Saucy activates during the GATE. " +
                          "Keep the Gold Saucer Slice is Right module enabled in Boss Mod settings."))
            ]);

    private static void DrawWindBlowsDependencies() =>
        PluginDependenciesUi.Draw(
            Loc.T("Optional plugin for automatic movement to the safe spot. Overlays still work without it."),
            [
                PluginDependenciesUi.Vnavmesh(
                    Loc.T("Pathfinds you onto the statistical safe spot during the GATE."))
            ]);
}
