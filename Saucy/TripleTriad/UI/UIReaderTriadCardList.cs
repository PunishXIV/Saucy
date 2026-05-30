using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Numerics;
namespace Saucy.TripleTriad.UI;

internal static class CardListFilterMapping
{
    // GSInfoCardListFilterMode native values (not 0/1/2).
    private const int DisplayAllCards = 14;
    private const int DisplayOwnedCards = 6;
    private const int DisplayUnownedCards = 9;

    public static byte ToCollectionIndex(int rawFilterMode) =>
        rawFilterMode switch
        {
            DisplayOwnedCards or 1 => (byte)GameCardCollectionFilter.OnlyOwned,
            DisplayUnownedCards or 2 => (byte)GameCardCollectionFilter.OnlyMissing,
            DisplayAllCards or 0 => (byte)GameCardCollectionFilter.All,
            _ => (byte)GameCardCollectionFilter.All
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
    private int pendingNavPage = -1;
    private int pendingNavCell = -1;
    private int pendingNavCardId;
    private int pendingNavAttempts;
    private int lastNotifiedCardId = -1;

    public UIStateTriadCardList cachedState = new();
    public Action<UIStateTriadCardList>? OnUIStateChanged;
    public Action<bool>? OnVisibilityChanged;

    public Status status = Status.AddonNotFound;
    public bool IsVisible => status is not Status.AddonNotFound and not Status.AddonNotVisible;
    public bool HasErrors => false;

    public string GetAddonName() => "GSInfoCardList";

    public void OnAddonLost()
    {
        cachedAddonAgentPtr = nint.Zero;
        ClearPendingNavigation();
        SetStatus(Status.AddonNotFound);
    }

    public unsafe void OnAddonShown(nint addonPtr)
    {
        cachedAddonAgentPtr = (addonPtr != nint.Zero) ? Svc.GameGui.FindAgentInterface(addonPtr) : nint.Zero;

        if (cachedAddonAgentPtr == nint.Zero)
        {
            cachedAddonAgentPtr = LoadFailsafeAgent();
#if DEBUG
            Svc.Log.Info($"using agentPtr from failsafe: {(ulong)cachedAddonAgentPtr:X}");
#endif // DEBUG
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

    public unsafe void OnAddonUpdate(nint addonPtr)
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
        var rawFilterMode = (int)CardListFilterMode.All;
        var newFilterMode = CardListFilterMapping.ToCollectionIndex(rawFilterMode);

        if (cachedAddonAgentPtr != nint.Zero)
        {
            var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
            rawFilterMode = (int)agent->CardListFilterMode;
            newFilterMode = CardListFilterMapping.ToCollectionIndex(rawFilterMode);

            if (pendingNavPage >= 0)
            {
                if (agent->EditDeckSelectedPage >= 0 && agent->EditDeckSelectedPage < GameCardDB.MaxGridPages)
                {
                    newPageIndex = (byte)agent->EditDeckSelectedPage;
                }

                if (agent->EditDeckSelectedCardIndex >= 0 &&
                    agent->EditDeckSelectedCardIndex < GameCardDB.MaxGridCells)
                {
                    newCardIndex = (byte)agent->EditDeckSelectedCardIndex;
                }
            }
            else
            {
                if (agent->EditDeckSelectedPage >= 0 && agent->EditDeckSelectedPage < GameCardDB.MaxGridPages)
                {
                    newPageIndex = (byte)agent->EditDeckSelectedPage;
                }

                if (agent->EditDeckSelectedCardIndex >= 0 &&
                    agent->EditDeckSelectedCardIndex < GameCardDB.MaxGridCells)
                {
                    newCardIndex = (byte)agent->EditDeckSelectedCardIndex;
                }
            }
        }

        var selectedCardId = TryReadSelectedCardId(addon);

        var selectionChanged = cachedState.pageIndex != newPageIndex ||
                               cachedState.cardIndex != newCardIndex ||
                               cachedState.filterMode != newFilterMode ||
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
        cachedState.selectedCardId = selectedCardId;

        var resolvedId = cachedState.ResolveCardId(new GameUIParser());
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
        if (pendingNavCardId > 0)
        {
            var pendingCard = TriadCardDB.Get().FindById(pendingNavCardId);
            if (pendingCard != null)
            {
                var resolved = cachedState.ToTriadCard(new GameUIParser());
                if (resolved == null || resolved.Id != pendingNavCardId)
                {
                    return pendingCard;
                }
            }
        }

        if (cachedState.selectedCardId > 0)
        {
            var cardFromNumber = TriadCardDB.Get().FindById(cachedState.selectedCardId);
            if (cardFromNumber != null)
            {
                return cardFromNumber;
            }
        }

        if (cachedState.iconId >= 88000)
        {
            var cardFromIcon = TriadCardDB.Get().FindById(cachedState.iconId - 88000);
            if (cardFromIcon != null)
            {
                return cardFromIcon;
            }
        }

        return cachedState.ToTriadCard(new GameUIParser());
    }

    private static unsafe int TryReadSelectedCardId(AddonGSInfoCardList* addon)
    {
        if (addon->SelectedCardName != null)
        {
            var name = GUINodeUtils.GetNodeText(&addon->SelectedCardName->AtkResNode)?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                // Unowned: name is "???" and SelectedCardNumber holds a display index (e.g. "Ex. 10") — let grid lookup resolve it.
                if (name.Contains('?'))
                {
                    return -1;
                }

                var cardFromName = TriadCardDB.Get().Find(name);
                if (cardFromName != null)
                {
                    return cardFromName.Id;
                }
            }
        }

        var fromNumber = TryParseCardIdFromText(addon->SelectedCardNumber != null ? &addon->SelectedCardNumber->AtkResNode : null);
        if (fromNumber > 0)
        {
            return fromNumber;
        }

        if (addon->CardIconId >= 88000)
        {
            var fromIcon = addon->CardIconId - 88000;
            if (IsKnownCardId(fromIcon))
            {
                return fromIcon;
            }
        }

        var descNode = addon->SelectedCardDescription;
        if (descNode != null)
        {
            foreach (var node in GUINodeUtils.GetAllChildNodes(&descNode->AtkResNode) ?? [])
            {
                if (node == null)
                {
                    continue;
                }

                fromNumber = TryParseCardIdFromText(GUINodeUtils.GetNodeText(node));
                if (fromNumber > 0)
                {
                    return fromNumber;
                }
            }
        }

        return -1;
    }

    private static int TryParseCardIdFromText(FFXIVClientStructs.FFXIV.Component.GUI.AtkResNode* node) =>
        TryParseCardIdFromText(GUINodeUtils.GetNodeText(node));

    private static int TryParseCardIdFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        foreach (var part in text.Split([' ', '.', '#', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var cardId) && cardId > 0 && IsKnownCardId(cardId))
            {
                return cardId;
            }
        }

        return -1;
    }

