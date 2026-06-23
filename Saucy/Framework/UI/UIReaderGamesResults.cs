using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.AirForce;
using System;
using System.Linq;
namespace Saucy.Framework.UI;

public class UIReaderGamesResults : IUIReader
{
    private UIStateAirForceResults airForceResults = new();
    private UIStateCuffResults cuffResults = new();
    private UIStateLimbResults limbResults = new();

    private bool needsNotify;
    public Action<UIStateAirForceResults>? OnAirForceUpdated;
    public Action<UIStateCuffResults>? OnCuffUpdated;
    public Action<UIStateLimbResults>? OnLimbUpdated;

    public bool HasResultsUI { get; private set; }

    public string GetAddonName() => "GoldSaucerReward";

    public void OnAddonLost()
    {
        foreach (var machine in GoldSaucerArcadeMachineHelper.All)
        {
            ArcadeMachineSession.OnRewardScreenClosed(machine);
        }

        SetIsResultsUI(false);
    }

    public void OnAddonShown(nint addonPtr)
    {
        needsNotify = true;
        if (GoldSaucerArcadeMachineHelper.AnyEnabled() || AirForceAutomation.ShouldTrackReward)
        {
            SetIsResultsUI(true);
        }

        foreach (var machine in GoldSaucerArcadeMachineHelper.All)
        {
            if (GoldSaucerArcadeMachineHelper.IsEnabled(machine))
            {
                ArcadeMachineSession.OnRewardScreenOpened(machine);
            }
        }

        cuffResults = new();
        limbResults = new();
        airForceResults = new();
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null || !needsNotify)
        {
            return;
        }

        if (!GoldSaucerArcadeMachineHelper.AnyEnabled() && !AirForceAutomation.ShouldTrackReward)
        {
            needsNotify = false;
            return;
        }

        UpdateCachedState(baseNode);

        var notified = false;

        if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Cuff))
        {
            if (cuffResults.numMGP >= 0)
            {
                notified = true;
                OnCuffUpdated?.Invoke(cuffResults);
            }
            else if (TryParseRewardMgpFallback(baseNode, out var cuffFallbackMgp))
            {
                cuffResults.numMGP = cuffFallbackMgp;
                notified = true;
                OnCuffUpdated?.Invoke(cuffResults);
            }
        }

        if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            if (limbResults.numMGP >= 0)
            {
                notified = true;
                OnLimbUpdated?.Invoke(limbResults);
            }
            else if (TryParseRewardMgpFallback(baseNode, out var fallbackMgp))
            {
                limbResults.numMGP = fallbackMgp;
                notified = true;
                OnLimbUpdated?.Invoke(limbResults);
            }
        }

        if (AirForceAutomation.ShouldTrackReward)
        {
            if (airForceResults.numMGP >= 0)
            {
                notified = true;
                OnAirForceUpdated?.Invoke(airForceResults);
            }
        }

        if (notified)
        {
            needsNotify = false;
        }
    }

    public void SetIsResultsUI(bool value)
    {
        HasResultsUI = value;
        if (value)
        {
            return;
        }

        foreach (var machine in GoldSaucerArcadeMachineHelper.All)
        {
            ArcadeMachineSession.OnRewardScreenClosed(machine);
            if (GoldSaucerArcadeMachineHelper.IsEnabled(machine))
            {
                ArcadeMachineSession.ClearInteractPending(machine);
            }
        }
    }

    private unsafe void UpdateCachedState(AtkUnitBase* baseNode)
    {
        if (!GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Cuff))
        {
            cuffResults.numMGP = -1;
        }

        if (!GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            limbResults.numMGP = -1;
        }

        if (!AirForceAutomation.ShouldTrackReward)
        {
            airForceResults.numMGP = -1;
        }

        if (!TryGetRewardMgpTextNode(baseNode, out var number))
        {
            if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Cuff))
            {
                cuffResults.numMGP = -1;
            }

            if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
            {
                limbResults.numMGP = -1;
            }

            if (AirForceAutomation.ShouldTrackReward)
            {
                airForceResults.numMGP = -1;
            }

            return;
        }

        if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Cuff))
        {
            if (!int.TryParse(number->NodeText.ToString(), out cuffResults.numMGP))
            {
                cuffResults.numMGP = -1;
            }

            switch (cuffResults.numMGP)
            {
                case 10:
                    cuffResults.isBruising = true; break;
                case 15:
                    cuffResults.isPunishing = true; break;
                case 25:
                    cuffResults.isBrutal = true; break;
            }
        }

        if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            if (!int.TryParse(number->NodeText.ToString().Where(char.IsDigit).ToArray(), out limbResults.numMGP))
            {
                limbResults.numMGP = -1;
            }
        }

        if (AirForceAutomation.ShouldTrackReward)
        {
            if (!int.TryParse(number->NodeText.ToString().Where(char.IsDigit).ToArray(), out airForceResults.numMGP))
            {
                airForceResults.numMGP = -1;
            }
        }
    }

    private static unsafe bool TryGetRewardMgpTextNode(AtkUnitBase* baseNode, out AtkTextNode* textNode)
    {
        textNode = null;
        if (baseNode == null)
        {
            return false;
        }

        ref var uld = ref baseNode->UldManager;
        if (uld.NodeListCount <= 4)
        {
            return false;
        }

        var node4 = uld.NodeList[4];
        if (node4 == null)
        {
            return false;
        }

        var component = node4->GetComponent();
        if (component == null)
        {
            return false;
        }

        ref var innerUld = ref component->UldManager;
        if (innerUld.NodeListCount <= 2)
        {
            return false;
        }

        var node2 = innerUld.NodeList[2];
        if (node2 == null)
        {
            return false;
        }

        var innerComponent = node2->GetComponent();
        if (innerComponent == null)
        {
            return false;
        }

        ref var deepestUld = ref innerComponent->UldManager;
        if (deepestUld.NodeListCount <= 1)
        {
            return false;
        }

        var node1 = deepestUld.NodeList[1];
        if (node1 == null)
        {
            return false;
        }

        textNode = node1->GetAsAtkTextNode();
        return textNode != null;
    }

    private static unsafe bool TryParseRewardMgpFallback(AtkUnitBase* baseNode, out int mgp) =>
        GoldSaucerRewardMgpParser.TryParseFromAddon(baseNode, out mgp);

    private static unsafe void TryParseMgpFromNode(AtkResNode* node, ref int bestMgp)
    {
        // Kept for TryGetRewardMgpTextNode path; fallback scan lives in GoldSaucerRewardMgpParser.
        var textNode = node->GetAsAtkTextNode();
        if (textNode == null)
        {
            return;
        }

        var text = textNode->NodeText.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var digits = new string([.. text.Where(char.IsDigit)]);
        if (digits.Length == 0 || !int.TryParse(digits, out var parsed) || parsed <= 0)
        {
            return;
        }

        if (parsed > bestMgp)
        {
            bestMgp = parsed;
        }
    }
}
