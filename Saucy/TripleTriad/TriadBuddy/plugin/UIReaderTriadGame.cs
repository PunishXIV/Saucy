using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public class UIReaderTriadGame : IUIReader
    {
        [StructLayout(LayoutKind.Explicit, Size = 0x1000)]              // no idea what size, last entries seems to be around +0xfc0? 
        private unsafe struct AddonTripleTriad
        {
            [FieldOffset(0x0)] public AtkUnitBase AtkUnitBase;
            [FieldOffset(0x230)] public byte TurnState;                 // 0: waiting, 1: normal move, 2: masked move (order/chaos)

            [FieldOffset(0x238)] public AddonTripleTriadCard BlueDeck0; // 2be = end of numbers
            [FieldOffset(0x2e0)] public AddonTripleTriadCard BlueDeck1; // 366 = end of numbers
            [FieldOffset(0x388)] public AddonTripleTriadCard BlueDeck2;
            [FieldOffset(0x430)] public AddonTripleTriadCard BlueDeck3;
            [FieldOffset(0x4d8)] public AddonTripleTriadCard BlueDeck4;

            [FieldOffset(0x580)] public AddonTripleTriadCard RedDeck0;
            [FieldOffset(0x628)] public AddonTripleTriadCard RedDeck1;
            [FieldOffset(0x6d0)] public AddonTripleTriadCard RedDeck2;
            [FieldOffset(0x778)] public AddonTripleTriadCard RedDeck3;
            [FieldOffset(0x820)] public AddonTripleTriadCard RedDeck4;

            [FieldOffset(0x8c8)] public AddonTripleTriadCard Board0;
            [FieldOffset(0x970)] public AddonTripleTriadCard Board1;
            [FieldOffset(0xa18)] public AddonTripleTriadCard Board2;
            [FieldOffset(0xac0)] public AddonTripleTriadCard Board3;
            [FieldOffset(0xb68)] public AddonTripleTriadCard Board4;
            [FieldOffset(0xc10)] public AddonTripleTriadCard Board5;
            [FieldOffset(0xcb8)] public AddonTripleTriadCard Board6;
            [FieldOffset(0xd60)] public AddonTripleTriadCard Board7;
            [FieldOffset(0xe08)] public AddonTripleTriadCard Board8;

            [FieldOffset(0xf98)] public byte NumCardsBlue;
            [FieldOffset(0xf99)] public byte NumCardsRed;

            // 0xFCA - int timer blue?
            // 0xFB0 - int timer red?
            // 0xFB4 - idk, 4-ish bytes of something changing
            // 0xFB8 - idk, 4-ish bytes of something changing
        }

        [StructLayout(LayoutKind.Explicit, Size = 0xA8)]
        private unsafe struct AddonTripleTriadCard
        {
            [FieldOffset(0x8)] public AtkComponentBase* CardDropControl;
            [FieldOffset(0x80)] public byte CardRarity;                 // 1..5
            [FieldOffset(0x81)] public byte CardType;                   // 0: no type, 1: primal, 2: scion, 3: beastman, 4: garland
            [FieldOffset(0x82)] public byte CardOwner;                  // 0: empty, 1: blue, 2: red
            [FieldOffset(0x83)] public byte NumSideU;
            [FieldOffset(0x84)] public byte NumSideD;
            [FieldOffset(0x85)] public byte NumSideR;
            [FieldOffset(0x86)] public byte NumSideL;
            [FieldOffset(0xA4)] public bool HasCard;

            // 0x87 - constant per card, changes between npcs
            // 0x88 - fixed per card, not ID
            // 0x89 - fixed per card, 40/41 ?
        }

        public enum Status
        {
            NoErrors,
            AddonNotFound,
            AddonNotVisible,
            PvPMatch,
            FailedToReadMove,
            FailedToReadRules,
            FailedToReadRedPlayer,
            FailedToReadCards,
        }

        public UIStateTriadGame? currentState;
        public Status status = Status.AddonNotFound;
        public bool HasErrors => status >= Status.FailedToReadMove;
        public bool IsVisible => (status != Status.AddonNotFound) && (status != Status.AddonNotVisible);

        public event Action<UIStateTriadGame?>? OnUIStateChanged;

        private IntPtr addonPtr;

        public string GetAddonName()
        {
            return "TripleTriad";
        }

        public void OnAddonLost()
        {
            SetStatus(Status.AddonNotFound);
            SetCurrentState(null);
            addonPtr = IntPtr.Zero;
        }

        public void OnAddonShown(IntPtr addonPtr)
        {
            this.addonPtr = addonPtr;
        }

        public unsafe void OnAddonUpdate(IntPtr addonPtr)
        {
            var addon = (AddonTripleTriad*)addonPtr;
            if (addon == null)
            {
                return;
            }

            status = Status.NoErrors;
            var newState = new UIStateTriadGame();

            var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(addon->AtkUnitBase.RootNode);
            newState.redPlayerDesc = GetUIDescriptionRedPlayer(nodeArrL0);
            newState.rules = GetUIDescriptionRules(nodeArrL0);
            newState.isPvP = GetUIStatePvP(nodeArrL0);

            if (status == Status.NoErrors)
            {
                newState.move = addon->TurnState;
                if (newState.move > 2)
                {
                    SetStatus(Status.FailedToReadMove);
                }
            }

            if (status == Status.NoErrors)
            {
                newState.blueDeck[0] = GetCardData(addon->BlueDeck0);
                newState.blueDeck[1] = GetCardData(addon->BlueDeck1);
                newState.blueDeck[2] = GetCardData(addon->BlueDeck2);
                newState.blueDeck[3] = GetCardData(addon->BlueDeck3);
                newState.blueDeck[4] = GetCardData(addon->BlueDeck4);

                newState.redDeck[0] = GetCardData(addon->RedDeck0);
                newState.redDeck[1] = GetCardData(addon->RedDeck1);
                newState.redDeck[2] = GetCardData(addon->RedDeck2);
                newState.redDeck[3] = GetCardData(addon->RedDeck3);
                newState.redDeck[4] = GetCardData(addon->RedDeck4);

                newState.board[0] = GetCardData(addon->Board0);
                newState.board[1] = GetCardData(addon->Board1);
                newState.board[2] = GetCardData(addon->Board2);
                newState.board[3] = GetCardData(addon->Board3);
                newState.board[4] = GetCardData(addon->Board4);
                newState.board[5] = GetCardData(addon->Board5);
                newState.board[6] = GetCardData(addon->Board6);
                newState.board[7] = GetCardData(addon->Board7);
                newState.board[8] = GetCardData(addon->Board8);
            }

            SetCurrentState(status == Status.NoErrors ? newState : null);

            // pvp status is part of UI state, allow setting detection, just make sure that solver will be disabled
            // if this flag is not set in pvp match, red player description will not match known npc and solver will be in failed state anyway
            if (newState.isPvP)
            {
                SetStatus(Status.PvPMatch);
            }
        }

        private void SetStatus(Status newStatus)
        {
            if (status != newStatus)
            {
                status = newStatus;
                if (HasErrors)
                {
                    Service.logger.Error("ui reader error: " + newStatus);
                }
            }
        }

        private void SetCurrentState(UIStateTriadGame? newState)
        {
            bool isEmpty = newState == null;
            bool wasEmpty = currentState == null;

            if (isEmpty && wasEmpty)
            {
                return;
            }

            bool changed = (isEmpty != wasEmpty);
            if (!changed && newState != null && currentState != null)
            {
                changed = !currentState.Equals(newState);
            }

            if (changed)
            {
                currentState = newState;
                OnUIStateChanged?.Invoke(newState);
            }
        }

        private unsafe List<string> GetUIDescriptionRedPlayer(AtkResNode*[]? level0)
        {
            var listRedDesc = new List<string>();

            var nodeName0 = GUINodeUtils.PickNode(level0, 6, 12);
            var nodeArrNameL1 = GUINodeUtils.GetImmediateChildNodes(nodeName0);
            var nodeNameL1 = GUINodeUtils.PickNode(nodeArrNameL1, 0, 5);
            var nodeArrNameL2 = GUINodeUtils.GetAllChildNodes(nodeNameL1);
            // there are multiple text boxes, for holding different combinations of name & titles?
            // idk, too lazy to investigate, grab everything inside
            int numParsed = 0;
            if (nodeArrNameL2 != null)
            {
                foreach (var testNode in nodeArrNameL2)
                {
                    var isVisible = (testNode != null) ? (testNode->NodeFlags & NodeFlags.Visible) == NodeFlags.Visible : false;
                    if (isVisible)
                    {
                        numParsed++;
                        var text = GUINodeUtils.GetNodeText(testNode);
                        if (!string.IsNullOrEmpty(text))
                        {
                            listRedDesc.Add(text);
                        }
                    }
                }
            }

            if (numParsed == 0)
            {
                SetStatus(Status.FailedToReadRedPlayer);
            }

            return listRedDesc;
        }

        private unsafe List<string> GetUIDescriptionRules(AtkResNode*[]? level0)
        {
            var listRuleDesc = new List<string>();

            var nodeRule0 = GUINodeUtils.PickNode(level0, 4, 12);
            var nodeArrRule = GUINodeUtils.GetImmediateChildNodes(nodeRule0);
            if (nodeArrRule != null && nodeArrRule.Length == 5)
            {
                for (int idx = 0; idx < 4; idx++)
                {
                    var text = GUINodeUtils.GetNodeText(nodeArrRule[4 - idx]);
                    if (!string.IsNullOrEmpty(text))
                    {
                        listRuleDesc.Add(text);
                    }
                }
            }
            else
            {
                SetStatus(Status.FailedToReadRules);
            }


            return listRuleDesc;
        }

        private unsafe bool GetUIStatePvP(AtkResNode*[]? level0)
        {
            // verify, is that for both pvp and tournament games?
            var nodePvPButton = GUINodeUtils.PickNode(level0, 11, 12);
            if (nodePvPButton != null && nodePvPButton->IsVisible())
            {
                return true;
            }

            return false;
        }

        private unsafe (string, bool) GetCardTextureData(AddonTripleTriadCard addonCard)
        {
            // DragDrop Component
            // [1] Icon Component
            //     [0] Base Component <- locked out colors here
            //         [3] Image Node
            var nodeA = GUINodeUtils.PickChildNode(addonCard.CardDropControl, 1, 3);
            var nodeB = GUINodeUtils.PickChildNode(nodeA, 0, 2);
            var nodeC = GUINodeUtils.PickChildNode(nodeB, 3, 21);
            var texPath = GUINodeUtils.GetNodeTexturePath(nodeC);

            if (nodeC == null)
            {
                SetStatus(Status.FailedToReadCards);
            }

            bool isLocked = (nodeB != null) && (nodeB->MultiplyRed < 100);
            return (texPath ?? "", isLocked);
        }

        private unsafe UIStateTriadCard GetCardData(AddonTripleTriadCard addonCard)
        {
            var resultOb = new UIStateTriadCard();
            if (addonCard.HasCard)
            {
                resultOb.isPresent = true;
                resultOb.owner = addonCard.CardOwner;

                bool isKnown = (addonCard.NumSideU != 0);
                if (isKnown)
                {
                    resultOb.numU = addonCard.NumSideU;
                    resultOb.numL = addonCard.NumSideL;
                    resultOb.numD = addonCard.NumSideD;
                    resultOb.numR = addonCard.NumSideR;
                    resultOb.rarity = addonCard.CardRarity;
                    resultOb.type = addonCard.CardType;

                    (resultOb.texturePath, resultOb.isLocked) = GetCardTextureData(addonCard);
                }
            }

            return resultOb;
        }

        private unsafe (Vector2, Vector2) GetCardPosAndSize(AddonTripleTriadCard addonCard)
        {
            if (addonCard.CardDropControl != null && addonCard.CardDropControl->OwnerNode != null)
            {
                var resNode = &addonCard.CardDropControl->OwnerNode->AtkResNode;
                return GUINodeUtils.GetNodePosAndSize(resNode);
            }

            return (Vector2.Zero, Vector2.Zero);
        }

        public unsafe (Vector2, Vector2) GetBlueCardPosAndSize(int idx)
        {
            if (addonPtr != IntPtr.Zero)
            {
                var addon = (AddonTripleTriad*)addonPtr;
                switch (idx)
                {
                    case 0: return GetCardPosAndSize(addon->BlueDeck0);
                    case 1: return GetCardPosAndSize(addon->BlueDeck1);
                    case 2: return GetCardPosAndSize(addon->BlueDeck2);
                    case 3: return GetCardPosAndSize(addon->BlueDeck3);
                    case 4: return GetCardPosAndSize(addon->BlueDeck4);
                    default: break;
                }
            }

            return (Vector2.Zero, Vector2.Zero);
        }

        public unsafe (Vector2, Vector2) GetBoardCardPosAndSize(int idx)
        {
            if (addonPtr != IntPtr.Zero)
            {
                var addon = (AddonTripleTriad*)addonPtr;
                switch (idx)
                {
                    case 0: return GetCardPosAndSize(addon->Board0);
                    case 1: return GetCardPosAndSize(addon->Board1);
                    case 2: return GetCardPosAndSize(addon->Board2);
                    case 3: return GetCardPosAndSize(addon->Board3);
                    case 4: return GetCardPosAndSize(addon->Board4);
                    case 5: return GetCardPosAndSize(addon->Board5);
                    case 6: return GetCardPosAndSize(addon->Board6);
                    case 7: return GetCardPosAndSize(addon->Board7);
                    case 8: return GetCardPosAndSize(addon->Board8);
                    default: break;
                }
            }

            return (Vector2.Zero, Vector2.Zero);
        }
    }
}
