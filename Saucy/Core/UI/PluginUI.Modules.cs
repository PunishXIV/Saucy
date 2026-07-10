using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;
using Saucy.AirForce;
using Saucy.CuffACur;
using Saucy.JumboCactpot;
using Saucy.OtherGames;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;
namespace Saucy;

public unsafe partial class PluginUI
{
    private void DrawCuffPanel()
    {
        DrawPanelHeader("Cuff-a-Cur", "punch the cactuar");
        if (C.ShowDebugUi)
        {
            ImGuiEx.EzTabBar("###Cuff",
                ("Main", CuffACurAutomation.DrawSettings, null, false),
                ("Debug", CuffACurAutomation.DrawDebug, null, false));
        }
        else
        {
            CuffACurAutomation.DrawSettings();
        }
    }

    private void DrawLimbPanel()
    {
        DrawPanelHeader("Out on a Limb", "swing the hatchet");
        if (C.ShowDebugUi)
        {
            ImGuiEx.EzTabBar("###Limb",
                ("Main", P.LimbManager.DrawSettings, null, false),
                ("Debug", P.LimbManager.DrawDebug, null, false));
        }
        else
        {
            P.LimbManager.DrawSettings();
        }
    }

    private static void DrawSliceIsRightPanel()
    {
        DrawPanelHeader("Slice is Right", "dodge the falling slices");
        var enabled = C.IsModuleEnabled(ModuleNames.SliceIsRight);
        if (ImGui.Checkbox("Enable##Slice", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.SliceIsRight, enabled);
            C.Save();
        }

        ImGui.TextWrapped("Draws slice and AoE markers during the GATE.");

        if (enabled)
        {
            using var indent = ImRaii.PushIndent();
            var autoMove = C.GoldSaucerGates.SliceIsRightAutoMovement;
            if (ImGui.Checkbox("Automatic movement (Boss Mod VBM AI)##SliceAuto", ref autoMove))
            {
                C.GoldSaucerGates.SliceIsRightAutoMovement = autoMove;
                C.Save();
            }

            if (autoMove)
            {
                SaucyTheme.TextMuted("Activates the VBM AI preset so Boss Mod's Slice is Right module can path you out of hazards.");
            }
        }

        ImGui.Dummy(new(0, 4));
        SaucyTheme.DrawCard("Dependencies", "Optional integrations", GoldSaucerGateDependenciesUi.DrawSliceIsRight);
    }

    private static void DrawWindBlowsPanel()
    {
        DrawPanelHeader("Any Way the Wind Blows", "statistical safe spot");
        var enabled = C.IsModuleEnabled(ModuleNames.AnyWayTheWindBlows);
        if (ImGui.Checkbox("Enable##Wind", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.AnyWayTheWindBlows, enabled);
            C.Save();
        }

        ImGui.TextWrapped("Shows the statistical safe spot during the GATE.");

        if (enabled)
        {
            using var indent = ImRaii.PushIndent();
            var autoMove = C.GoldSaucerGates.WindBlowsAutoMovement;
            if (ImGui.Checkbox("Automatic movement (vnavmesh)##WindAuto", ref autoMove))
            {
                C.GoldSaucerGates.WindBlowsAutoMovement = autoMove;
                C.Save();
            }

            if (autoMove)
            {
                SaucyTheme.TextMuted("Pathfinds you onto the safe spot while you are off it.");
            }
        }

        ImGui.Dummy(new(0, 4));
        SaucyTheme.DrawCard("Dependencies", "Optional integrations", GoldSaucerGateDependenciesUi.DrawWindBlows);
    }

    private static void DrawAirForcePanel()
    {
        DrawPanelHeader("Air Force One", "ride shooting minigame");
        if (C.ShowDebugUi)
        {
            ImGuiEx.EzTabBar("###AirForce",
                ("Main", DrawAirForceMain, null, false),
                ("Debug", AirForceAutomation.DrawDebug, null, false));
        }
        else
        {
            DrawAirForceMain();
        }
    }

