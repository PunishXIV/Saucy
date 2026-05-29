using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Runtime.InteropServices;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadResults : IUIReader
{
    private UIStateTriadResults cachedState = new();

    private bool needsNotify;
    public Action<UIStateTriadResults>? OnUpdated;

    public string GetAddonName() => "TripleTriadResult";

    public void OnAddonLost()
    {
        needsNotify = false;
        TriadAutomater.ResetResultMatchRecording();
    }

    public void OnAddonShown(nint addonPtr)
    {
        needsNotify = true;
        cachedState = new();
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        if (needsNotify)
        {
            nint agentPtr = Svc.GameGui.FindAgentInterface(addonPtr);
            if (agentPtr != nint.Zero)
            {
                var agent = (AgentTripleTriad*)agentPtr;
                cachedState.cardItemId = agent->rewardItemId;
            }

            UpdateCachedState(baseNode);

            if (IsResultReadyToNotify(baseNode))
            {
                if (cachedState.numMGP < 0)
                {
                    cachedState.numMGP = 0;
                }

                needsNotify = false;
                OnUpdated?.Invoke(cachedState);
            }
        }
    }

    private unsafe bool IsResultReadyToNotify(AtkUnitBase* baseNode) =>
        cachedState.isDraw || cachedState.isLose || cachedState.isWin ||
        TriadAutomater.HasVisibleResultActionButtons(baseNode);

    private unsafe void UpdateCachedState(AtkUnitBase* baseNode)
    {
        // 10 nodes (sibling scan)
        //    [8] res node, rewards, 8 nodes (sibling scan)
        //        [6] comp node, 6 node list
        //            [5] textninegrid comp, 2 node list
        //                [1] text node, MGP reward
        //
        //    [9] res node, result
        //        [0] = draw, vis?
        //        [1] = lose, vis?
        //        [2] = win, vis ?

        cachedState.isDraw = false;
        cachedState.isLose = false;
        cachedState.isWin = false;

        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);

        if (nodeArrL0 != null)
        {
            if (nodeArrL0.Length == 8)
            {
                TryReadMgpReward(GUINodeUtils.PickNode(nodeArrL0, 7, 8));
            }
            else
            {
                TryReadMgpReward(GUINodeUtils.PickNode(nodeArrL0, 8, 10));
            }
        }

        if (!TryReadResultFlags(nodeArrL0, 9, 10) && nodeArrL0 != null)
        {
            foreach (var node in nodeArrL0)
            {
                if (TryReadResultFlags(GUINodeUtils.GetImmediateChildNodes(node), expectedLength: 3))
                {
                    break;
                }
            }
        }
    }

    private unsafe void TryReadMgpReward(AtkResNode* nodeRewards)
    {
        var nodeArrRewards0 = GUINodeUtils.GetImmediateChildNodes(nodeRewards);
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
            if (!string.IsNullOrEmpty(descCoins) && int.TryParse(descCoins, out cachedState.numMGP))
            {
                return;
            }
        }

        cachedState.numMGP = -1;
    }

    private unsafe bool TryReadResultFlags(AtkResNode*[]? nodes, int nodeIdx, int expectedNumNodes)
    {
        if (nodes == null)
        {
            return false;
        }

        return TryReadResultFlags(GUINodeUtils.PickNode(nodes, nodeIdx, expectedNumNodes));
    }

    private unsafe bool TryReadResultFlags(AtkResNode* nodeResult) =>
        nodeResult != null && TryReadResultFlags(GUINodeUtils.GetImmediateChildNodes(nodeResult));

    private unsafe bool TryReadResultFlags(AtkResNode*[]? nodeArrResult0, int expectedLength = 3)
    {
        if (nodeArrResult0 == null || nodeArrResult0.Length != expectedLength)
        {
            return false;
        }

        cachedState.isDraw = nodeArrResult0[0] != null && nodeArrResult0[0]->IsVisible();
        cachedState.isLose = nodeArrResult0[1] != null && nodeArrResult0[1]->IsVisible();
        cachedState.isWin = nodeArrResult0[2] != null && nodeArrResult0[2]->IsVisible();
        return cachedState.isDraw || cachedState.isLose || cachedState.isWin;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x1d0)]
    private struct AgentTripleTriad
    {
        [FieldOffset(0x1c8)] public uint rewardItemId;
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
