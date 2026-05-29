using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using Saucy;
using Saucy.TripleTriad;
using System;
using System.Collections.Generic;
using System.Numerics;
using TriadBuddy;
namespace TriadBuddyPlugin;

public unsafe class PluginWindowCardSearch : Window, IDisposable
{
    private const float WindowContentWidth = 270.0f;

    private readonly List<Tuple<TriadCard, GameCardInfo>> listCards = [];
    private readonly List<Tuple<TriadCard, int>> listNpcReward = [];
    private readonly List<Tuple<TriadNpc, GameNpcInfo>> listNpcs = [];
    private readonly PluginWindowNpcStats statsWindow;

    private readonly UIReaderTriadCardList uiReaderCardList;
    private int filterMode = -1;
    private bool hasCachedLocStrings;
    private bool hideNpcBeatenOnce;
    private bool hideNpcCompleted;
    private string? locEstMGP;
    private string? locFilterActive;
    private string? locHideBeatenNpc;
    private string? locHideCompletedNpc;
    private string? locNoAvail;
    private string? locNotOwnedOnly;

    private string? locNpcOnly;
    private string? locNpcReward;
    private string? locNpcStats;
    private string? locShowOnMap;
    private string? locTabCards;
    private string? locTabNpc;
    private int numNotOwnedRewards;
    private ImGuiTextFilterPtr searchFilterCard;
    private ImGuiTextFilterPtr searchFilterNpc;

    private int selectedCardIdx;
    private int selectedNpcIdx;
    private bool showNotOwnedOnly;

    private bool showNpcMatchesOnly;

    private bool npcFilterDataStale = true;
    private bool sawGameDataReady;
    private DateTime lastOwnershipRefreshUtc = DateTime.MinValue;

    private static bool IsGameDataReady => dataLoader.IsDataReady;

    public PluginWindowCardSearch(UIReaderTriadCardList uiReaderCardList, PluginWindowNpcStats statsWindow) : base("Card Search")
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