    private static bool IsKnownCardId(int cardId) =>
        TriadCardDB.Get().FindById(cardId) != null || GameCardDB.Get().FindById(cardId) != null;

    public unsafe bool SetPageAndGridView(int pageIndex, int cellIndex, int cardId = 0)
    {
        if (pageIndex < 0 || pageIndex >= GameCardDB.MaxGridPages || cellIndex < 0 || cellIndex >= GameCardDB.MaxGridCells)
        {
            return false;
        }

        var addonPtr = ResolveAddonPtr();
        OnAddonShown(addonPtr);

        if (addonPtr == nint.Zero || cachedAddonAgentPtr == nint.Zero)
        {
            return false;
        }

        pendingNavPage = pageIndex;
        pendingNavCell = cellIndex;
        pendingNavCardId = cardId;
        pendingNavAttempts = 90;

        cachedState.pageIndex = (byte)pageIndex;
        cachedState.cardIndex = (byte)cellIndex;

        OnUIStateChanged?.Invoke(cachedState);

        TickPendingCardNavigation(addonPtr);
        return true;
    }

    private void ClearPendingNavigation()
    {
        pendingNavPage = -1;
        pendingNavCell = -1;
        pendingNavCardId = 0;
        pendingNavAttempts = 0;
    }

    private unsafe void TickPendingCardNavigation(nint addonPtr)
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

