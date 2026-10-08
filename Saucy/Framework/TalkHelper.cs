using Dalamud.Game.ClientState.Conditions;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using static ECommons.GenericHelpers;
namespace Saucy.Framework;

public static unsafe class TalkHelper
{
    public static bool IsVisible()
    {
        if (!TryGetAddonByName<AtkUnitBase>("Talk", out var talk))
        {
            return false;
        }

        return talk->IsVisible && IsAddonReady(talk);
    }

    public static bool TryAdvance(string throttleKey = "Saucy.Talk.Advance", int throttleMs = 400)
    {
        if (!TryGetAddonByName<AtkUnitBase>("Talk", out var talk) || !IsAddonReady(talk) || !talk->IsVisible)
        {
            return false;
        }

        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        try
        {
            new AddonMaster.Talk(talk).Click();
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TalkHelper] Talk click failed; trying callback");
        }

        try
        {
            talk->FireCallbackInt(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[TalkHelper] Talk callback failed");
            return false;
        }
    }
}

public static class NpcDialogueGate
{
    public static bool ShouldBlockQuestDialogue(bool isAtTrackedNpc) =>
        Svc.Condition[ConditionFlag.OccupiedInQuestEvent] && !isAtTrackedNpc;

    public static bool CanAutomateYesno(string scope, bool inTimedFlow) =>
        ObjectHelper.HasInitiatedDialogue(scope) ||
        (inTimedFlow && ObjectHelper.IsTargeting(scope));

    public static void RefreshTimedFlow(
        string scope,
        bool inTimedFlow,
        Action markFlow,
        Func<bool> hasModuleUi)
    {
        if (!ObjectHelper.IsTargeting(scope))
        {
            return;
        }

        if (!inTimedFlow && !ObjectHelper.HasInitiatedDialogue(scope))
        {
            return;
        }

        if (hasModuleUi())
        {
            markFlow();
        }
    }
}