        // doesn't matter will be updated on next draw
        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(WindowContentWidth + 20, 0), MaximumSize = new(WindowContentWidth + 20, 1000)
        };

        ForceMainWindow = true;
        RespectCloseHotkey = false;
        Flags = ImGuiWindowFlags.NoDecoration |
                //ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                //ImGuiWindowFlags.NoMouseInputs |
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;

    }

    public void Dispose()
    {
        ImGuiNative.Destroy(searchFilterCard.Handle);
        ImGuiNative.Destroy(searchFilterNpc.Handle);
    }

    private void UpdateLocalizationCache()
    {
        if (hasCachedLocStrings) { return; }
        hasCachedLocStrings = true;

        locNpcOnly = "NPC matches only";
        locNotOwnedOnly = "Not owned only";
        locFilterActive = "(Collection filtering is active)";
        locHideBeatenNpc = "Hide beaten once";
        locHideCompletedNpc = "Hide completed";
        locTabCards = "Cards";
        locTabNpc = "NPC";
        locNpcReward = "NPC reward:";
        locShowOnMap = "Show on map";
        locNoAvail = "Not available";
        locNpcStats = "NPC stats";
        locEstMGP = "MGP per match:";
    }

    internal void SyncVisibility()
    {
        UpdateWindowData();
    }

    internal void OnGameDataReady()
    {
        if (sawGameDataReady)
            return;

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
            return;

        var wasOpen = IsOpen;
        IsOpen = uiReaderCardList.IsVisible;

        if (IsOpen && !wasOpen)
        {
            if (TriadMemoryReads.IsAvailable)
                GameCardDB.Get().Refresh();

            filterMode = -1;
            searchFilterCard.Clear();
            GameNpcDB.Get().RefreshCompleted();
            if (hideNpcBeatenOnce)
                GameNpcDB.Get().RefreshBeatenOnce();
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
            return;

        if (GameNpcDB.Get().mapNpcs.Count == 0)
            return;

        if (listNpcs.Count > 0)
            return;

        GenerateNpcList();
    }

    private void RefreshNpcProgress()
    {
        if (!IsGameDataReady || GameNpcDB.Get().mapNpcs.Count == 0)
            return;

        if (TriadMemoryReads.IsAvailable)
            GameCardDB.Get().Refresh();

        if (hideNpcCompleted)
            GameNpcDB.Get().RefreshCompleted();

        if (hideNpcBeatenOnce)
            GameNpcDB.Get().RefreshBeatenOnce();
    }

    public void OnUIStateChanged(UIStateTriadCardList uiState) => RebuildCardList(uiState);

    private void RebuildCardList(UIStateTriadCardList uiState)
    {
        var needsOwnership = showNotOwnedOnly || uiState.filterMode != 0;
        if (filterMode == uiState.filterMode && listCards.Count > 0 && (!needsOwnership || GameCardDB.Get().ownedCardIds.Count > 0))
            return;

        filterMode = uiState.filterMode;
        listCards.Clear();

        if (!IsGameDataReady)
            return;

        if (TriadMemoryReads.IsAvailable && needsOwnership)
            GameCardDB.Get().Refresh();

        var cardDB = TriadCardDB.Get();
        var cardInfoDB = GameCardDB.Get();

        var includeOwned = filterMode != 2;
        var includeMissing = filterMode != 1;

        foreach (var card in cardDB.cards)
        {
            if (card == null || !card.IsValid())
                continue;

            var cardInfo = cardInfoDB.FindById(card.Id);
            if (cardInfo == null)
                continue;

            var isOwned = IsCardOwned(card.Id);
            if ((includeOwned && isOwned) || (includeMissing && !isOwned))
                listCards.Add(new(card, cardInfo));
        }

        if (listCards.Count > 1)
            listCards.Sort((a, b) => a.Item1.SortOrder.CompareTo(b.Item1.SortOrder));

        selectedCardIdx = -1;
    }

    public override void PreDraw()
    {
        if (!IsGameDataReady || !uiReaderCardList.IsVisible)
            return;

        Position = new Vector2(
            uiReaderCardList.cachedState.screenPos.X + uiReaderCardList.cachedState.screenSize.X + 10,
            uiReaderCardList.cachedState.screenPos.Y);
    }

    public override void Draw()
    {
        UpdateLocalizationCache();

        if (!IsGameDataReady)
        {
            ImGui.TextDisabled("Loading card data…");
            return;
        }

        OnGameDataReady();

        if (ImGui.BeginTabBar("##CollectionSearch"))
        {
            if (ImGui.BeginTabItem(locTabCards))
            {
                DrawCardsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(locTabNpc))
            {
                DrawNpcTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private static bool IsCardOwned(int cardId)
    {
        var db = GameCardDB.Get();
        if (db.ownedCardIds.Contains(cardId))
            return true;

        if (!TriadMemoryReads.IsAvailable)
            return false;

        return TriadMemoryReads.TryIsCardOwned(cardId);
    }

    private void RefreshOwnershipIfNeeded(bool force = false)
    {
        if (!TriadMemoryReads.IsAvailable)
            return;

        var now = DateTime.UtcNow;
        if (!force && (now - lastOwnershipRefreshUtc) < TimeSpan.FromSeconds(1))
            return;

        lastOwnershipRefreshUtc = now;
        GameCardDB.Get().Refresh();
    }

    private void DrawCardsTab()
    {
        if (showNotOwnedOnly)
            RefreshOwnershipIfNeeded();

        RebuildCardList(uiReaderCardList.cachedState);
        searchFilterCard.Draw("", WindowContentWidth * ImGuiHelpers.GlobalScale);

        if (ImGui.BeginListBox("##cards", new(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 10)))
        {
            for (var idx = 0; idx < listCards.Count; idx++)
            {
                (var cardOb, var cardInfo) = listCards[idx];
                if ((showNpcMatchesOnly && cardInfo.RewardNpcs.Count <= 0) ||
                    (showNotOwnedOnly && IsCardOwned(cardOb.Id)))
                {
                    continue;
                }

                var itemDesc = $"[{CardUtils.GetOrderDesc(cardOb)}] {CardUtils.GetRarityDesc(cardOb)} {CardUtils.GetUIDesc(cardOb)}";
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

        ImGui.Spacing();
        if (ImGui.Checkbox(locNpcOnly, ref showNpcMatchesOnly))
        {
            C.TriadCollection.CheckCardNpcMatchOnly = showNpcMatchesOnly;
            C.Save();
        }

        if (ImGui.Checkbox(locNotOwnedOnly, ref showNotOwnedOnly))
        {
            if (showNotOwnedOnly)
                RefreshOwnershipIfNeeded(force: true);
            C.TriadCollection.CheckCardNotOwnedOnly = showNotOwnedOnly;
            C.Save();
        }

        if (filterMode != 0)
        {
            ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled), locFilterActive);
        }
    }

    private void DrawNpcTab()
    {
        TryPopulateNpcList();

        if (npcFilterDataStale && (hideNpcBeatenOnce || hideNpcCompleted))
        {
            RefreshNpcProgress();
            npcFilterDataStale = false;
        }

        searchFilterNpc.Draw("", WindowContentWidth * ImGuiHelpers.GlobalScale);

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
        if (ImGui.BeginListBox("##npcs", new(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 10)))
        {
            for (var idx = 0; idx < listNpcs.Count; idx++)
            {
                (var npcOb, var npcInfo) = listNpcs[idx];
                if ((hideNpcBeatenOnce && (npcInfo.IsBeatenOnce || npcInfo.IsExcludedFromAchievementTracker)) ||
                    (hideNpcCompleted && npcInfo.IsCompleted))
                {
                    continue;
                }

                var itemDesc = npcOb.Name.GetLocalized();
                if (string.IsNullOrWhiteSpace(itemDesc))
                    itemDesc = $"NPC #{npcOb.Id}";

                if (searchFilterNpc.PassFilterBool(itemDesc))
                {
                    visibleCount++;
                    var isSelected = selectedNpcIdx == idx;
                    if (ImGui.Selectable(itemDesc, isSelected))
                    {
                        selectedNpcIdx = idx;
                        GenerateNpcRewardList();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
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
        if (ImGui.Checkbox(locHideBeatenNpc, ref hideNpcBeatenOnce))
        {
            npcFilterDataStale = true;
            RefreshNpcProgress();
            npcFilterDataStale = false;
            C.TriadCollection.CheckNpcHideBeaten = hideNpcBeatenOnce;
            C.Save();
        }

        if (ImGui.Checkbox(locHideCompletedNpc, ref hideNpcCompleted))
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
            return;

        if (npcInfo.Location != null)
        {
            DrawIconTextRow(FontAwesomeIcon.Map, locShowOnMap, () => Svc.GameGui.OpenMapWithMapLink(npcInfo.Location),
                $"{npcInfo.Location.PlaceName} {npcInfo.Location.CoordinateString}");
        }

        TriadNpcQuestUi.DrawUnlockQuestIconRow(npcInfo);

        ImGui.Spacing();
        var hasAvgRewards = StatTracker.GetAverageRewardPerMatchDesc(C.TriadCollection, npcInfo, out var avgRewardPerMatch);
        DrawIconTextRow(FontAwesomeIcon.ChartLine, null, () => statsWindow.SetupAndOpen(npcData.Item1), () =>
        {
            ImGui.Text(locNpcStats + (hasAvgRewards ? "," : ""));
            if (hasAvgRewards)
            {
                ImGui.SameLine();
                ImGui.Text(locEstMGP);
                ImGui.SameLine();
                ImGui.Text(avgRewardPerMatch.ToString("0.#"));
            }
        });

        ImGui.Spacing();
        ImGui.Text($"{locNpcReward} {numNotOwnedRewards}");
        if (listNpcReward.Count > 0)
        {
            var settingsDB = PlayerSettingsDB.Get();

            ImGui.BeginListBox("##cardReward", new(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 4.5f));
            for (var idx = 0; idx < listNpcReward.Count; idx++)
            {
                (var cardOb, var cardListIdx) = listNpcReward[idx];
                var isCardOwned = settingsDB.ownedCards.Contains(cardOb);

                var itemDesc = $"{CardUtils.GetOrderDesc(cardOb)} {CardUtils.GetUIDesc(cardOb)}";
                var isSelected = selectedCardIdx == cardListIdx;

                if (isCardOwned)
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xffa8a8a8);

                if (ImGui.Selectable($"{CardUtils.GetRarityDesc(cardOb)}  {itemDesc}", isSelected))
                {
                    selectedCardIdx = cardListIdx;
                    OnCardSelectionChanged();
                }

                if (isCardOwned)
                    ImGui.PopStyleColor(1);

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndListBox();
        }
        else
        {
            ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled), locNoAvail);
        }
    }

    private static void DrawIconTextRow(FontAwesomeIcon icon, string? tooltip, Action onIconClick, string text) =>
        DrawIconTextRow(icon, tooltip, onIconClick, () => ImGui.Text(text));

    private static void DrawIconTextRow(FontAwesomeIcon icon, string? tooltip, Action onIconClick, Action drawText)
    {
        ImGui.AlignTextToFramePadding();
        var rowY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(rowY - ImGui.GetStyle().FramePadding.Y);
        if (ImGuiComponents.IconButton(icon))
            onIconClick();
        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        ImGui.SetCursorPosY(rowY);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        drawText();
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
            var filterEnum = (filterMode == 1) ? GameCardCollectionFilter.OnlyOwned :
                (filterMode == 2) ? GameCardCollectionFilter.OnlyMissing :
                GameCardCollectionFilter.All;

            var collectionPos = cardInfo.Collection[(int)filterEnum];

            //Svc.Log.Info($"Card selection! {cardOb.Name.GetLocalized()}, filter:{filterEnum} ({filterMode}) => page:{collectionPos.PageIndex}, cell:{collectionPos.CellIndex}");
            uiReaderCardList.SetPageAndGridView(collectionPos.PageIndex, collectionPos.CellIndex);
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
                listNpcs.Add(new(npc, kvp.Value));
        }

        if (listNpcs.Count > 1)
        {
            listNpcs.Sort((a, b) => a.Item1.Name.GetLocalized().CompareTo(b.Item1.Name.GetLocalized()));
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
            listNpcReward.Sort((a, b) => a.Item1.Name.GetLocalized().CompareTo(b.Item1.Name.GetLocalized()));
        }
    }
}
