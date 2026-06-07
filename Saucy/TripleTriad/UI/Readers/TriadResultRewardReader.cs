using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadResultRewardReader
{
    public static uint TryReadRewardItemId(AddonTripleTriadResult* resultAddon)
    {
        var fromAgent = TryReadRewardItemIdFromAgent();
        return fromAgent > 0 ? fromAgent : TryReadRewardItemIdFromUi(resultAddon);
    }

    public static uint TryReadRewardItemId(AtkUnitBase* resultAddon) =>
        TryReadRewardItemId((AddonTripleTriadResult*)resultAddon);

    private static uint TryReadRewardItemIdFromAgent()
    {
        var agent = AgentTripleTriad.TryGet();
        var itemId = agent != null ? agent->RewardItemId : 0;
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

    private static uint TryReadRewardItemIdFromUi(AddonTripleTriadResult* resultAddon)
    {
        var baseNode = &resultAddon->AtkUnitBase;
        if (baseNode->RootNode == null)
        {
            return 0;
        }

        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        if (nodeArrL0 == null)
        {
            return 0;
        }

        var rewardsNode = nodeArrL0.Length == 8
            ? GUINodeUtils.PickNode(nodeArrL0, 7, 8)
            : GUINodeUtils.PickNode(nodeArrL0, 8, 10);
        if (rewardsNode == null)
        {
            return 0;
        }

        foreach (var node in GUINodeUtils.GetAllChildNodes(rewardsNode) ?? [])
        {
            var texPath = GUINodeUtils.GetNodeTexturePath(node);
            if (string.IsNullOrEmpty(texPath))
            {
                continue;
            }

            var card = TriadCardDB.Get().FindByTexture(texPath);
            if (card == null)
            {
                continue;
            }

            var cardInfo = GameCardDB.Get().FindById(card.Id);
            if (cardInfo != null && cardInfo.ItemId > 0)
            {
                return cardInfo.ItemId;
            }
        }

        return 0;
    }
}
