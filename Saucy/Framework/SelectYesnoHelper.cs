using ECommons.Automation.UIInput;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static ECommons.GenericHelpers;
using AgentId = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId;

namespace Saucy.Framework;

public static unsafe class SelectYesnoHelper
{
    public const uint PromptTextNodeId = 2;
    public const uint YesButtonNodeId = 8;
    public const uint NoButtonNodeId = 11;

    private const uint MaxScannedPayoutTextNodeId = 32;

    private static DateTime? armedUntilUtc;

    private static readonly Regex DigitGroupRegex = new(@"[0-9][0-9,]*", RegexOptions.Compiled);

    private static readonly string[] BlockedSystemPromptMarkers =
    [
        "aetheryte",
        "aethernet",
        "ethérite",
        "étheryte",
        "ätheryt",
        "teleport",
        "téléport",
        "teleportieren",
        "テレポ",
        "summoning bell",
        "cloche d'invocation",
        "beschwörungsglocke",
        "discard",
        "jeter",
        "wegwerfen",
        "home point",
        "point de retour",
        "heimpunkt",
        "return home",
        "retour au foyer",
        "heimkehr",
        "party invitation",
        "party invite",
        "invitation dans un groupe",
        "gruppeneinladung",
        "upon release",
        "under release",
        "libération",
        "freigabe"
    ];

    public static bool IsArmed => armedUntilUtc != null && DateTime.UtcNow < armedUntilUtc;

    public static void ArmForYes(TimeSpan window) => armedUntilUtc = DateTime.UtcNow + window;

    public static void Disarm() => armedUntilUtc = null;

    public static bool IsVisible() => TryGetVisible(out var _);

    public static bool TryGetVisible(out AddonSelectYesno* yesno)
    {
        yesno = null;
        for (var i = 1; i < 100; i++)
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno", i).Address;
            if (addon == null)
            {
                return false;
            }

            if (!addon->IsVisible || !IsAddonReady(addon))
            {
                continue;
            }

            yesno = (AddonSelectYesno*)addon;
            return true;
        }

