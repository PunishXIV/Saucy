using ECommons.Automation;
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

    /// <summary>Standard SelectYesno (Yes=8, No=11). Skip when ticket layout node 12 is visible.</summary>
    public const uint YesButtonNodeId = 8;

    public const uint NoButtonNodeId = 11;

    /// <summary>Lottery "buy another ticket?" (Yes=11, No=12 HoldButton).</summary>
    public const uint TicketPurchaseYesButtonNodeId = 11;

    public const uint TicketPurchaseNoButtonNodeId = 12;

    /// <summary>LotteryWeekly nested SelectString follow-up layout.</summary>
    public const uint AlternateYesButtonNodeId = 13;

    public const uint AlternateNoButtonNodeId = 10;

    private static readonly uint[] YesButtonNodeCandidates =
    [
        TicketPurchaseYesButtonNodeId,
        AlternateYesButtonNodeId,
        YesButtonNodeId,
    ];

    private static readonly (uint Yes, uint No)[] YesNoButtonNodeLayouts =
    [
        (TicketPurchaseYesButtonNodeId, TicketPurchaseNoButtonNodeId),
        (AlternateYesButtonNodeId, AlternateNoButtonNodeId),
        (YesButtonNodeId, NoButtonNodeId),
    ];

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
        (IsLotteryAgentAddon(&yesno->AtkUnitBase, lotteryAgent) ||
         IsArcadeYesno(yesno) ||
         AgentHelper.IsActive(lotteryAgent));

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
        yesno != null && (TryResolveYesNoButtons(yesno, out _, out _) || TryResolveYesButton(yesno, out _));

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
        var wantsYes = callbackId == 0;
        if (wantsYes && TryResolveYesButton(yesno, out var yesButton) &&
            TryClickStructButton(yesno, yesButton, forceEnable: true))
        {
            return true;
        }

        if (TryResolveYesNoButtons(yesno, out yesButton, out var noButton))
        {
            var targetButton = wantsYes ? yesButton : noButton;
            if (targetButton != null &&
                TryClickStructButton(yesno, targetButton, forceEnable: wantsYes))
            {
                return true;
            }
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

        if (wantsYes)
        {
            foreach (var nodeId in YesButtonNodeCandidates)
            {
                if (TryGetVisibleButtonByNodeId(yesno, nodeId, out var button) &&
                    TryClickButton(yesno, button, forceEnable: true))
                {
                    return true;
                }
            }
        }

        try
        {
            Callback.Fire(&yesno->AtkUnitBase, true, callbackId);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectYesno] Callback.Fire({callbackId}) failed");
        }

        try
        {
            yesno->FireCallbackInt(callbackId);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, $"[SelectYesno] FireCallbackInt({callbackId}) failed");
        }

        return false;
    }

    private static bool TryResolveYesButton(AddonSelectYesno* yesno, out AtkComponentButton* yesButton)
    {
        yesButton = null;
        if (yesno == null)
        {
            return false;
        }

        if (HasVisibleStructButton(yesno->YesButton, out yesButton))
        {
            return true;
        }

        foreach (var nodeId in YesButtonNodeCandidates)
        {
            if (nodeId == YesButtonNodeId && IsComponentNodeVisible(yesno, TicketPurchaseNoButtonNodeId))
            {
                continue;
            }

            if (TryGetVisibleButtonByNodeId(yesno, nodeId, out yesButton))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveYesNoButtons(
        AddonSelectYesno* yesno,
        out AtkComponentButton* yesButton,
        out AtkComponentButton* noButton)
    {
        yesButton = null;
        noButton = null;
        if (yesno == null)
        {
            return false;
        }

        if (HasVisibleStructButton(yesno->YesButton, out yesButton) &&
            HasVisibleStructButton(yesno->NoButton, out noButton))
        {
            return true;
        }

        if (TryGetVisibleButtonByNodeId(yesno, TicketPurchaseYesButtonNodeId, out yesButton) &&
            IsComponentNodeVisible(yesno, TicketPurchaseNoButtonNodeId))
        {
            TryGetVisibleButtonByNodeId(yesno, TicketPurchaseNoButtonNodeId, out noButton);
            return true;
        }

        foreach (var (yesNodeId, noNodeId) in YesNoButtonNodeLayouts)
        {
            if (yesNodeId == YesButtonNodeId &&
                noNodeId == NoButtonNodeId &&
                IsComponentNodeVisible(yesno, TicketPurchaseNoButtonNodeId))
            {
                continue;
            }

            if (!TryGetVisibleButtonByNodeId(yesno, yesNodeId, out yesButton))
            {
                continue;
            }

            if (noNodeId == TicketPurchaseNoButtonNodeId)
            {
                if (IsComponentNodeVisible(yesno, noNodeId))
                {
                    TryGetVisibleButtonByNodeId(yesno, noNodeId, out noButton);
                    return true;
                }

                continue;
            }

            if (TryGetVisibleButtonByNodeId(yesno, noNodeId, out noButton))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsComponentNodeVisible(AddonSelectYesno* yesno, uint nodeId)
    {
        var componentNode = yesno->AtkUnitBase.GetComponentNodeById(nodeId);
        if (componentNode == null)
        {
            return false;
        }

        return ((AtkResNode*)componentNode)->IsVisible();
    }

    private static bool HasVisibleStructButton(AtkComponentButton* button, out AtkComponentButton* resolved)
    {
        resolved = button;
        if (button == null || button->AtkResNode == null)
        {
            resolved = null;
            return false;
        }

        return button->AtkResNode->IsVisible();
    }

    private static bool TryClickStructButton(AddonSelectYesno* yesno, AtkComponentButton* button, bool forceEnable = false) =>
        HasVisibleStructButton(button, out var resolved) &&
        TryClickButton(yesno, resolved, forceEnable);

    private static bool TryClickButton(AddonSelectYesno* yesno, AtkComponentButton* button, bool forceEnable = false)
    {
        if (button == null)
        {
            return false;
        }

        if (forceEnable && !button->IsEnabled)
        {
            TryForceEnableButton(button);
        }

        return AddonButton.TryClick(&yesno->AtkUnitBase, button, requireEnabled: !forceEnable);
    }

    private static void TryForceEnableButton(AtkComponentButton* button)
    {
        try
        {
            var flagsPtr = (ushort*)&button->AtkComponentBase.OwnerNode->AtkResNode.NodeFlags;
            *flagsPtr ^= 1 << 5;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[SelectYesno] Force-enable button failed");
        }
    }
}