        if (!TriadAutomater.TryClickGoldSaucerCardCell(addonPtr, pendingNavCell))
        {
            return;
        }

        if (IsPendingNavigationComplete(addon))
        {
            ClearPendingNavigation();
        }
    }

    private unsafe bool IsPendingNavigationComplete(AddonGSInfoCardList* addon)
    {
        if (addon->SelectedPage != pendingNavPage || addon->SelectedCardIndex != pendingNavCell)
        {
            return false;
        }

        if (pendingNavCardId <= 0)
        {
            return true;
        }

        if (addon->CardIconId >= 88000 && addon->CardIconId - 88000 == pendingNavCardId)
        {
            return AddonStatsMatchCard(addon, pendingNavCardId);
        }

        if (!CardNumberNodeMatches(addon, pendingNavCardId))
        {
            return false;
        }

        return AddonStatsMatchCard(addon, pendingNavCardId);
    }

    private static unsafe bool AddonStatsMatchCard(AddonGSInfoCardList* addon, int cardId)
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

    private static unsafe bool CardNumberNodeMatches(AddonGSInfoCardList* addon, int cardId)
    {
        var node = addon->SelectedCardNumber;
        if (node == null)
        {
            return false;
        }

        var text = GUINodeUtils.GetNodeText(&node->AtkResNode);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains(cardId.ToString(), StringComparison.Ordinal);
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

    public static unsafe nint LoadFailsafeAgent()
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

            if (HasErrors)
            {
                Svc.Log.Error("CardList reader error: " + newStatus);
            }

            if (wasVisible != IsVisible)
            {
                OnVisibilityChanged?.Invoke(IsVisible);
            }
        }
    }
}

public class UIStateTriadCardList
{
    public byte cardIndex;
    public int selectedCardId = -1;
    public Vector2 descriptionPos;
    public Vector2 descriptionSize;
    public byte filterMode;
    public int iconId;
    public byte numD;
    public byte numL;
    public byte numR;

    public byte numU;

    public byte pageIndex;
    public byte rarity;
    public Vector2 screenPos;
    public Vector2 screenSize;
    public byte type;

    public int ResolveCardId(GameUIParser ctx)
    {
        if (selectedCardId > 0)
        {
            return selectedCardId;
        }

        if (iconId >= 88000)
        {
            var fromIcon = iconId - 88000;
            if (TriadCardDB.Get().FindById(fromIcon) != null)
            {
                return fromIcon;
            }
        }

        return ToTriadCard(ctx)?.Id ?? -1;
    }

    public TriadCard? ToTriadCard(GameUIParser ctx)
    {
        if (selectedCardId > 0)
        {
            var card = ctx.cards.FindById(selectedCardId);
            if (card != null)
            {
                return card;
            }
        }

        if (iconId >= 88000)
        {
            var iconCard = ctx.cards.FindById(iconId - 88000);
            if (iconCard != null)
            {
                return iconCard;
            }
        }

        var gridMatch = ctx.ParseCardByGridLocation(pageIndex, cardIndex, filterMode, false);

        TriadCard? iconMatch = null;
        if (iconId >= 88000)
        {
            iconMatch = ctx.cards.FindById(iconId - 88000);
        }

        if (gridMatch != null)
        {
            // Grid selection is authoritative; icon and side stats can lag behind selection changes.
            if (iconMatch != null && iconMatch.Id != gridMatch.Id)
            {
                return gridMatch;
            }

            var statsMatch = ctx.ParseCard(numU, numL, numD, numR, (ETriadCardType)type, (ETriadCardRarity)rarity, false);
            if (statsMatch != null && statsMatch.SameNumberId < 0 && statsMatch.Id != gridMatch.Id)
            {
                return gridMatch;
            }

            return gridMatch;
        }

        if (iconMatch != null)
        {
            return iconMatch;
        }

        if (iconId == 0 && numU == 0 && numL == 0 && numD == 0 && numR == 0)
        {
            return null;
        }

        var matchOb = ctx.ParseCard(numU, numL, numD, numR, (ETriadCardType)type, (ETriadCardRarity)rarity, false);
        if (matchOb != null && matchOb.SameNumberId < 0)
        {
            return matchOb;
        }

        return matchOb;
    }
}
