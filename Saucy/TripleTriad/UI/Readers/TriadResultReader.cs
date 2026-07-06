using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework.UI;
using Saucy.TripleTriad.Utils;

namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadResultReader
{
    private const int ExpandedRootChildCount = 10;
    private const int ResultFlagsIndex = 9;

    public static void Read(AddonTripleTriadResult* addon, UIStateTriadResults state)
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

    private static void TryReadMgpReward(AtkResNode*[] nodeArrL0, AtkUnitBase* baseNode, UIStateTriadResults state)
    {
        var rewardsNode = TriadResultLayoutHelper.TryGetRewardsPanel(nodeArrL0);
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

    private static void TryReadMgpRewardStructured(AtkResNode* rewardsNode, UIStateTriadResults state)
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

    private static bool TryReadResultFlags(AtkResNode*[]? nodes, int nodeIdx, int expectedNumNodes, UIStateTriadResults state)
    {
        if (nodes is null)
        {
            return false;
        }

        return TryReadResultFlags(GUINodeUtils.PickNode(nodes, nodeIdx, expectedNumNodes), state);
    }

    private static bool TryReadResultFlags(AtkResNode* nodeResult, UIStateTriadResults state) =>
        nodeResult is not null && TryReadResultFlags(GUINodeUtils.GetImmediateChildNodes(nodeResult), state);

    private static bool TryReadResultFlags(AtkResNode*[]? nodeArrResult0, UIStateTriadResults state, int expectedLength = 3)
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
}