        return false;
    }

    public static bool PressYes(AddonSelectYesno* yesno = null)
    {
        if (!TryResolve(yesno, out yesno))
        {
            return false;
        }

        return PressCallback(yesno, 0, static master => master.Yes());
    }

    public static bool PressNo(AddonSelectYesno* yesno = null)
    {
        if (!TryResolve(yesno, out yesno))
        {
            return false;
        }

        return PressCallback(yesno, 1, static master => master.No());
    }

    public static bool IsBlockedSystemPrompt(AddonSelectYesno* yesno)
    {
        var prompt = GetPromptText(yesno);
        return !string.IsNullOrWhiteSpace(prompt) && PromptContainsAny(prompt, BlockedSystemPromptMarkers);
    }

    public static bool IsArcadeYesno(AddonSelectYesno* yesno) =>
        yesno != null && IsArcadeAddon(&yesno->AtkUnitBase) && HasYesnoButtons(yesno);

    public static bool ShouldPressLotteryYesno(AddonSelectYesno* yesno, AgentId lotteryAgent) =>
        IsSafeMinigameYesno(yesno) &&
        (IsLotteryAgentAddon(&yesno->AtkUnitBase, lotteryAgent) || IsArcadeYesno(yesno));

    public static bool ShouldPressTriadYesno(AddonSelectYesno* yesno) => IsTriadYesno(yesno);

    public static bool IsSafeMinigameYesno(AddonSelectYesno* yesno) =>
        yesno != null &&
        HasYesnoButtons(yesno) &&
        !IsBlockedSystemPrompt(yesno) &&
        !IsTriadYesno(yesno) &&
        !IsTriadYesNoPrompt(yesno) &&
        !IsCuffPlayRoundPrompt(yesno) &&
        !IsArcadeDoubleDownYesno(yesno);

    public static bool IsCuffPlayRoundPrompt(AddonSelectYesno* yesno)
    {
        if (yesno == null || IsBlockedSystemPrompt(yesno) || IsTriadYesNoPrompt(yesno))
        {
            return false;
        }

        var text = GetPromptText(yesno);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!text.Contains("Cuff", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Contains("payout", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("réussite", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Gewinn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Contains("Play", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("round", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("jouer", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("spielen", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("プレイ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTriadYesNoPrompt(AddonSelectYesno* yesno)
    {
        if (IsBlockedSystemPrompt(yesno) || IsArcadeDoubleDownYesno(yesno))
        {
            return false;
        }

        var text = NormalizePrompt(GetPromptText(yesno));
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return PromptContainsAny(text,
        [
            "triad",
            "triade",
            "triplo",
            "トリプル",
            "三重幻卡"
        ]);
    }

    private static bool PromptContainsAny(string prompt, string[] markers)
    {
        foreach (var marker in markers)
        {
            if (prompt.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTriadYesno(AddonSelectYesno* yesno) =>
        yesno != null &&
        HasYesnoButtons(yesno) &&
        !IsBlockedSystemPrompt(yesno) &&
        !IsArcadeAddon(&yesno->AtkUnitBase) &&
        IsTriadAddon(&yesno->AtkUnitBase);

    public static bool TryGetTriadYesno(out AddonSelectYesno* yesno)
    {
        if (!TryGetVisible(out yesno) || !IsTriadYesno(yesno))
        {
            yesno = null;
            return false;
        }

        return true;
    }

    public static bool HasYesnoButtons(AddonSelectYesno* yesno) =>
        TryGetVisibleButtonByNodeId(yesno, YesButtonNodeId, out var _) &&
        TryGetVisibleButtonByNodeId(yesno, NoButtonNodeId, out var _);

    public static bool IsArcadeDoubleDownYesno(AddonSelectYesno* yesno) =>
        yesno != null &&
        IsArcadeYesno(yesno) &&
        HasVisiblePayoutAmount(yesno) &&
        IsOutOnALimbDoubleDownContext();

    public static bool IsArcadeAddon(AtkUnitBase* addon) =>
        AgentHelper.IsAddonOwnedBy(addon, AgentId.GoldSaucerMiniGame);

    public static bool IsLotteryDailyAddon(AtkUnitBase* addon) =>
        AgentHelper.IsAddonOwnedBy(addon, AgentId.LotteryDaily);

    public static bool IsLotteryWeeklyAddon(AtkUnitBase* addon) =>
        AgentHelper.IsAddonOwnedBy(addon, AgentId.LotteryWeekly);

    private static bool IsLotteryAgentAddon(AtkUnitBase* addon, AgentId lotteryAgent) =>
        lotteryAgent switch
        {
            AgentId.LotteryDaily => IsLotteryDailyAddon(addon),
            AgentId.LotteryWeekly => IsLotteryWeeklyAddon(addon),
            var _ => false
        };

    public static bool IsTriadAddon(AtkUnitBase* addon) =>
        AgentHelper.IsAddonOwnedBy(addon, AgentId.TripleTriad);

    private static bool IsModuleManagedYesno(AddonSelectYesno* yesno)
    {
        if (yesno == null || !HasYesnoButtons(yesno))
        {
            return false;
        }

        var addon = &yesno->AtkUnitBase;
        return IsTriadAddon(addon) ||
               IsArcadeAddon(addon) ||
               IsLotteryDailyAddon(addon) ||
               IsLotteryWeeklyAddon(addon);
    }

    public static bool IsRouteSafeYesno(AddonSelectYesno* yesno) =>
        yesno != null &&
        HasYesnoButtons(yesno) &&
        !IsBlockedSystemPrompt(yesno) &&
        !IsModuleManagedYesno(yesno);

    private static bool IsOutOnALimbDoubleDownContext() =>
        TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon);

    private static bool HasVisiblePayoutAmount(AddonSelectYesno* yesno)
    {
        var numericValues = new List<int>();
        TryCollectDigitGroupsFromTextNode(yesno->AtkTextNode298, numericValues);
        TryCollectDigitGroupsFromTextNode(yesno->PromptText, numericValues);

        for (uint nodeId = 1; nodeId <= MaxScannedPayoutTextNodeId; nodeId++)
        {
            TryCollectDigitGroupsFromTextNode(yesno->AtkUnitBase.GetTextNodeById(nodeId), numericValues);
        }

        foreach (var value in numericValues)
        {
            if (value is >= 10 and <= 9999)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryCollectDigitGroupsFromTextNode(AtkTextNode* textNode, List<int> values)
    {
        if (textNode == null || !((AtkResNode*)textNode)->IsVisible())
        {
            return;
        }

        var text = textNode->NodeText.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in DigitGroupRegex.Matches(text))
        {
            var digits = match.Value.Replace(",", string.Empty);
            if (int.TryParse(digits, out var value) && value > 0)
            {
                values.Add(value);
            }
        }
    }

    public static bool TryPressArmedYes()
    {
        if (!IsArmed || !TryGetVisible(out var yesno) || IsBlockedSystemPrompt(yesno))
        {
            return false;
        }

        if (!PressYes(yesno))
        {
            return false;
        }

        Disarm();
        return true;
    }

    private static bool TryResolve(AddonSelectYesno* yesno, out AddonSelectYesno* resolved)
    {
        if (yesno != null && IsAddonReady(&yesno->AtkUnitBase))
        {
            resolved = yesno;
            return true;
        }

        return TryGetVisible(out resolved);
    }

    private static string NormalizePrompt(string text) =>
        text.Replace(" ", string.Empty).Replace("\u00A0", string.Empty);

    private static string GetPromptText(AddonSelectYesno* yesno)
    {
        if (yesno->PromptText != null)
        {
            return yesno->PromptText->NodeText.GetText();
        }

        var textNode = yesno->AtkUnitBase.GetTextNodeById(PromptTextNodeId);
        if (textNode == null)
        {
            return string.Empty;
        }

        return textNode->NodeText.ToString();
    }

    private static bool TryGetVisibleButtonByNodeId(AddonSelectYesno* yesno, uint nodeId, out AtkComponentButton* button)
    {
        button = null;
        if (yesno == null)
        {
            return false;
        }

        button = yesno->AtkUnitBase.GetComponentButtonById(nodeId);
        if (button == null || button->AtkResNode == null)
        {
            return false;
        }

        return button->AtkResNode->IsVisible();
    }

    private static bool PressCallback(AddonSelectYesno* yesno, int callbackId, Action<AddonMaster.SelectYesno> fallback)
    {
        try
        {
            yesno->FireCallbackInt(callbackId);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectYesno] FireCallbackInt({callbackId}) failed; falling back to AddonMaster");
        }

        try
        {
            fallback(new(yesno));
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectYesno] AddonMaster fallback failed for callback {callbackId}");
        }

        var nodeId = callbackId == 0 ? YesButtonNodeId : NoButtonNodeId;
        if (TryGetVisibleButtonByNodeId(yesno, nodeId, out var button))
        {
            try
            {
                button->ClickAddonButton(&yesno->AtkUnitBase);
                yesno->AtkUnitBase.Update(0);
                return true;
            }
            catch (Exception ex)
            {
                Svc.Log.Verbose(ex, $"[SelectYesno] Button node {nodeId} click failed for callback {callbackId}");
            }
        }

        return false;
    }
}
