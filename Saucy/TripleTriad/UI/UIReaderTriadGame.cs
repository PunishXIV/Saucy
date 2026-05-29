using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadGame : IUIReader
{
    public enum Status
    {
        NoErrors,
        AddonNotFound,
        AddonNotVisible,
        PvPMatch,
        FailedToReadMove,
        FailedToReadRules,
        FailedToReadRedPlayer,
        FailedToReadCards
    }

    private nint addonPtr;

    public UIStateTriadGame? currentState;
    public Status status = Status.AddonNotFound;
    public bool HasErrors => status >= Status.FailedToReadMove;
    public bool IsVisible => status is not Status.AddonNotFound and not Status.AddonNotVisible;

    public string GetAddonName() => "TripleTriad";

    public void OnAddonLost()
    {
        SetStatus(Status.AddonNotFound);
        SetCurrentState(null);
        addonPtr = nint.Zero;
    }

    public void OnAddonShown(nint addonPtr) => this.addonPtr = addonPtr;

    public unsafe void OnAddonUpdate(nint addonPtr)
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
            newState.move = (byte)addon->TurnState;
            if (newState.move > 2)
            {
                SetStatus(Status.FailedToReadMove);
            }
        }

        if (status == Status.NoErrors)
        {
            for (var idx = 0; idx < 5; idx++)
            {
                newState.blueDeck[idx] = GetCardData(addon->BlueDeck[idx]);
                newState.redDeck[idx] = GetCardData(addon->RedDeck[idx]);
            }

            for (var idx = 0; idx < 9; idx++)
            {
                newState.board[idx] = GetCardData(addon->Board[idx]);
            }
        }

        SetCurrentState(status == Status.NoErrors ? newState : null);

        if (newState.isPvP)
        {
            SetStatus(Status.PvPMatch);
        }
    }

    public event Action<UIStateTriadGame?>? OnUIStateChanged;

    private void SetStatus(Status newStatus)
    {
        if (status != newStatus)
        {
            status = newStatus;
            if (HasErrors)
            {
                Svc.Log.Error("ui reader error: " + newStatus);
            }
        }
    }

    private void SetCurrentState(UIStateTriadGame? newState)
    {
        var isEmpty = newState == null;
        var wasEmpty = currentState == null;

        if (isEmpty && wasEmpty)
        {
            return;
        }

        var changed = (isEmpty != wasEmpty);
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
        var numParsed = 0;
        if (nodeArrNameL2 != null)
        {
            foreach (var testNode in nodeArrNameL2)
            {
                var isVisible = (testNode != null) && (testNode->NodeFlags & NodeFlags.Visible) == NodeFlags.Visible;
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
            for (var idx = 0; idx < 4; idx++)
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
        var nodePvPButton = GUINodeUtils.PickNode(level0, 11, 12);
        return nodePvPButton != null && nodePvPButton->IsVisible();
    }

    private unsafe (string, bool) GetCardTextureData(AddonTripleTriad.TripleTriadCard addonCard)
    {
        var nodeA = GUINodeUtils.PickChildNode(addonCard.CardDropControl, 1, 3);
        var nodeB = GUINodeUtils.PickChildNode(nodeA, 0, 2);
        var nodeC = GUINodeUtils.PickChildNode(nodeB, 3, 21);
        var texPath = GUINodeUtils.GetNodeTexturePath(nodeC);

        if (nodeC == null)
        {
            SetStatus(Status.FailedToReadCards);
        }

        var isLocked = (nodeB != null) && (nodeB->MultiplyRed < 100);
        return (texPath ?? "", isLocked);
    }

    private UIStateTriadCard GetCardData(AddonTripleTriad.TripleTriadCard addonCard)
    {
        var resultOb = new UIStateTriadCard();
        if (addonCard.HasCard)
        {
            resultOb.isPresent = true;
            resultOb.owner = (byte)addonCard.CardOwner;

            var isKnown = (addonCard.NumSideU != 0);
            if (isKnown)
            {
                resultOb.numU = addonCard.NumSideU;
                resultOb.numL = addonCard.NumSideL;
                resultOb.numD = addonCard.NumSideD;
                resultOb.numR = addonCard.NumSideR;
                resultOb.rarity = addonCard.CardRarity;
                resultOb.type = (byte)addonCard.CardType;

                (resultOb.texturePath, resultOb.isLocked) = GetCardTextureData(addonCard);
            }
        }

        return resultOb;
    }

    private unsafe (Vector2, Vector2) GetCardPosAndSize(AddonTripleTriad.TripleTriadCard addonCard)
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
        if (addonPtr != nint.Zero && idx is >= 0 and < 5)
        {
            var addon = (AddonTripleTriad*)addonPtr;
            return GetCardPosAndSize(addon->BlueDeck[idx]);
        }

        return (Vector2.Zero, Vector2.Zero);
    }

    public unsafe (Vector2, Vector2) GetBoardCardPosAndSize(int idx)
    {
        if (addonPtr != nint.Zero && idx is >= 0 and < 9)
        {
            var addon = (AddonTripleTriad*)addonPtr;
            return GetCardPosAndSize(addon->Board[idx]);
        }

        return (Vector2.Zero, Vector2.Zero);
    }
}
