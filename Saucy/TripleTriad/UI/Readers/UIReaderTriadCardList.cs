using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using System;
namespace Saucy.TripleTriad.UI;

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
    private bool inAddonUpdate;

    public UIStateTriadCardList cachedState = new();
    private int lastLoggedSelectedPage = int.MinValue;
    private string lastLoggedNavAction = "";
    private int lastNotifiedCardId = -1;
    public Action<UIStateTriadCardList>? OnUIStateChanged;
    public Action<bool>? OnVisibilityChanged;
    private int pendingNavAttempts;
    private int pendingNavCardId;
    private int pendingNavCell = -1;
    private int pendingNavClickCooldown;
    private int pendingNavGraceFrames;
    private int pendingNavPage = -1;
    private bool pendingNavSawTargetPage;
    private int pendingNavSourceCardId = -1;

    public Status status = Status.AddonNotFound;
    public bool IsVisible => status is not Status.AddonNotFound and not Status.AddonNotVisible;
    public bool IsPendingCardNavigation => pendingNavPage >= 0;

    public string GetAddonName() => "GSInfoCardList";

    public void OnAddonLost()
    {
        cachedAddonAgentPtr = nint.Zero;
        if (pendingNavPage >= 0)
        {
            ClearPendingNavigation("addon lost");
        }
        else
        {
            ClearPendingNavigation();
        }
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
        if (inAddonUpdate)
        {
            return;
        }

        inAddonUpdate = true;
        try
        {
            OnAddonUpdateCore(addonPtr);
        }
        finally
        {
            inAddonUpdate = false;
        }
    }

    private void OnAddonUpdateCore(nint addonPtr)
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
        var newCardIndex = (byte)SanitizeCellIndex(addon->SelectedCardIndex);
        var newFilterMode = ToCollectionFilter(CardListFilterMode.All);

        AgentGoldSaucer* agent = null;
        if (cachedAddonAgentPtr != nint.Zero)
        {
            agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
            newFilterMode = ToCollectionFilter(agent->CardListFilterMode);
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
                ClearPendingNavigation($"done {FormatCard(pendingNavCardId)} page={newPageIndex} cell={newCardIndex} {FormatAddonLabels(addon)}");
            }
            else if (newPageIndex == pendingNavPage &&
                     (displayCardId == pendingNavCardId ||
                      TriadCardListSelectionReader.IconMatchesCard(addon->CardIconId, pendingNavCardId)))
            {
                ClearPendingNavigation($"done via selection {FormatCard(pendingNavCardId)} page={newPageIndex} cell={newCardIndex} {FormatAddonLabels(addon)}");
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
        cachedState.isDeckEditMode = IsDeckEditScreenOpen();
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
            Log($"GSInfoCardList or agent not found for {FormatCard(cardId)} page={pageIndex} cell={cellIndex}");
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
        var filterMode = ToCollectionFilter(agent->CardListFilterMode);
        var displayCardId = TriadCardListSelectionReader.TryParseCardIdFromDisplayLabel(addon);
        pendingNavSourceCardId = TriadCardListSelectionReader.ReadSelectedCardId(addon, filterMode, agent, displayCardId);

        pendingNavCardId = cardId;
        pendingNavAttempts = 180;
        pendingNavClickCooldown = 0;
        pendingNavGraceFrames = 5;

        if (pageIndex < 0 || pageIndex >= GameCardDB.MaxGridPages || cellIndex < 0 || cellIndex >= GameCardDB.MaxGridCells)
        {
            Log($"invalid grid page={pageIndex} cell={cellIndex} for {FormatCard(cardId)} (max page {GameCardDB.MaxGridPages}, cells {GameCardDB.MaxGridCells})");
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
        pendingNavSawTargetPage = false;
        lastLoggedNavAction = "";
        lastLoggedSelectedPage = int.MinValue;
        agent->EditDeckSelectedPage = pageIndex;
        addon->RequestedPage = pageIndex;
        Log(
            $"nav start {FormatCard(cardId)} -> page {pageIndex} cell {cellIndex} filter={filterMode} " +
            $"from {FormatCard(pendingNavSourceCardId)} addon page={addon->SelectedPage} cell={SanitizeCellIndex(addon->SelectedCardIndex)} " +
            $"requested={addon->RequestedPage} agentPage={agent->EditDeckSelectedPage} deckEdit={IsDeckEditScreenOpen()}");

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

    private void ClearPendingNavigation(string? reason = null)
    {
        if (!string.IsNullOrEmpty(reason) && (pendingNavPage >= 0 || pendingNavCardId > 0))
        {
            Log(reason);
        }

        pendingNavPage = -1;
        pendingNavCell = -1;
        pendingNavCardId = 0;
        pendingNavAttempts = 0;
        pendingNavClickCooldown = 0;
        pendingNavGraceFrames = 0;
        pendingNavSawTargetPage = false;
        pendingNavSourceCardId = -1;
        lastLoggedNavAction = "";
        lastLoggedSelectedPage = int.MinValue;
    }

    private void TickPendingCardNavigation(nint addonPtr)
    {
        if (pendingNavPage < 0 || addonPtr == nint.Zero)
        {
            return;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        if (--pendingNavAttempts <= 0)
        {
            ClearPendingNavigation(
                $"timeout wanted {FormatCard(pendingNavCardId)} page={pendingNavPage} cell={pendingNavCell}; " +
                $"addon page={addon->SelectedPage} requested={addon->RequestedPage} cell={SanitizeCellIndex(addon->SelectedCardIndex)} " +
                $"{FormatAddonLabels(addon)} totalPages={addon->TotalPages}");
            return;
        }

        var agentPage = -1;
        if (cachedAddonAgentPtr != nint.Zero)
        {
            var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
            agent->EditDeckSelectedPage = pendingNavPage;
            agentPage = agent->EditDeckSelectedPage;
            if (cachedState.isDeckEditMode)
            {
                agent->EditDeckSelectedCardIndex = pendingNavCell;
            }
        }

        addon->RequestedPage = pendingNavPage;

        if (addon->SelectedPage != pendingNavPage)
        {
            pendingNavSawTargetPage = false;
            if (lastLoggedNavAction != "wait-page" || lastLoggedSelectedPage != addon->SelectedPage)
            {
                Log($"waiting for page {pendingNavPage}, still on {addon->SelectedPage} (requested={addon->RequestedPage} agentPage={agentPage})");
                lastLoggedNavAction = "wait-page";
                lastLoggedSelectedPage = addon->SelectedPage;
            }

            return;
        }

        if (!pendingNavSawTargetPage)
        {
            pendingNavSawTargetPage = true;
            pendingNavGraceFrames = 2;
            Log($"page {pendingNavPage} matched (cell {SanitizeCellIndex(addon->SelectedCardIndex)}), waiting for grid before clicking cell {pendingNavCell} for {FormatCard(pendingNavCardId)}");
            lastLoggedNavAction = "wait-grid";
            return;
        }

        if (pendingNavGraceFrames > 0)
        {
            return;
        }

        if (pendingNavClickCooldown > 0)
        {
            pendingNavClickCooldown--;
            return;
        }

        var currentCell = SanitizeCellIndex(addon->SelectedCardIndex);
        addon->SelectedCardIndex = pendingNavCell;
        if (lastLoggedNavAction != "click")
        {
            Log($"page {pendingNavPage} ready (cell {currentCell}), clicking cell {pendingNavCell} for {FormatCard(pendingNavCardId)} {FormatAddonLabels(addon)} totalPages={addon->TotalPages}");
            lastLoggedNavAction = "click";
        }

        if (!TryClickCardCell(addonPtr, pendingNavCell))
        {
            if (lastLoggedNavAction != "click-fail")
            {
                Log($"cell {pendingNavCell} click failed on page {pendingNavPage}");
                lastLoggedNavAction = "click-fail";
            }

            return;
        }

        pendingNavClickCooldown = 6;

        if (IsPendingNavigationComplete(addon))
        {
            ClearPendingNavigation($"done after click {FormatCard(pendingNavCardId)} page={addon->SelectedPage} cell={SanitizeCellIndex(addon->SelectedCardIndex)} {FormatAddonLabels(addon)}");
        }
    }

    private bool IsPendingNavigationComplete(AddonGSInfoCardList* addon)
    {
        if (addon->SelectedPage != pendingNavPage || SanitizeCellIndex(addon->SelectedCardIndex) != pendingNavCell)
        {
            return false;
        }

        if (pendingNavCardId <= 0)
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

    private static byte ToCollectionFilter(CardListFilterMode mode) =>
        mode switch
        {
            CardListFilterMode.OwnedOnly => (byte)GameCardCollectionFilter.OnlyOwned,
            CardListFilterMode.MissingOnly => (byte)GameCardCollectionFilter.OnlyMissing,
            var _ => (byte)GameCardCollectionFilter.All
        };

    private static bool IsDeckEditScreenOpen()
    {
        for (var idx = 0; idx < 8; idx++)
        {
            if (Svc.GameGui.GetAddonByName("GSInfoCardDeck", idx) != nint.Zero)
            {
                return true;
            }
        }

        return false;
    }

    private static int SanitizeCellIndex(int cellIndex) =>
        cellIndex is >= 0 and < 30 ? cellIndex : 0;

    private static bool TryClickCardCell(nint addonPtr, int cellIndex)
    {
        if (addonPtr == nint.Zero || cellIndex < 0 || cellIndex >= 30)
        {
            return false;
        }

        var addon = (AddonGSInfoCardList*)addonPtr;
        return AddonButton.TryClick(&addon->AtkUnitBase, addon->CardButtons[cellIndex], false);
    }

    private static void Log(string message) =>
        Svc.Log.Information($"[CardSearch] {message}");

    private static string FormatAddonLabels(AddonGSInfoCardList* addon)
    {
        var selected = addon->SelectedCardNumber != null
            ? GUINodeUtils.GetNodeText(&addon->SelectedCardNumber->AtkResNode)
            : null;
        var previewed = addon->PreviewedCardNumber != null
            ? GUINodeUtils.GetNodeText(&addon->PreviewedCardNumber->AtkResNode)
            : null;
        return $"display='{selected}' preview='{previewed}' icon={addon->CardIconId}";
    }

    private static string FormatCard(int cardId)
    {
        if (cardId <= 0)
        {
            return cardId.ToString();
        }

        var card = TriadCardDB.Get().FindById(cardId);
        return card == null
            ? $"#{cardId}"
            : $"{card.Name} #{cardId} {CardUtils.GetOrderDesc(card)}";
    }
}
