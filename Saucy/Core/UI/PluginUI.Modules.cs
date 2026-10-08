using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using Saucy.AirForce;
using Saucy.CuffACur;
using Saucy.JumboCactpot;
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
        if (TriadRunSession.ModuleEnabled)
        {
            status = ModuleDisplayNames.TripleTriad;
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
                           C.SessionStats.AirForceMGP;

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
