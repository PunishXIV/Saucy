using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Linq;
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

        if (state.numMGP < 0 &&
            GoldSaucerRewardMgpParser.TryParseFromAddon(&addon->AtkUnitBase, out var fallbackMgp))
        {
            state.numMGP = fallbackMgp;
        }

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
            if (string.IsNullOrEmpty(descCoins))
            {
                continue;
            }

            // Parse into a local: TryParse zeroes its out arg on failure, which would
            // clobber the -1 "not read yet" sentinel and bypass the MGP readiness gate
            // (and the fallback parser) whenever a digitless reward row is visited first.
            var digits = new string([.. descCoins.Where(char.IsDigit)]);
            if (digits.Length > 0 && int.TryParse(digits, out var mgpValue))
            {
                state.numMGP = mgpValue;
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
