using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
namespace Saucy.TripleTriad;

public unsafe class TriadCardSearchWindow : Window, IDisposable
{
    private const float WindowContentWidth = 270.0f;
    private readonly List<int> listCardDisplayNos = [];

    private readonly List<Tuple<TriadCard, GameCardInfo>> listCards = [];
    private readonly List<Tuple<TriadCard, int>> listNpcReward = [];
    private readonly List<Tuple<TriadNpc, GameNpcInfo>> listNpcs = [];
    private readonly TriadNpcStatsWindow statsWindow;

    private readonly UIReaderTriadCardList uiReaderCardList;

    private int activeTab;
    private bool deckEditMode;
    private int filterMode = -1;
    private bool hideNpcBeatenOnce;
    private bool hideNpcCompleted;
    private DateTime lastOwnershipRefreshUtc = DateTime.MinValue;

    private bool npcFilterDataStale = true;
    private int numNotOwnedRewards;

    private int pluginNavTargetCardId = -1;
    private int pluginNavTargetNpcId = -1;
    private bool sawGameDataReady;
    private int scrollNpcListToSelectionFrames;
    private ImGuiTextFilterPtr searchFilterCard;
    private ImGuiTextFilterPtr searchFilterNpc;
    private int selectedCardIdx;
    private int selectedNpcIdx;
    private bool showNotOwnedOnly;

    private bool showNpcMatchesOnly;

    public TriadCardSearchWindow(UIReaderTriadCardList uiReaderCardList, TriadNpcStatsWindow statsWindow) : base("Card Search")
    {
        this.uiReaderCardList = uiReaderCardList;
        this.statsWindow = statsWindow;

        var searchFilterCardPtr = ImGuiNative.ImGuiTextFilter(null);
        searchFilterCard = new(searchFilterCardPtr);

        var searchFilterNpcPtr = ImGuiNative.ImGuiTextFilter(null);
        searchFilterNpc = new(searchFilterNpcPtr);

        uiReaderCardList.OnVisibilityChanged += _ => UpdateWindowData();
        uiReaderCardList.OnUIStateChanged += OnUIStateChanged;

        var collection = C.TriadCollection;
        showNpcMatchesOnly = collection.CheckCardNpcMatchOnly;
        showNotOwnedOnly = collection.CheckCardNotOwnedOnly;
        hideNpcBeatenOnce = collection.CheckNpcHideBeaten;
        hideNpcCompleted = collection.CheckNpcHideCompleted;

        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(WindowContentWidth + 20, 0), MaximumSize = new(WindowContentWidth + 20, 1000)
        };

