using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadPrepRequestReader
{
    private const int MinRootChildCount = 9;
    private const int RulesGroupAIndex = 6;
    private const int RulesGroupBIndex = 7;
    private const int NpcNameIndex = 8;

    public const int RegionalRuleSlot0 = 0;
    public const int RegionalRuleSlot1 = 1;
    public const int MatchRuleSlot0 = 2;
    public const int MatchRuleSlot1 = 3;

    public static void Read(AddonRequest* addon, UIStateTriadPrep state)
    {
        state.decks.Clear();
        state.npc = string.Empty;

        var baseNode = &addon->AtkUnitBase;
        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        if (nodeArrL0 != null && nodeArrL0.Length >= MinRootChildCount)
        {
            ReadLayout(nodeArrL0, state);
        }

        if (string.IsNullOrWhiteSpace(state.npc))
        {
            state.npc = TryFindNpcName(baseNode) ?? string.Empty;
        }
    }

    private static void ReadLayout(AtkResNode*[] nodeArrL0, UIStateTriadPrep state)
    {
        var nodeRulesA = GUINodeUtils.PickNode(nodeArrL0, RulesGroupAIndex, nodeArrL0.Length);
        var nodeArrL1A = GUINodeUtils.GetImmediateChildNodes(nodeRulesA);
        if (nodeArrL1A != null)
        {
            var nodeL2A1 = GUINodeUtils.PickNode(nodeArrL1A, 0, nodeArrL1A.Length);
            state.rules[3] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A1, 2, 3)) ?? "";
            var nodeL2A2 = GUINodeUtils.PickNode(nodeArrL1A, 1, nodeArrL1A.Length);
            state.rules[2] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A2, 2, 3)) ?? "";
        }

        var nodeRulesB = GUINodeUtils.PickNode(nodeArrL0, RulesGroupBIndex, nodeArrL0.Length);
        var nodeArrL1B = GUINodeUtils.GetImmediateChildNodes(nodeRulesB);
        if (nodeArrL1B != null)
        {
            var nodeL2B1 = GUINodeUtils.PickNode(nodeArrL1B, 0, nodeArrL1B.Length);
            state.rules[1] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B1, 2, 3)) ?? "";
            var nodeL2B2 = GUINodeUtils.PickNode(nodeArrL1B, 1, nodeArrL1B.Length);
            state.rules[0] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B2, 2, 3)) ?? "";
        }

        var nodeNpc = GUINodeUtils.PickNode(nodeArrL0, NpcNameIndex, nodeArrL0.Length);
        state.npc = GUINodeUtils.GetNodeText(GUINodeUtils.GetChildNode(nodeNpc)) ?? "";
    }

    private static string? TryFindNpcName(AtkUnitBase* baseNode)
    {
        var parseCtx = new GameUIParser();

        for (var i = 0; i < baseNode->UldManager.NodeListCount; i++)
        {
            if (TryParseNpcLabel(parseCtx, GUINodeUtils.GetNodeText(baseNode->UldManager.NodeList[i]), out var npcName))
            {
                return npcName;
            }
        }

        foreach (var node in GUINodeUtils.GetAllChildNodes(baseNode->RootNode) ?? [])
        {
            if (node == null)
            {
                continue;
            }

            if (TryParseNpcLabel(parseCtx, GUINodeUtils.GetNodeText(node), out var npcName))
            {
                return npcName;
            }
        }

        return null;
    }

    private static bool TryParseNpcLabel(GameUIParser parseCtx, string? text, out string? npcName)
    {
        npcName = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.Length < 3)
        {
            return false;
        }

        if (int.TryParse(text, out var _) || parseCtx.ParseModifier(text, false) != null)
        {
            return false;
        }

        if (parseCtx.ParseNpc(text, false) != null ||
            parseCtx.ParseNpcNameStart(text, false) != null)
        {
            npcName = text;
            return true;
        }

        return false;
    }
}
