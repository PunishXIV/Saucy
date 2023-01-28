using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using System;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public class UIReaderTriadResults : IUIReader
    {
        [StructLayout(LayoutKind.Explicit, Size = 0x1d0)]
        private unsafe struct AgentTripleTriad
        {
            [FieldOffset(0x1c8)] public uint rewardItemId;
        }

        private readonly GameGui gameGui;
        private UIStateTriadResults cachedState = new();
        public Action<UIStateTriadResults> OnUpdated;

        private bool needsNotify = false;

        public UIReaderTriadResults(GameGui gameGui)
        {
            this.gameGui = gameGui;
        }

        public string GetAddonName()
        {
            return "TripleTriadResult";
        }

        public void OnAddonLost()
        {
            // meh
        }

        public void OnAddonShown(IntPtr addonPtr)
        {
            needsNotify = true;
            cachedState = new();
        }

        public unsafe void OnAddonUpdate(IntPtr addonPtr)
        {
            var baseNode = (AtkUnitBase*)addonPtr;
            if (baseNode == null)
            {
                return;
            }

            if (needsNotify)
            {
                IntPtr agentPtr = gameGui.FindAgentInterface(addonPtr);
                if (agentPtr != IntPtr.Zero)
                {
                    var agent = (AgentTripleTriad*)agentPtr;
                    cachedState.cardItemId = agent->rewardItemId;
                }

                UpdateCachedState(baseNode);

                if ((cachedState.isDraw || cachedState.isLose || cachedState.isWin) && cachedState.numMGP >= 0)
                {
                    needsNotify = false;
                    OnUpdated?.Invoke(cachedState);
                }
            }
        }

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

            var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);

            var nodeRewards = GUINodeUtils.PickNode(nodeArrL0, 8, 10);
            var nodeArrRewards0 = GUINodeUtils.GetImmediateChildNodes(nodeRewards);
            if (nodeArrRewards0 != null && nodeArrRewards0.Length == 8)
            {
                var nodeCoinsA = GUINodeUtils.PickNode(nodeArrRewards0, 6, 8);
                var nodeCoinsB = GUINodeUtils.PickChildNode(nodeCoinsA, 5, 6);
                var nodeCoinsC = GUINodeUtils.PickChildNode(nodeCoinsB, 1, 2);
                var descCoins = GUINodeUtils.GetNodeText(nodeCoinsC);

                if (!int.TryParse(descCoins, out cachedState.numMGP))
                {
                    cachedState.numMGP = -1;
                }
            }

            var nodeResult = GUINodeUtils.PickNode(nodeArrL0, 9, 10);
            var nodeArrResult0 = GUINodeUtils.GetImmediateChildNodes(nodeResult);
            if (nodeArrResult0 != null && nodeArrResult0.Length == 3)
            {
                cachedState.isDraw = (nodeArrResult0[0] != null) ? nodeArrResult0[0]->IsVisible : false;
                cachedState.isLose = (nodeArrResult0[1] != null) ? nodeArrResult0[1]->IsVisible : false;
                cachedState.isWin = (nodeArrResult0[2] != null) ? nodeArrResult0[2]->IsVisible : false;
            }
        }
    }

    public class UIStateTriadResults
    {
        public int numMGP;
        public bool isWin;
        public bool isDraw;
        public bool isLose;
        public uint cardItemId;
    }
}
