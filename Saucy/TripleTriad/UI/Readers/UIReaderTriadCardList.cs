using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
namespace Saucy.TripleTriad.UI;

internal static class CardListFilterMapping
{
    public static byte ToCollectionFilter(CardListFilterMode mode) =>
        mode switch
        {
            CardListFilterMode.OwnedOnly => (byte)GameCardCollectionFilter.OnlyOwned,
            CardListFilterMode.MissingOnly => (byte)GameCardCollectionFilter.OnlyMissing,
            var _ => (byte)GameCardCollectionFilter.All
        };
}

public unsafe class UIReaderTriadCardList : IUIReader
{
    public enum Status
    {
        NoErrors,
        AddonNotFound,
        AddonNotVisible,
        NodesNotReady
    }

    private nint cachedAddonAgentPtr;

    public UIStateTriadCardList cachedState = new();
    private int lastNotifiedCardId = -1;
    public Action<UIStateTriadCardList>? OnUIStateChanged;
    public Action<bool>? OnVisibilityChanged;
    private int pendingNavAttempts;
    private int pendingNavCardId;
    private int pendingNavCell = -1;
    private int pendingNavGraceFrames;
    private int pendingNavPage = -1;
    private int pendingNavSourceCardId = -1;

    public Status status = Status.AddonNotFound;
    public bool IsVisible => status is not Status.AddonNotFound and not Status.AddonNotVisible;
    public bool IsPendingCardNavigation => pendingNavPage >= 0;

    public string GetAddonName() => "GSInfoCardList";

    public void OnAddonLost()
    {
        cachedAddonAgentPtr = nint.Zero;
        ClearPendingNavigation();
        SetStatus(Status.AddonNotFound);
    }

