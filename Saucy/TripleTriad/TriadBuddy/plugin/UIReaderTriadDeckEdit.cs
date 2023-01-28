using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public class UIReaderTriadDeckEdit : IUIReader
    {
        [StructLayout(LayoutKind.Explicit, Size = 0xD70)]
        private unsafe struct AddonTriadDeckEdit
        {
            [FieldOffset(0x0)] public AtkUnitBase AtkUnitBase;

            [FieldOffset(0xD64)] public byte PageIndex;                 // ignores writes
            [FieldOffset(0xD68)] public byte CardIndex;                 // can be written to, yay!
        }

        public UIStateTriadDeckEdit cachedState = new();
        public Action<bool> OnVisibilityChanged;

        public bool IsVisible { get; private set; }

        private float blinkAlpha;
        private bool isOptimizerActive;

        private List<string> highlightTexPaths = new();
        private readonly GameGui gameGui;

        public UIReaderTriadDeckEdit(GameGui gameGui)
        {
            this.gameGui = gameGui;
            blinkAlpha = 0.0f;
        }

        public string GetAddonName()
        {
            return "GSInfoEditDeck";
        }

        public void OnAddonLost()
        {
            IsVisible = false;
            OnVisibilityChanged?.Invoke(false);
        }

        public void OnAddonShown(IntPtr addonPtr)
        {
            IsVisible = true;
            OnVisibilityChanged?.Invoke(true);
        }

        public unsafe void OnAddonUpdate(IntPtr addonPtr)
        {
            var addon = (AddonTriadDeckEdit*)addonPtr;
            blinkAlpha = (blinkAlpha + ImGui.GetIO().DeltaTime) % 1.0f;

            // root, 14 children (sibling scan)
            //     [9] res node, card icon grid + fancy stuff
            //         [0] res node, just card icon grid
            //             [x] DragDrop components for each card, 3 children on node list
            //                 [2] icon component, 7 children on node list
            //                     [0] image node

            var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(addon->AtkUnitBase.RootNode);
            var nodeA = GUINodeUtils.PickNode(nodeArrL0, 9, 14);
            var nodeB = GUINodeUtils.GetChildNode(nodeA);
            var nodeArrCards = GUINodeUtils.GetImmediateChildNodes(nodeB);
            if (nodeArrCards != null)
            {
                foreach (var nodeD in nodeArrCards)
                {
                    var nodeE = GUINodeUtils.PickChildNode(nodeD, 2, 3);
                    var nodeImage = GUINodeUtils.PickChildNode(nodeE, 0, 7);

                    if (nodeImage != null)
                    {
                        if (!isOptimizerActive)
                        {
                            // no optimizer: reset highlights
                            nodeImage->MultiplyBlue = 100;
                            nodeImage->MultiplyRed = 100;
                            nodeImage->MultiplyGreen = 100;
                        }
                        else
                        {
                            var texPath = GUINodeUtils.GetNodeTexturePath(nodeImage);
                            bool shouldHighlight = IsCardTexPathMatching(texPath);

                            // lerp color:
                            //   t0 .. t0.5 = 0 -> 100%
                            //   t0.5 .. t1 -> hold 100%
                            float colorAlpha = (blinkAlpha < 0.5f) ? (blinkAlpha * 2.0f) : 1.0f;
                            byte colorV = (byte)(shouldHighlight ? (50 + 50 * colorAlpha) : 25);

                            nodeImage->MultiplyBlue = colorV;
                            nodeImage->MultiplyRed = colorV;
                            nodeImage->MultiplyGreen = colorV;
                        }
                    }
                }
            }

            (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);
            cachedState.pageIndex = addon->PageIndex;
            cachedState.cardIndex = addon->CardIndex;
        }

        public void OnDeckOptimizerVisible(bool isVisible)
        {
            isOptimizerActive = isVisible;
            highlightTexPaths.Clear();
        }

        public void SetHighlightedCards(int[] cardIds)
        {
            highlightTexPaths.Clear();
            if (cardIds != null)
            {
                foreach (int cardId in cardIds)
                {
                    // see TriadCardDB.FindByTexture for details on patterns
                    var pathPattern = string.Format("082000/{0:D6}", FFTriadBuddy.TriadCardDB.GetCardIconTextureId(cardId));
                    highlightTexPaths.Add(pathPattern);
                }
            }
        }

        private bool IsCardTexPathMatching(string texPath)
        {
            foreach (var pathPattern in highlightTexPaths)
            {
                if (texPath.Contains(pathPattern))
                {
                    return true;
                }
            }

            return false;
        }

        public unsafe bool SetPageAndGridView(int pageIndex, int cellIndex)
        {
            // doesn't really belong to a ui "reader", but won't be making a class just for calling one function

            // basic sanity checks on values before writing them to memory
            // this will NOT be enough when filters are active!
            if (pageIndex < 0 || pageIndex >= GameCardDB.MaxGridPages || cellIndex < 0 || cellIndex >= GameCardDB.MaxGridCells)
            {
                return false;
            }

            // agentPtr is NOT available through deck edit addon here
            // use GSInfoCardDeck instead

            IntPtr addonPtr = gameGui.GetAddonByName(GetAddonName(), 1);
            IntPtr agentPtr = gameGui.FindAgentInterface("GSInfoCardDeck");
            if (agentPtr == IntPtr.Zero)
            {
                agentPtr = UIReaderTriadCardList.LoadFailsafeAgent(gameGui);
            }

            if (addonPtr != IntPtr.Zero && agentPtr != IntPtr.Zero)
            {
                OnAddonShown(addonPtr);

                var addon = (AddonTriadDeckEdit*)addonPtr;
                var agent = (UIReaderTriadCardList.AgentTriadCardList*)agentPtr;

                // reset all filters, otherwise page/card won't be correct
                agent->FilterDeckTypeRarity = 0;
                agent->FilterDeckSides = 0;
                agent->FilterDeckSorting = 0;
                agent->FilterMode = 0;

                agent->PageIndex = pageIndex;
                addon->CardIndex = (byte)cellIndex;

                return true;
            }

            return false;
        }
    }

    public class UIStateTriadDeckEdit
    {
        public Vector2 screenPos;
        public Vector2 screenSize;

        public byte pageIndex;
        public byte cardIndex;
    }
}
