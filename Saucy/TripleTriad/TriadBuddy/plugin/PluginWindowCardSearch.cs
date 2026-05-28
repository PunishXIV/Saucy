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
        UpdateWindowData();

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

        if (TriadCollectionUi.Loc != null)
            TriadCollectionUi.Loc.LocalizationChanged += _ => { hasCachedLocStrings = false; };
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

        locNpcOnly = Localization.Localize("CS_NpcOnly", "NPC matches only");
        locNotOwnedOnly = Localization.Localize("CS_NotOwnedOnly", "Not owned only");
        locFilterActive = Localization.Localize("CS_FilterActive", "(Collection filtering is active)");
        locHideBeatenNpc = Localization.Localize("CS_BeatenNpc", "Hide beaten once");
        locHideCompletedNpc = Localization.Localize("CS_CompletedNpc", "Hide completed");
        locTabCards = Localization.Localize("CS_TabCards", "Cards");
        locTabNpc = Localization.Localize("CS_TabNpc", "NPC");

        // reuse CardInfo locs
        locNpcReward = Localization.Localize("CI_NpcReward", "NPC reward:");
        locShowOnMap = Localization.Localize("CI_ShowMap", "Show on map");
        locNoAvail = Localization.Localize("CI_NotAvail", "Not available");

        // reuse NpcStats
        locNpcStats = Localization.Localize("NS_Title", "NPC stats");
        locEstMGP = Localization.Localize("NS_DropPerMatch", "MGP per match:");
    }

    internal void SyncVisibility()
    {
        UpdateWindowData();
    }

    private void UpdateWindowData()
    {
        var wasOpen = IsOpen;
        IsOpen = uiReaderCardList.IsVisible;

        if (IsOpen && !wasOpen)
        {
            GameCardDB.Get().Refresh();
            filterMode = -1;
            searchFilterCard.Clear();

            GameNpcDB.Get().Refresh();
            searchFilterNpc.Clear();

            OnUIStateChanged(uiReaderCardList.cachedState);
            GenerateNpcList();
        }
    }

    public void OnUIStateChanged(UIStateTriadCardList uiState)
    {
        if (filterMode != uiState.filterMode)
        {
            filterMode = uiState.filterMode;
            listCards.Clear();

            var cardDB = TriadCardDB.Get();
            var cardInfoDB = GameCardDB.Get();

            var includeOwned = filterMode != 2;
            var includeMissing = filterMode != 1;

            foreach (var card in cardDB.cards)
            {
                if (card != null && card.IsValid())
                {
                    var cardInfo = cardInfoDB.FindById(card.Id);
                    if (cardInfo != null)
                    {
                        if ((includeOwned && cardInfo.IsOwned) || (includeMissing && !cardInfo.IsOwned))
                        {
                            listCards.Add(new(card, cardInfo));
                        }
                    }
                }
            }

            if (listCards.Count > 1)
            {
                listCards.Sort((a, b) => a.Item1.SortOrder.CompareTo(b.Item1.SortOrder));
            }

            selectedCardIdx = -1;
        }
    }

    public override void PreDraw()
    {
        var vp = ImGuiHelpers.MainViewport.Pos;
        Position = vp + uiReaderCardList.cachedState.screenPos
                 + new Vector2(uiReaderCardList.cachedState.screenSize.X + 10, 0);
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##CollectionSearch"))
        {
            UpdateLocalizationCache();

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

    private void DrawCardsTab()
    {
        var showOwnedCheckbox = filterMode == 0;
        searchFilterCard.Draw("", WindowContentWidth * ImGuiHelpers.GlobalScale);

        if (ImGui.BeginListBox("##cards", new(WindowContentWidth * ImGuiHelpers.GlobalScale, ImGui.GetTextLineHeightWithSpacing() * 10)))
        {
            for (var idx = 0; idx < listCards.Count; idx++)
            {
                (var cardOb, var cardInfo) = listCards[idx];
                if ((showNpcMatchesOnly && cardInfo.RewardNpcs.Count <= 0) ||
                    (showOwnedCheckbox && showNotOwnedOnly && cardInfo.IsOwned))
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

        if (showOwnedCheckbox)
        {
            if (ImGui.Checkbox(locNotOwnedOnly, ref showNotOwnedOnly))
            {
                C.TriadCollection.CheckCardNotOwnedOnly = showNotOwnedOnly;
                C.Save();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), locFilterActive);
        }
    }

    private void DrawNpcTab()
    {
        searchFilterNpc.Draw("", WindowContentWidth * ImGuiHelpers.GlobalScale);

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
                if (searchFilterNpc.PassFilterBool(itemDesc))
                {
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

        ImGui.Spacing();
        if (ImGui.Checkbox(locHideBeatenNpc, ref hideNpcBeatenOnce))
        {
            C.TriadCollection.CheckNpcHideBeaten = hideNpcBeatenOnce;
            C.Save();
        }

        if (ImGui.Checkbox(locHideCompletedNpc, ref hideNpcCompleted))
        {
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
        if (npcData != null && npcData.Item2 != null && npcData.Item2.Location != null)
        {
            var cursorY = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(cursorY - ImGui.GetStyle().FramePadding.Y);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Map))
            {
                Svc.GameGui.OpenMapWithMapLink(npcData.Item2.Location);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(locShowOnMap);
            }

            ImGui.SetCursorPosY(cursorY);
            ImGui.SameLine();
            ImGui.Text($"{npcData.Item2.Location.PlaceName} {npcData.Item2.Location.CoordinateString}");

            TriadNpcQuestUi.DrawUnlockQuest(npcData.Item2);

            cursorY += ImGui.GetTextLineHeight() + (ImGui.GetStyle().FramePadding.Y * 3);
            ImGui.SetCursorPosY(cursorY - ImGui.GetStyle().FramePadding.Y);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChartLine))
            {
                statsWindow.SetupAndOpen(npcData.Item1);
            }

            var hasAvgRewards = StatTracker.GetAverageRewardPerMatchDesc(C.TriadCollection, npcData.Item2, out var avgRewardPerMatch);
            ImGui.SetCursorPosY(cursorY);
            ImGui.SameLine();
            ImGui.Text(locNpcStats + (hasAvgRewards ? "," : ""));

            if (hasAvgRewards)
            {
                ImGui.SameLine();
                ImGui.Text(locEstMGP);
                ImGui.SameLine();
                ImGui.Text(avgRewardPerMatch.ToString("0.#"));
            }

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
                var colorGray = new Vector4(0.6f, 0.6f, 0.6f, 1);
                ImGui.TextColored(colorGray, locNoAvail);
            }
        }
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
            var npc = (kvp.Key >= 0 && kvp.Key < npcDB.npcs.Count) ? npcDB.npcs[kvp.Key] : null;
            if (npc != null)
            {
                listNpcs.Add(new(npc, kvp.Value));
            }
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