    public void OnAddonShown(nint addonPtr)
    {
        cachedAddonAgentPtr = (addonPtr != nint.Zero) ? Svc.GameGui.FindAgentInterface(addonPtr) : nint.Zero;

        if (cachedAddonAgentPtr == nint.Zero)
        {
            cachedAddonAgentPtr = LoadFailsafeAgent();
#if DEBUG
            Svc.Log.Info($"using agentPtr from failsafe: {(ulong)cachedAddonAgentPtr:X}");
#endif
        }

        if (addonPtr != nint.Zero)
        {
            var addon = (AddonGSInfoCardList*)addonPtr;
            if (addon->AtkUnitBase.RootNode != null)
            {
                (cachedState.screenPos, cachedState.screenSize) =
                    GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);
            }

            SetStatus(Status.NodesNotReady);
        }
    }

    public void OnAddonUpdate(nint addonPtr)
    {
        var addon = (AddonGSInfoCardList*)addonPtr;
        (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);

        var descNode = addon->SelectedCardDescription;
        if (descNode == null)
        {
            SetStatus(Status.NodesNotReady);
            return;
        }

        (cachedState.descriptionPos, cachedState.descriptionSize) =
            GUINodeUtils.GetNodePosAndSize(&descNode->AtkResNode);

        var newPageIndex = (byte)addon->SelectedPage;
        var newCardIndex = (byte)addon->SelectedCardIndex;
        var newFilterMode = CardListFilterMapping.ToCollectionFilter(CardListFilterMode.All);

        AgentGoldSaucer* agent = null;
        if (cachedAddonAgentPtr != nint.Zero)
        {
            agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
            newFilterMode = CardListFilterMapping.ToCollectionFilter(agent->CardListFilterMode);
        }

        var displayCardId = TriadCardListSelectionReader.TryParseCardIdFromDisplayLabel(addon);
        var gameSelectedCardId = TriadCardListSelectionReader.ReadSelectedCardId(addon, newFilterMode, agent, displayCardId);
        var newSelectionMasked = TriadCardListSelectionReader.IsMaskedUnownedSelection(addon, displayCardId);
        var selectedCardId = gameSelectedCardId;

        if (pendingNavPage >= 0 && pendingNavCardId > 0)
        {
            if (pendingNavGraceFrames > 0)
            {
                pendingNavGraceFrames--;
            }

            var atPendingCell = newPageIndex == pendingNavPage && newCardIndex == pendingNavCell;

            if (IsPendingNavigationComplete(addon))
            {
                ClearPendingNavigation();
            }
            else if (gameSelectedCardId == pendingNavCardId)
            {
                ClearPendingNavigation();
            }
            else if (gameSelectedCardId > 0 &&
                     gameSelectedCardId != pendingNavSourceCardId &&
                     gameSelectedCardId != pendingNavCardId)
            {
                ClearPendingNavigation();
            }
            else if (gameSelectedCardId == pendingNavSourceCardId && !atPendingCell)
            {
                selectedCardId = pendingNavCardId;
                if (IsCardUnowned(pendingNavCardId))
                {
                    newSelectionMasked = true;
                }
            }
        }

        var selectionChanged = cachedState.pageIndex != newPageIndex ||
                               cachedState.cardIndex != newCardIndex ||
                               cachedState.filterMode != newFilterMode ||
                               cachedState.selectionMasked != newSelectionMasked ||
                               cachedState.iconId != addon->CardIconId ||
                               cachedState.numU != addon->NumSideU ||
                               cachedState.numL != addon->NumSideL ||
                               cachedState.numD != addon->NumSideD ||
                               cachedState.numR != addon->NumSideR ||
                               cachedState.rarity != addon->CardRarity ||
                               cachedState.type != (byte)addon->CardType ||
                               cachedState.selectedCardId != selectedCardId;

        cachedState.numU = addon->NumSideU;
        cachedState.numL = addon->NumSideL;
        cachedState.numD = addon->NumSideD;
        cachedState.numR = addon->NumSideR;
        cachedState.rarity = addon->CardRarity;
        cachedState.type = (byte)addon->CardType;
        cachedState.iconId = addon->CardIconId;
        cachedState.pageIndex = newPageIndex;
        cachedState.cardIndex = newCardIndex;
        cachedState.filterMode = newFilterMode;
        cachedState.isDeckEditMode = TriadDeckEditUi.IsDeckEditScreenOpen();
        cachedState.selectedCardId = selectedCardId;
        cachedState.selectionMasked = newSelectionMasked;

        var resolvedId = cachedState.ResolveCardId(new());
        if (selectionChanged || resolvedId != lastNotifiedCardId)
        {
            lastNotifiedCardId = resolvedId;
            OnUIStateChanged?.Invoke(cachedState);
        }

        TickPendingCardNavigation(addonPtr);

        SetStatus(Status.NoErrors);
    }

    public void RefreshLiveSelectionState()
    {
        var addonPtr = ResolveAddonPtr();
        if (addonPtr != nint.Zero)
        {
            OnAddonUpdate(addonPtr);
        }
    }

    public TriadCard? ResolveSelectedCard()
    {
        if (cachedState.selectedCardId > 0)
        {
            var card = TriadCardDB.Get().FindById(cachedState.selectedCardId);
            if (card != null)
            {
                return card;
            }
        }

        if (cachedState.IsMaskedSelection())
        {
            return cachedState.ToTriadCardFromGrid(new());
        }

        var fromGrid = cachedState.ToTriadCardFromGrid(new());
        if (fromGrid != null)
        {
            return fromGrid;
        }

        var fromIcon = TriadCardDB.Get().TryGetCardIdFromIconId(cachedState.iconId);
        if (fromIcon >= 0)
        {
            return TriadCardDB.Get().FindById(fromIcon);
        }

        return cachedState.ToTriadCard(new());
    }

    public bool SetPageAndGridView(int pageIndex, int cellIndex, int cardId = 0)
    {
        var addonPtr = ResolveAddonPtr();
        OnAddonShown(addonPtr);

        if (addonPtr == nint.Zero || cachedAddonAgentPtr == nint.Zero)
        {
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
        var filterMode = CardListFilterMapping.ToCollectionFilter(agent->CardListFilterMode);
        var displayCardId = TriadCardListSelectionReader.TryParseCardIdFromDisplayLabel(addon);
        pendingNavSourceCardId = TriadCardListSelectionReader.ReadSelectedCardId(addon, filterMode, agent, displayCardId);

        pendingNavCardId = cardId;
        pendingNavAttempts = 90;
        pendingNavGraceFrames = 5;

        if (pageIndex < 0 || pageIndex >= GameCardDB.MaxGridPages || cellIndex < 0 || cellIndex >= GameCardDB.MaxGridCells)
        {
            pendingNavPage = -1;
            pendingNavCell = -1;
            if (cardId > 0)
            {
                cachedState.selectedCardId = cardId;
                cachedState.selectionMasked = IsCardUnowned(cardId);
                OnUIStateChanged?.Invoke(cachedState);
            }

            return false;
        }

        pendingNavPage = pageIndex;
        pendingNavCell = cellIndex;

        if (cardId > 0)
        {
            cachedState.selectedCardId = cardId;
            cachedState.selectionMasked = IsCardUnowned(cardId);
        }

        OnUIStateChanged?.Invoke(cachedState);

        TickPendingCardNavigation(addonPtr);
        return true;
    }

    private static bool IsCardUnowned(int cardId)
    {
        if (cardId <= 0)
        {
            return false;
        }

        if (TriadMemoryReads.IsAvailable)
        {
            return !TriadMemoryReads.TryIsCardOwned(cardId);
        }

        return !GameCardDB.Get().ownedCardIds.Contains(cardId);
    }

    private void ClearPendingNavigation()
    {
        pendingNavPage = -1;
        pendingNavCell = -1;
        pendingNavCardId = 0;
        pendingNavAttempts = 0;
        pendingNavGraceFrames = 0;
        pendingNavSourceCardId = -1;
    }

    private void TickPendingCardNavigation(nint addonPtr)
    {
        if (pendingNavPage < 0 || addonPtr == nint.Zero)
        {
            return;
        }

        if (--pendingNavAttempts <= 0)
        {
            ClearPendingNavigation();
            return;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        if (cachedAddonAgentPtr != nint.Zero)
        {
            var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
            agent->EditDeckSelectedPage = pendingNavPage;
            agent->EditDeckSelectedCardIndex = pendingNavCell;
        }

        if (addon->SelectedPage != pendingNavPage)
        {
            addon->RequestedPage = pendingNavPage;
            addon->TabController.SetTabIndexAndCallBack(pendingNavPage);
            addon->AtkUnitBase.Update(0);
            return;
        }

        if (!GoldSaucerCardListUi.TryClickCell(addonPtr, pendingNavCell))
        {
            return;
        }

        if (IsPendingNavigationComplete(addon))
        {
            ClearPendingNavigation();
        }
    }

    private bool IsPendingNavigationComplete(AddonGSInfoCardList* addon)
    {
        if (addon->SelectedPage != pendingNavPage || addon->SelectedCardIndex != pendingNavCell)
        {
            return false;
        }

        if (pendingNavCardId <= 0)
        {
            return true;
        }

        var gridMatch = GameCardDB.Get().FindByGridLocationAnyFilter(
            pendingNavPage,
            pendingNavCell,
            cachedState.filterMode);
        if (gridMatch?.CardId == pendingNavCardId)
        {
            return true;
        }

        var fromDisplay = TriadCardListSelectionReader.TryParseCardIdFromDisplayLabel(addon);
        if (fromDisplay == pendingNavCardId)
        {
            return true;
        }

        if (TriadCardListSelectionReader.IconMatchesCard(addon->CardIconId, pendingNavCardId))
        {
            return AddonStatsMatchCard(addon, pendingNavCardId);
        }

        return false;
    }

    private static bool AddonStatsMatchCard(AddonGSInfoCardList* addon, int cardId)
    {
        var hasSideStats = addon->NumSideU != 0 || addon->NumSideL != 0 || addon->NumSideD != 0 || addon->NumSideR != 0;
        if (!hasSideStats)
        {
            return true;
        }

        var expectedCard = TriadCardDB.Get().FindById(cardId);
        if (expectedCard == null || expectedCard.Sides == null || expectedCard.Sides.Length < 4)
        {
            return true;
        }

        return addon->NumSideU == expectedCard.Sides[0] &&
               addon->NumSideL == expectedCard.Sides[1] &&
               addon->NumSideD == expectedCard.Sides[2] &&
               addon->NumSideR == expectedCard.Sides[3];
    }

    private static nint ResolveAddonPtr()
    {
        for (var i = 0; i < 8; i++)
        {
            var handle = Svc.GameGui.GetAddonByName("GSInfoCardList", i);
            if (handle.Address != nint.Zero)
            {
                return handle.Address;
            }
        }

        return nint.Zero;
    }

    public static nint LoadFailsafeAgent()
    {
        var uiModule = (UIModule*)Svc.GameGui.GetUIModule().Address;
        if (uiModule != null)
        {
            var agentModule = uiModule->GetAgentModule();
            if (agentModule != null)
            {
                var agentPtr = agentModule->GetAgentByInternalId(AgentId.GoldSaucer);
                if (agentPtr != null)
                {
                    return new(agentPtr);
                }
            }
        }

        return nint.Zero;
    }

    private void SetStatus(Status newStatus)
    {
        if (status != newStatus)
        {
            var wasVisible = IsVisible;
            status = newStatus;

            if (wasVisible != IsVisible)
            {
                OnVisibilityChanged?.Invoke(IsVisible);
            }
        }
    }
}