        ForceMainWindow = true;
        RespectCloseHotkey = false;
        Flags = ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;
    }

    private static bool IsGameDataReady => dataLoader.IsDataReady;

    public void Dispose()
    {
        ImGuiNative.Destroy(searchFilterCard.Handle);
        ImGuiNative.Destroy(searchFilterNpc.Handle);
    }

    internal void SyncVisibility() => UpdateWindowData();

    internal void FocusNpcById(int npcId)
    {
        if (npcId <= 0)
        {
            return;
        }

        pluginNavTargetNpcId = npcId;
        activeTab = 1;
    }

    internal void OnGameDataReady()
    {
        if (sawGameDataReady)
        {
            return;
        }

        sawGameDataReady = true;
        filterMode = -1;
        listCards.Clear();
        listNpcs.Clear();
        selectedCardIdx = -1;
        selectedNpcIdx = -1;
        lastOwnershipRefreshUtc = DateTime.MinValue;
        TriadNpcQuestUi.InvalidateCache();
    }

    private void UpdateWindowData()
    {
        if (!IsGameDataReady)
        {
            return;
        }

        var wasOpen = IsOpen;
        IsOpen = uiReaderCardList.IsVisible;

        if (IsOpen && !wasOpen)
        {
            if (TriadMemoryReads.IsAvailable)
            {
                GameCardDB.Get().Refresh();
            }

            filterMode = -1;
            searchFilterCard.Clear();
            GameNpcDB.Get().RefreshCompleted();
            if (hideNpcBeatenOnce)
            {
                GameNpcDB.Get().RefreshBeatenOnce();
            }
            searchFilterNpc.Clear();
            npcFilterDataStale = true;

            OnUIStateChanged(uiReaderCardList.cachedState);
            TryPopulateNpcList();
        }
        else if (!IsOpen && wasOpen)
        {
            TriadNpcQuestUi.InvalidateCache();
        }
    }

    private void TryPopulateNpcList()
    {
        if (!IsOpen || !IsGameDataReady)
        {
            return;
        }

        if (GameNpcDB.Get().mapNpcs.Count == 0)
        {
            return;
        }

        if (listNpcs.Count > 0)
        {
            return;
        }

        GenerateNpcList();
    }

    private void RefreshNpcProgress()
    {
        if (!IsGameDataReady || GameNpcDB.Get().mapNpcs.Count == 0)
        {
            return;
        }

        if (TriadMemoryReads.IsAvailable)
        {
            GameCardDB.Get().Refresh();
        }

        if (hideNpcCompleted)
        {
            GameNpcDB.Get().RefreshCompleted();
        }

        if (hideNpcBeatenOnce)
        {
            GameNpcDB.Get().RefreshBeatenOnce();
        }
    }

    public void OnUIStateChanged(UIStateTriadCardList uiState)
    {
        RebuildCardList(uiState);
        SyncSelectionFromGame(uiState);
    }

    private void RebuildCardList(UIStateTriadCardList uiState)
    {
        var needsOwnership = showNotOwnedOnly || uiState.filterMode != 0;
        var modeKey = uiState.isDeckEditMode ? -2 : uiState.filterMode;
        if (filterMode == modeKey && deckEditMode == uiState.isDeckEditMode && listCards.Count > 0 &&
            (!needsOwnership || GameCardDB.Get().ownedCardIds.Count > 0))
        {
            return;
        }

        filterMode = modeKey;
        deckEditMode = uiState.isDeckEditMode;
        var preserveCardId = selectedCardIdx >= 0 && selectedCardIdx < listCards.Count
            ? listCards[selectedCardIdx].Item1.Id
            : -1;
        listCards.Clear();
        listCardDisplayNos.Clear();

        if (!IsGameDataReady)
        {
            return;
        }

        if (TriadMemoryReads.IsAvailable && (needsOwnership || uiState.isDeckEditMode))
        {
            GameCardDB.Get().Refresh();
        }

        if (uiState.isDeckEditMode)
        {
            foreach (var entry in GameCardDB.Get().GetDeckEditCardEntries())
            {
                listCards.Add(new(entry.Card, entry.Info));
                listCardDisplayNos.Add(entry.DisplayNo);
            }
        }
        else
        {
            var cardDB = TriadCardDB.Get();
            var cardInfoDB = GameCardDB.Get();

            var includeOwned = filterMode != 2;
            var includeMissing = filterMode != 1;

            foreach (var card in cardDB.cards)
            {
                if (card == null || !card.IsValid())
                {
                    continue;
                }

                var cardInfo = cardInfoDB.FindById(card.Id);
                if (cardInfo == null)
                {
                    continue;
                }

                var isOwned = IsCardOwned(card.Id);
                if ((includeOwned && isOwned) || (includeMissing && !isOwned))
                {
                    listCards.Add(new(card, cardInfo));
                    listCardDisplayNos.Add(card.SortOrder);
                }
            }

            if (listCards.Count > 1)
            {
                listCards.Sort((a, b) => a.Item1.SortOrder.CompareTo(b.Item1.SortOrder));
                listCardDisplayNos.Clear();
                foreach (var entry in listCards)
                {
                    listCardDisplayNos.Add(entry.Item1.SortOrder);
                }
            }
        }

        if (preserveCardId >= 0)
        {
            selectedCardIdx = listCards.FindIndex(x => x.Item1.Id == preserveCardId);
        }
    }

    private void SyncSelectionFromGame(UIStateTriadCardList uiState)
    {
        if (!IsGameDataReady || listCards.Count == 0)
        {
            return;
        }

        var card = uiReaderCardList.ResolveSelectedCard();
        if (pluginNavTargetCardId >= 0)
        {
            if (card == null)
            {
                return;
            }

            if (card.Id == pluginNavTargetCardId)
            {
                pluginNavTargetCardId = -1;
            }
            else if (!uiReaderCardList.IsPendingCardNavigation)
            {
                pluginNavTargetCardId = -1;
            }
            else
            {
                return;
            }
        }

        if (card == null)
        {
            selectedCardIdx = -1;
            return;
        }

        var listIdx = listCards.FindIndex(x => x.Item1.Id == card.Id);
        if (listIdx >= 0)
        {
            selectedCardIdx = listIdx;
        }
    }

    public override void PreDraw()
    {
        if (!IsGameDataReady || !uiReaderCardList.IsVisible)
        {
            return;
        }

        deckEditMode = uiReaderCardList.cachedState.isDeckEditMode;
        WindowName = deckEditMode ? "Deck Cards" : "Card Search";
        if (deckEditMode)
        {
            activeTab = 0;
        }

        Position = new Vector2(
            uiReaderCardList.cachedState.screenPos.X + uiReaderCardList.cachedState.screenSize.X + 10,
            uiReaderCardList.cachedState.screenPos.Y);
    }

    public override void Draw()
    {
        if (!IsGameDataReady)
        {
            ImGui.TextDisabled("Loading card data…");
            return;
        }

        OnGameDataReady();
        ApplyPendingNpcNavigation();

        if (!deckEditMode)
        {
            DrawTabHeader();
            ImGui.Separator();
        }

        switch (activeTab)
        {
            case 0: DrawCardsTab(); break;
            case 1: DrawNpcTab(); break;
        }
    }

    private void DrawTabHeader()
    {
        if (deckEditMode)
        {
            return;
        }

        if (ImGui.Selectable("Cards", activeTab == 0, ImGuiSelectableFlags.None, ImGui.CalcTextSize("Cards") + new Vector2(12, 0)))
        {
            activeTab = 0;
        }

        ImGui.SameLine();

        if (ImGui.Selectable("NPC", activeTab == 1, ImGuiSelectableFlags.None, ImGui.CalcTextSize("NPC") + new Vector2(12, 0)))
        {
            activeTab = 1;
        }
    }

    private static bool IsCardOwned(int cardId)
    {
        var db = GameCardDB.Get();
        if (db.ownedCardIds.Contains(cardId))
        {
            return true;
        }

        if (!TriadMemoryReads.IsAvailable)
        {
            return false;
        }

        return TriadMemoryReads.TryIsCardOwned(cardId);
    }

    private void RefreshOwnershipIfNeeded(bool force = false)
    {
        if (!TriadMemoryReads.IsAvailable)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && (now - lastOwnershipRefreshUtc) < TimeSpan.FromSeconds(1))
        {
            return;
        }

        lastOwnershipRefreshUtc = now;
        GameCardDB.Get().Refresh();
    }

    private string FormatCardListLabel(int idx, TriadCard cardOb)
    {
        if (deckEditMode && idx >= 0 && idx < listCardDisplayNos.Count)
        {
            return CardUtils.FormatDeckEditListLabel(listCardDisplayNos[idx], cardOb);
        }

        return $"[{CardUtils.GetOrderDesc(cardOb)}] {CardUtils.GetRarityDesc(cardOb)} {CardUtils.GetUIDesc(cardOb)}";
    }

    private void DrawCardsTab()
    {
        var showNotOwnedCheckbox = !deckEditMode && filterMode == 0;

        if (showNotOwnedCheckbox && showNotOwnedOnly)
        {
            RefreshOwnershipIfNeeded();
        }

        RebuildCardList(uiReaderCardList.cachedState);
        searchFilterCard.Draw("##cardSearchFilter", GetContentWidth());

        if (ImGui.BeginListBox("##cards", GetListBoxSize(10)))
        {
            for (var idx = 0; idx < listCards.Count; idx++)
            {
                (var cardOb, var cardInfo) = listCards[idx];
                if ((showNpcMatchesOnly && cardInfo.RewardNpcs.Count <= 0) ||
                    (showNotOwnedCheckbox && showNotOwnedOnly && IsCardOwned(cardOb.Id)))
                {
                    continue;
                }

                var itemDesc = FormatCardListLabel(idx, cardOb);
                if (searchFilterCard.PassFilterBool(itemDesc))
                {
                    var isSelected = selectedCardIdx == idx;
                    if (ImGui.Selectable(itemDesc, isSelected))
                    {
                        selectedCardIdx = idx;
                        OnCardSelectionChanged();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
            }
            ImGui.EndListBox();
        }

        if (!deckEditMode)
        {
            ImGui.Spacing();
            if (ImGui.Checkbox("NPC reward cards only", ref showNpcMatchesOnly))
            {
                C.TriadCollection.CheckCardNpcMatchOnly = showNpcMatchesOnly;
                C.Save();
            }

            if (showNotOwnedCheckbox)
            {
                if (ImGui.Checkbox("Unowned only", ref showNotOwnedOnly))
                {
                    if (showNotOwnedOnly)
                    {
                        RefreshOwnershipIfNeeded(force: true);
                    }

                    C.TriadCollection.CheckCardNotOwnedOnly = showNotOwnedOnly;
                    C.Save();
                }
            }
            else if (filterMode is >= 0 and not 0)
            {
                ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled), "(Collection filtering is active)");
            }
        }
    }

    private void ApplyPendingNpcNavigation()
    {
        if (pluginNavTargetNpcId <= 0)
        {
            return;
        }

        TryPopulateNpcList();
        var targetId = pluginNavTargetNpcId;
        pluginNavTargetNpcId = -1;

        var idx = listNpcs.FindIndex(x => x.Item1.Id == targetId);
        if (idx < 0)
        {
            return;
        }

        activeTab = 1;
        selectedNpcIdx = idx;
        searchFilterNpc.Clear();
        GenerateNpcRewardList();
        TriadRun.OnNpcSelected(listNpcs[idx].Item1);
        scrollNpcListToSelectionFrames = 2;
    }

    private void DrawNpcTab()
    {
        TryPopulateNpcList();

        if (npcFilterDataStale && (hideNpcBeatenOnce || hideNpcCompleted))
        {
            RefreshNpcProgress();
            npcFilterDataStale = false;
        }

        searchFilterNpc.Draw("##npcSearchFilter", GetContentWidth());

        if (!IsGameDataReady)
        {
            ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled), "Loading NPC data…");
            return;
        }

        if (listNpcs.Count == 0)
        {
            ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled),
                GameNpcDB.Get().mapNpcs.Count == 0 ? "No NPC data loaded." : "No NPCs available.");
            return;
        }

        var visibleCount = 0;
        if (ImGui.BeginListBox("##npcs", GetListBoxSize(10)))
        {
            for (var idx = 0; idx < listNpcs.Count; idx++)
            {
                (var npcOb, var npcInfo) = listNpcs[idx];
                var bypassHideFilters = selectedNpcIdx == idx;
                if (!bypassHideFilters &&
                    ((hideNpcBeatenOnce && (npcInfo.IsBeatenOnce || npcInfo.IsExcludedFromAchievementTracker)) ||
                     (hideNpcCompleted && npcInfo.IsCompleted)))
                {
                    continue;
                }

                var itemDesc = npcOb.Name;
                if (string.IsNullOrWhiteSpace(itemDesc))
                {
                    itemDesc = $"NPC #{npcOb.Id}";
                }

                if (searchFilterNpc.PassFilterBool(itemDesc))
                {
                    visibleCount++;
                    var isSelected = selectedNpcIdx == idx;
                    if (ImGui.Selectable(itemDesc, isSelected))
                    {
                        selectedNpcIdx = idx;
                        GenerateNpcRewardList();
                        TriadRun.OnNpcSelected(npcOb);
                    }

                    if (isSelected)
                    {
                        if (scrollNpcListToSelectionFrames > 0)
                        {
                            ImGui.SetScrollHereY(0.35f);
                            scrollNpcListToSelectionFrames--;
                        }
                        else
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                }
            }
            ImGui.EndListBox();
        }

        if (visibleCount == 0)
        {
            ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled),
                "No NPCs match the current filters.");
        }

        ImGui.Spacing();
        if (ImGui.Checkbox("Hide beaten NPCs", ref hideNpcBeatenOnce))
        {
            npcFilterDataStale = true;
            RefreshNpcProgress();
            npcFilterDataStale = false;
            C.TriadCollection.CheckNpcHideBeaten = hideNpcBeatenOnce;
            C.Save();
        }

        if (ImGui.Checkbox("Hide completed NPCs", ref hideNpcCompleted))
        {
            npcFilterDataStale = true;
            RefreshNpcProgress();
            npcFilterDataStale = false;
            C.TriadCollection.CheckNpcHideCompleted = hideNpcCompleted;
            C.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNpcDetails();
    }

    private void DrawNpcDetails()
    {
        var npcData = (selectedNpcIdx < 0 || selectedNpcIdx >= listNpcs.Count) ? null : listNpcs[selectedNpcIdx];
        if (npcData?.Item2 is not { } npcInfo)
        {
            return;
        }

        if (npcInfo.Location != null)
        {
            TriadNpcMapUi.DrawMapLocationRow(npcInfo.Location, "Show on map", npcData.Item1);
        }

        TriadNpcQuestUi.DrawUnlockQuestIconRow(npcInfo);

        ImGui.Spacing();
        var hasAvgRewards = StatTracker.GetAverageRewardPerMatchDesc(C.TriadCollection, npcInfo, out var avgRewardPerMatch);
        var settingsDB = PlayerSettingsDB.Get();
        DrawIconTextRow(FontAwesomeIcon.ChartLine, null, () => statsWindow.SetupAndOpen(npcData.Item1), () =>
        {
            ImGui.Text("NPC stats" + (hasAvgRewards ? "," : ""));
            if (hasAvgRewards)
            {
                ImGui.SameLine();
                ImGui.Text("MGP per match:");
                ImGui.SameLine();
                ImGui.Text(avgRewardPerMatch.ToString("0.#"));
            }
        });

        TriadCollectionPremadeDeckUi.DrawForNpc(npcData.Item1);

        ImGui.Spacing();
        ImGui.Text($"Unowned rewards: {numNotOwnedRewards}");
        if (listNpcReward.Count > 0 &&
            ImGui.BeginListBox("##cardReward", GetListBoxSize(4.5f)))
        {
            for (var idx = 0; idx < listNpcReward.Count; idx++)
            {
                (var cardOb, var cardListIdx) = listNpcReward[idx];
                var isCardOwned = settingsDB.ownedCards.Contains(cardOb);

                var itemDesc = $"{CardUtils.GetOrderDesc(cardOb)} {CardUtils.GetUIDesc(cardOb)}";
                var isSelected = selectedCardIdx == cardListIdx;

                if (isCardOwned)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xffa8a8a8);
                }

                if (ImGui.Selectable($"{CardUtils.GetRarityDesc(cardOb)}  {itemDesc}", isSelected))
                {
                    selectedCardIdx = cardListIdx;
                    OnCardSelectionChanged();
                }

                if (isCardOwned)
                {
                    ImGui.PopStyleColor(1);
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndListBox();
        }
        else
        {
            ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled), "Not available");
        }
    }

    private static void DrawIconTextRow(FontAwesomeIcon icon, string? tooltip, Action onIconClick, Action drawText)
    {
        ImGui.AlignTextToFramePadding();
        var rowY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(rowY - ImGui.GetStyle().FramePadding.Y);
        if (ImGuiComponents.IconButton(icon))
        {
            onIconClick();
        }
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
        ImGui.SetCursorPosY(rowY);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        drawText();
    }

    private static float GetContentWidth() =>
        MathF.Max(1f, ImGui.GetContentRegionAvail().X);

    private static Vector2 GetListBoxSize(float visibleLines) =>
        new(GetContentWidth(), ImGui.GetTextLineHeightWithSpacing() * visibleLines);

    private GameCardCollectionFilter ResolveNavigationCollectionFilter()
    {
        var collectionFilter = uiReaderCardList.cachedState.GetActiveCollectionFilter();
        if (showNotOwnedOnly && collectionFilter == GameCardCollectionFilter.All)
        {
            return GameCardCollectionFilter.OnlyMissing;
        }

        return collectionFilter;
    }

    private void OnCardSelectionChanged()
    {
        if (selectedCardIdx < 0 || selectedCardIdx >= listCards.Count)
        {
            return;
        }

        (var cardOb, var cardInfo) = listCards[selectedCardIdx];
        if (cardOb != null && cardInfo != null)
        {
            pluginNavTargetCardId = cardOb.Id;

            var collectionFilter = ResolveNavigationCollectionFilter();
            var collectionPos = cardInfo.Collection[(int)collectionFilter];
            uiReaderCardList.SetPageAndGridView(collectionPos.PageIndex, collectionPos.CellIndex, cardOb.Id);
        }
    }

    private void GenerateNpcList()
    {
        listNpcs.Clear();

        var npcDB = TriadNpcDB.Get();
        var npcInfoDB = GameNpcDB.Get();

        foreach (var kvp in npcInfoDB.mapNpcs)
        {
            var npc = npcDB.FindByID(kvp.Key);
            if (npc != null)
            {
                listNpcs.Add(new(npc, kvp.Value));
            }
        }

        if (listNpcs.Count > 1)
        {
            listNpcs.Sort((a, b) => a.Item1.Name.CompareTo(b.Item1.Name));
        }

        selectedNpcIdx = -1;
    }

    private void GenerateNpcRewardList()
    {
        listNpcReward.Clear();

        numNotOwnedRewards = 0;
        var settingsDB = PlayerSettingsDB.Get();

        var npcData = (selectedNpcIdx < 0 || selectedNpcIdx >= listNpcs.Count) ? null : listNpcs[selectedNpcIdx];
        if (npcData != null && npcData.Item2 != null)
        {
            foreach (var cardId in npcData.Item2.rewardCards)
            {
                var listIdx = listCards.FindIndex(x => x.Item1.Id == cardId);
                if (listIdx >= 0)
                {
                    var cardOb = listCards[listIdx].Item1;
                    if (cardOb != null)
                    {
                        listNpcReward.Add(new(cardOb, listIdx));
                        numNotOwnedRewards += settingsDB.ownedCards.Contains(cardOb) ? 0 : 1;
                    }
                }
            }
        }

        if (listNpcReward.Count > 1)
        {
            listNpcReward.Sort((a, b) => a.Item1.Name.CompareTo(b.Item1.Name));
        }
    }
}
