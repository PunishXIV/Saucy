using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Linq;
namespace Saucy.Framework.UI;

internal static unsafe class GoldSaucerRewardMgpParser
{
    public static bool TryParseFromAddon(AtkUnitBase* baseNode, out int mgp)
    {
        mgp = -1;
        if (baseNode == null)
        {
            return false;
        }

        ref var uld = ref baseNode->UldManager;
        for (var i = 0; i < uld.NodeListCount; i++)
        {
            var node = uld.NodeList[i];
            if (node == null)
            {
                continue;
            }

            TryParseFromNode(node, ref mgp);
            var component = node->GetComponent();
            if (component == null)
            {
                continue;
            }

            ref var innerUld = ref component->UldManager;
            for (var j = 0; j < innerUld.NodeListCount; j++)
            {
                var innerNode = innerUld.NodeList[j];
                if (innerNode == null)
                {
                    continue;
                }

                TryParseFromNode(innerNode, ref mgp);
                var innerComponent = innerNode->GetComponent();
                if (innerComponent == null)
                {
                    continue;
                }

                ref var deepestUld = ref innerComponent->UldManager;
                for (var k = 0; k < deepestUld.NodeListCount; k++)
                {
                    var deepestNode = deepestUld.NodeList[k];
                    if (deepestNode != null)
                    {
                        TryParseFromNode(deepestNode, ref mgp);
                    }
                }
            }
        }

        return mgp >= 0;
    }

    private static void TryParseFromNode(AtkResNode* node, ref int bestMgp)
    {
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
