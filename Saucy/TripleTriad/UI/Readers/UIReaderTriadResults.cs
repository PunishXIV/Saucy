using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadResults : IUIReader
{
    private const int CompactRootChildCount = 8;
    private const int CompactRewardsIndex = 7;
    private const int ExpandedRootChildCount = 10;
    private const int ExpandedRewardsIndex = 8;
    private const int ResultFlagsIndex = 9;
    private const int ResultNotifyFallbackFrames = 90;

    private UIStateTriadResults cachedState = new();
    private int framesSinceShown;

    public Action<UIStateTriadResults>? OnUpdated;

    public bool HasPendingNotify { get; private set; }

    public string GetAddonName() => "TripleTriadResult";

    public void OnAddonLost()
    {
        if (HasPendingNotify && (cachedState.isDraw || cachedState.isLose || cachedState.isWin))
        {
            PublishResult();
        }

        HasPendingNotify = false;
        framesSinceShown = 0;
        TriadMgpTracker.Reset();
        TriadRematchAutomation.ResetResultMatchRecording();
    }

    public void OnAddonShown(nint addonPtr)
    {
        HasPendingNotify = true;
        framesSinceShown = 0;
        cachedState = new();
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var addon = (AddonTripleTriadResult*)addonPtr;
        if (addon is null || !HasPendingNotify)
        {
            return;
        }

        framesSinceShown++;
        RefreshCachedState(addon);

        var ready = IsResultReadyToNotify(addon);
        if (!ready && framesSinceShown >= ResultNotifyFallbackFrames)
        {
            ready = true;
        }

        if (ready)
        {
            PublishResult();
        }
    }

    public unsafe void ForceNotifyFromFallback(nint addonPtr = default)
    {
        AddonTripleTriadResult* addon;
        if (addonPtr != nint.Zero)
        {
            addon = (AddonTripleTriadResult*)addonPtr;
        }
        else if (!TriadLocalClientStructs.TryGetResult(out addon))
        {
            return;
        }

        RefreshCachedState(addon);
        PublishResult();
    }

    private unsafe void RefreshCachedState(AddonTripleTriadResult* addon)
    {
        cachedState.cardItemId = TryReadRewardItemId(addon);
        ReadResult(addon, cachedState);
    }

    private void PublishResult()
    {
        if (cachedState.numMGP < 0 &&
            cachedState.isWin &&
            TriadMgpTracker.TryConsumeWinReward(out var inventoryMgp))
        {
            cachedState.numMGP = inventoryMgp;
        }

        if (cachedState.numMGP < 0)
        {
            cachedState.numMGP = 0;
        }

        HasPendingNotify = false;
        framesSinceShown = 0;
        OnUpdated?.Invoke(cachedState);
    }

    private unsafe bool IsResultReadyToNotify(AddonTripleTriadResult* addon)
    {
        if (!cachedState.isDraw && !cachedState.isLose && !cachedState.isWin)
        {
            return false;
        }

        if (cachedState.numMGP < 0)
        {
            return false;
        }

        return TriadRematchAutomation.IsResultReady(&addon->AtkUnitBase);
    }

    private static unsafe void ReadResult(AddonTripleTriadResult* addon, UIStateTriadResults state)
    {
        state.isDraw = false;
        state.isLose = false;
        state.isWin = false;
        state.numMGP = -1;

        var baseNode = &addon->AtkUnitBase;
        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        if (nodeArrL0 is null)
        {
            return;
        }

        TryReadMgpReward(nodeArrL0, baseNode, state);

        if (!TryReadResultFlags(nodeArrL0, ResultFlagsIndex, ExpandedRootChildCount, state))
        {
            foreach (var node in nodeArrL0)
            {
                if (TryReadResultFlags(GUINodeUtils.GetImmediateChildNodes(node), state))
                {
                    break;
                }
            }
        }
    }

    private static unsafe void TryReadMgpReward(AtkResNode*[] nodeArrL0, AtkUnitBase* baseNode, UIStateTriadResults state)
    {
        var rewardsNode = TryGetRewardsPanel(nodeArrL0);
        if (rewardsNode is not null)
        {
            TryReadMgpRewardStructured(rewardsNode, state);
            if (state.numMGP < 0 &&
                GoldSaucerRewardMgpParser.TryParseFromVisibleTree(rewardsNode, out var rewardsMgp))
            {
                state.numMGP = rewardsMgp;
            }
        }

        if (state.numMGP < 0 &&
            GoldSaucerRewardMgpParser.TryParseFromAddon(baseNode, out var fallbackMgp))
        {
            state.numMGP = fallbackMgp;
        }
    }

    private static unsafe void TryReadMgpRewardStructured(AtkResNode* rewardsNode, UIStateTriadResults state)
    {
        var nodeArrRewards0 = GUINodeUtils.GetImmediateChildNodes(rewardsNode);
        if (nodeArrRewards0 is null)
        {
            return;
        }

        foreach (var nodeCoinsA in nodeArrRewards0)
        {
            var nodeCoinsB = GUINodeUtils.PickChildNode(nodeCoinsA, 5, 6);
            if (nodeCoinsB is null)
            {
                continue;
            }

            var nodeCoinsC = GUINodeUtils.PickChildNode(nodeCoinsB, 1, 2);
            if (GoldSaucerRewardMgpParser.TryParseMgpDigits(GUINodeUtils.GetNodeText(nodeCoinsC), out var mgpValue))
            {
                state.numMGP = mgpValue;
                return;
            }
        }
    }

    private static unsafe bool TryReadResultFlags(AtkResNode*[]? nodes, int nodeIdx, int expectedNumNodes, UIStateTriadResults state)
    {
        if (nodes is null)
        {
            return false;
        }

        return TryReadResultFlags(GUINodeUtils.PickNode(nodes, nodeIdx, expectedNumNodes), state);
    }

    private static unsafe bool TryReadResultFlags(AtkResNode* nodeResult, UIStateTriadResults state) =>
        nodeResult is not null && TryReadResultFlags(GUINodeUtils.GetImmediateChildNodes(nodeResult), state);

    private static unsafe bool TryReadResultFlags(AtkResNode*[]? nodeArrResult0, UIStateTriadResults state, int expectedLength = 3)
    {
        if (nodeArrResult0 is not { Length: var length } || length != expectedLength)
        {
            return false;
        }

        state.isDraw = GUINodeUtils.IsNodeVisible(nodeArrResult0[0]);
        state.isLose = GUINodeUtils.IsNodeVisible(nodeArrResult0[1]);
        state.isWin = GUINodeUtils.IsNodeVisible(nodeArrResult0[2]);
        return state.isDraw || state.isLose || state.isWin;
    }

    private static unsafe AtkResNode* TryGetRewardsPanel(AtkResNode*[] nodeArrL0)
    {
        if (nodeArrL0.Length == CompactRootChildCount)
        {
            return GUINodeUtils.PickNode(nodeArrL0, CompactRewardsIndex, CompactRootChildCount);
        }

        if (nodeArrL0.Length >= ExpandedRootChildCount)
        {
            return GUINodeUtils.PickNode(nodeArrL0, ExpandedRewardsIndex, ExpandedRootChildCount);
        }

        if (nodeArrL0.Length > ExpandedRewardsIndex)
        {
            return nodeArrL0[ExpandedRewardsIndex];
        }

        return nodeArrL0.Length > CompactRewardsIndex ? nodeArrL0[CompactRewardsIndex] : null;
    }

    private static unsafe uint TryReadRewardItemId(AddonTripleTriadResult* resultAddon)
    {
        var fromAgent = TryReadRewardItemIdFromAgent();
        return fromAgent > 0 ? fromAgent : TryReadRewardItemIdFromUi(resultAddon);
    }

    private static unsafe uint TryReadRewardItemIdFromAgent()
    {
        var agent = AgentTripleTriad.TryGet();
        var itemId = agent is not null ? agent->RewardItemId : 0;
        if (itemId > 0)
        {
            return itemId;
        }

        if (!TriadLocalClientStructs.TryGetResult(out var resultAddon, false))
        {
            return 0;
        }

        var ifacePtr = Svc.GameGui.FindAgentInterface((nint)resultAddon);
        if (ifacePtr.Address == nint.Zero)
        {
            return 0;
        }

        return ((AgentTripleTriad*)ifacePtr.Address)->RewardItemId;
    }

    private static unsafe uint TryReadRewardItemIdFromUi(AddonTripleTriadResult* resultAddon)
    {
        var baseNode = &resultAddon->AtkUnitBase;
        if (baseNode->RootNode is null)
        {
            return 0;
        }

        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        if (nodeArrL0 is null)
        {
            return 0;
        }

        var rewardsNode = TryGetRewardsPanel(nodeArrL0);
        var scanRoot = rewardsNode is not null ? rewardsNode : baseNode->RootNode;

        foreach (var node in GUINodeUtils.GetAllChildNodes(scanRoot) ?? [])
        {
            var texPath = GUINodeUtils.GetNodeTexturePath(node);
            if (string.IsNullOrEmpty(texPath))
            {
                continue;
            }

            var card = TriadCardDB.Get().FindByTexture(texPath);
            if (card is null)
            {
                continue;
            }

            var cardInfo = GameCardDB.Get().FindById(card.Id);
            if (cardInfo is not null && cardInfo.ItemId > 0)
            {
                return cardInfo.ItemId;
            }
        }

        return 0;
    }
}

public class UIStateTriadResults
{
    public uint cardItemId;
    public bool isDraw;
    public bool isLose;
    public bool isWin;
    public int numMGP;
}