    private static void DrawAirForceMain()
    {
        var enabled = C.IsModuleEnabled(ModuleNames.AirForceOne);
        if (ImGui.Checkbox("Enable##AirForce", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.AirForceOne, enabled);
            if (!enabled)
            {
                AirForceAutomation.ClearRewardTracking();
            }

            C.Save();
        }

        ImGui.TextWrapped("Runs automatically when enabled. Plays the Air Force One ride-shooting minigame for you.");
    }

    private static void DrawMiniCactpotPanel()
    {
        DrawPanelHeader("Mini-Cactpot", "daily 3\u00d73 scratcher");
        var enabled = C.IsModuleEnabled(ModuleNames.MiniCactpot);
        if (ImGui.Checkbox("Enable##Mini", ref enabled))
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

        ImGui.TextWrapped("Plays Mini Cactpot automatically when you open the daily scratcher at the Gold Saucer.");
    }

    private static void DrawJumboCactpotPanel()
    {
        DrawPanelHeader("Jumbo Cactpot", "weekly 4-digit raffle");
        var enabled = C.IsModuleEnabled(ModuleNames.JumboCactpot);
        if (ImGui.Checkbox("Enable##Jumbo", ref enabled))
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

        ImGui.TextWrapped(
            "Collect prizes at the Cactpot cashier yourself. Saucy then paths you to the Jumbo " +
            "broker and handles ticket purchase dialogue and confirms.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Number selection");
        var numberMode = C.JumboCactpot.NumberMode;
        var save = false;
        if (ImGui.RadioButton("Random##JumboNumbers", numberMode == JumboCactpotNumberMode.Random))
        {
            numberMode = JumboCactpotNumberMode.Random;
            save = true;
        }

        ImGui.SameLine();
        if (ImGui.RadioButton("Specific numbers##JumboNumbers", numberMode == JumboCactpotNumberMode.Specific))
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
        save |= DrawJumboTicketNumberField("Ticket 1 (100 MGP)", ref ticket1);
        save |= DrawJumboTicketNumberField("Ticket 2 (150 MGP)", ref ticket2);
        save |= DrawJumboTicketNumberField("Ticket 3 (200 MGP)", ref ticket3);
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
            ImGui.TextDisabled("Leave a ticket blank to randomize that purchase.");
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
        ImGuiLayout.DrawCollapsingSection("Jumbo Cactpot input", ImGuiTreeNodeFlags.DefaultOpen, () =>
        {
            if (!TryGetAddonByName<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>(
                    "LotteryWeeklyInput",
                    out var addon) ||
                !IsAddonReady(addon) ||
                !addon->IsVisible)
            {
                ImGui.TextDisabled("Open the Jumbo ticket purchase window to inspect addon nodes.");
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
            status = "Triple Triad";
        }
        else if (CuffACurAutomation.IsEnabled)
        {
            status = "Cuff-a-Cur";
        }
        else if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            status = "Out on a Limb";
        }
        else if (C.IsModuleEnabled(ModuleNames.SliceIsRight))
        {
            status = "Slice is Right";
        }
        else if (C.IsModuleEnabled(ModuleNames.AnyWayTheWindBlows))
        {
            status = "Any Way the Wind Blows";
        }
        else if (C.IsModuleEnabled(ModuleNames.AirForceOne))
        {
            status = "Air Force One";
        }
        else if (C.IsModuleEnabled(ModuleNames.MiniCactpot))
        {
            status = "Mini-Cactpot";
        }
        else if (C.IsModuleEnabled(ModuleNames.JumboCactpot))
        {
            status = "Jumbo Cactpot";
        }
        else
        {
            status = "Idle";
        }

        var sessionDelta = C.SessionStats.MGPWon + C.SessionStats.CuffMGP + C.SessionStats.LimbMGP +
                           C.SessionStats.AirForceMGP;

        return new()
        {
            Mgp = mgp, SessionDelta = sessionDelta, ModuleStatus = status
        };
    }
}
