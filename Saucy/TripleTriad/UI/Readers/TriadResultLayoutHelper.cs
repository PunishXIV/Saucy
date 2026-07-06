using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.TripleTriad.Utils;

namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadResultLayoutHelper
{
    private const int CompactRootChildCount = 8;
    private const int ExpandedRootChildCount = 10;
    private const int CompactRewardsIndex = 7;
    private const int ExpandedRewardsIndex = 8;

    public static AtkResNode* TryGetRewardsPanel(AtkResNode*[] nodeArrL0)
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

        if (nodeArrL0.Length > CompactRewardsIndex)
        {
            return nodeArrL0[CompactRewardsIndex];
        }

        return null;
    }
}
