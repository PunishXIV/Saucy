using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.TripleTriad.Utils;
using System;
using System.Linq;

namespace Saucy.Framework.UI;

internal static unsafe class GoldSaucerRewardMgpParser
{
    public static bool TryParseFromAddon(AtkUnitBase* baseNode, out int mgp)
    {
        mgp = -1;
        if (baseNode is null)
        {
            return false;
        }

        TryParseFromUldManager(baseNode, ref mgp);

        if (mgp < 0 && baseNode->RootNode is not null)
        {
            ScanVisibleTree(baseNode->RootNode, ref mgp);
        }

        return mgp >= 0;
    }

    public static bool TryParseFromVisibleTree(AtkResNode* root, out int mgp)
    {
        mgp = -1;
        if (root is null)
        {
            return false;
        }

        ScanVisibleTree(root, ref mgp);
        return mgp >= 0;
    }

    private static void TryParseFromUldManager(AtkUnitBase* baseNode, ref int mgp)
    {
        ref var uld = ref baseNode->UldManager;
        for (var i = 0; i < uld.NodeListCount; i++)
        {
            var node = uld.NodeList[i];
            if (node is null)
            {
                continue;
            }

            TryParseFromNode(node, ref mgp);
            var component = node->GetComponent();
            if (component is null)
            {
                continue;
            }

            ref var innerUld = ref component->UldManager;
            for (var j = 0; j < innerUld.NodeListCount; j++)
            {
                var innerNode = innerUld.NodeList[j];
                if (innerNode is null)
                {
                    continue;
                }

                TryParseFromNode(innerNode, ref mgp);
                var innerComponent = innerNode->GetComponent();
                if (innerComponent is null)
                {
                    continue;
                }

                ref var deepestUld = ref innerComponent->UldManager;
                for (var k = 0; k < deepestUld.NodeListCount; k++)
                {
                    var deepestNode = deepestUld.NodeList[k];
                    if (deepestNode is not null)
                    {
                        TryParseFromNode(deepestNode, ref mgp);
                    }
                }
            }
        }
    }

    private static void ScanVisibleTree(AtkResNode* root, ref int mgp)
    {
        foreach (var node in GUINodeUtils.GetAllChildNodes(root) ?? [])
        {
            if (!GUINodeUtils.IsNodeVisible(node))
            {
                continue;
            }

            TryParseFromNode(node, ref mgp);
        }
    }

    private static void TryParseFromNode(AtkResNode* node, ref int bestMgp)
    {
        var text = GUINodeUtils.GetNodeText(node);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Contains("mgp", StringComparison.OrdinalIgnoreCase) &&
            TryParseMgpDigits(text, out var labeled) &&
            labeled > 0)
        {
            bestMgp = labeled;
            return;
        }

        if (!TryParseMgpDigits(text, out var parsed) || parsed <= 0)
        {
            return;
        }

        if (parsed > bestMgp)
        {
            bestMgp = parsed;
        }
    }

    internal static bool TryParseMgpDigits(string? text, out int mgp)
    {
        mgp = -1;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var digits = new string([.. text.Where(char.IsDigit)]);
        return digits.Length > 0 && int.TryParse(digits, out mgp) && mgp > 0;
    }
}
