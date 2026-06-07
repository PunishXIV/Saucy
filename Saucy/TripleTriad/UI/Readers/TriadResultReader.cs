using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadResultReader
{
    private const int CompactRootChildCount = 8;
    private const int ExpandedRootChildCount = 10;
    private const int CompactMgpRewardIndex = 7;
    private const int ExpandedMgpRewardIndex = 8;
    private const int ResultFlagsIndex = 9;

    public static void Read(AddonTripleTriadResult* addon, UIStateTriadResults state)
    {
        state.isDraw = false;
        state.isLose = false;
        state.isWin = false;
        state.numMGP = -1;

        var baseNode = &addon->AtkUnitBase;
        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        if (nodeArrL0 == null)
        {
            return;
        }

        TryReadMgpReward(nodeArrL0, state);

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

    private static void TryReadMgpReward(AtkResNode*[] nodeArrL0, UIStateTriadResults state)
    {
        var rewardsNode = nodeArrL0.Length == CompactRootChildCount
            ? GUINodeUtils.PickNode(nodeArrL0, CompactMgpRewardIndex, CompactRootChildCount)
            : GUINodeUtils.PickNode(nodeArrL0, ExpandedMgpRewardIndex, ExpandedRootChildCount);
        if (rewardsNode == null)
        {
            return;
        }

        var nodeArrRewards0 = GUINodeUtils.GetImmediateChildNodes(rewardsNode);
        if (nodeArrRewards0 == null)
        {
            return;
        }

        foreach (var nodeCoinsA in nodeArrRewards0)
        {
            var nodeCoinsB = GUINodeUtils.PickChildNode(nodeCoinsA, 5, 6);
            if (nodeCoinsB == null)
            {
                continue;
            }

            var nodeCoinsC = GUINodeUtils.PickChildNode(nodeCoinsB, 1, 2);
            var descCoins = GUINodeUtils.GetNodeText(nodeCoinsC);
            if (!string.IsNullOrEmpty(descCoins) && int.TryParse(descCoins, out state.numMGP))
            {
                return;
            }
        }
    }

    private static bool TryReadResultFlags(AtkResNode*[]? nodes, int nodeIdx, int expectedNumNodes, UIStateTriadResults state)
    {
        if (nodes == null)
        {
            return false;
        }

        return TryReadResultFlags(GUINodeUtils.PickNode(nodes, nodeIdx, expectedNumNodes), state);
    }

    private static bool TryReadResultFlags(AtkResNode* nodeResult, UIStateTriadResults state) =>
        nodeResult != null && TryReadResultFlags(GUINodeUtils.GetImmediateChildNodes(nodeResult), state);

    private static bool TryReadResultFlags(AtkResNode*[]? nodeArrResult0, UIStateTriadResults state, int expectedLength = 3)
    {
        if (nodeArrResult0 == null || nodeArrResult0.Length != expectedLength)
        {
            return false;
        }

        state.isDraw = nodeArrResult0[0] != null && nodeArrResult0[0]->IsVisible();
        state.isLose = nodeArrResult0[1] != null && nodeArrResult0[1]->IsVisible();
        state.isWin = nodeArrResult0[2] != null && nodeArrResult0[2]->IsVisible();
        return state.isDraw || state.isLose || state.isWin;
    }
}
