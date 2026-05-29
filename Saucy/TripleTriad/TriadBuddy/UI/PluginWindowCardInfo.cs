using Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using Saucy;
using System;
using System.Numerics;
using Saucy.TripleTriad;
using TriadBuddy;
namespace TriadBuddyPlugin;

public class PluginWindowCardInfo : Window, IDisposable
{
    private readonly UIReaderTriadCardList uiReaderCardList;
    private bool hasCachedLocStrings;
    private string? locNoAvail;

    private string? locNpcReward;
    private string? locShowOnMap;
    private int numRewardSources;
    private TriadNpc? rewardNpc;
    private GameNpcInfo? rewardNpcInfo;
    private string rewardNpcRules = string.Empty;
    private int rewardSourceIdx = -1;

    private TriadCard? selectedCard;
    private GameCardInfo? selectedCardInfo;

    public PluginWindowCardInfo(UIReaderTriadCardList uiReaderCardList) : base("Card Info")
    {
        this.uiReaderCardList = uiReaderCardList;

        uiReaderCardList.OnVisibilityChanged += _ => UpdateWindowData();
        uiReaderCardList.OnUIStateChanged += _ => UpdateWindowData();

        // doesn't matter will be updated on next draw
        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;
        RespectCloseHotkey = false;
        ForceMainWindow = true;

        Flags = ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                //ImGuiWindowFlags.NoMouseInputs |
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;

    }

    public void Dispose()
    {
        // meh
    }

    private void UpdateLocalizationCache()
    {
        if (hasCachedLocStrings) { return; }
        hasCachedLocStrings = true;

        locNpcReward = "NPC reward:";
        locShowOnMap = "Show on map";
        locNoAvail = "Not available";
    }

    internal void SyncVisibility()
    {
        UpdateWindowData();
    }

    private void UpdateWindowData()
    {
        var canShow = false;

        if (uiReaderCardList != null &&
            uiReaderCardList.IsVisible &&
            uiReaderCardList.cachedState != null &&
            uiReaderCardList.cachedState.iconId == 0)
        {
            if (!IsOpen && TriadMemoryReads.IsAvailable)
            {
                // force refresh owned cards, required for parsing id based on collection index
                GameCardDB.Get().Refresh();
            }

            var parseCtx = new GameUIParser();
            selectedCard = uiReaderCardList.cachedState.ToTriadCard(parseCtx);

            if (selectedCard != null)
            {
                canShow = true;
                selectedCardInfo = GameCardDB.Get().FindById(selectedCard.Id);
            }
        }

        numRewardSources = (selectedCardInfo == null) ? 0 : selectedCardInfo.RewardNpcs.Count;
        rewardSourceIdx = 0;
        UpdateRewardSource();

        IsOpen = canShow;
    }

    public override void PreDraw()
    {
        var requestedSize = uiReaderCardList.cachedState.descriptionSize / ImGuiHelpers.GlobalScale;
        if (ImGuiHelpers.GlobalScale > 1.0f)
        {
            requestedSize.Y = Math.Max(requestedSize.Y, ImGui.GetTextLineHeight() * 6.5f);
        }

        Position = uiReaderCardList.cachedState.descriptionPos;
        Size = requestedSize;
    }

    public override void Draw()
    {
        if (selectedCard != null)
        {
            UpdateLocalizationCache();

            var colorName = SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.Text);
            var colorGray = SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled);

            ImGui.TextColored(colorName, selectedCard.Name.GetLocalized());

            ImGui.Text(CardUtils.GetRarityDesc(selectedCard));
            ImGui.SameLine();
            ImGui.Text($"{selectedCard.Sides[(int)ETriadGameSide.Up]:X}-{selectedCard.Sides[(int)ETriadGameSide.Left]:X}-{selectedCard.Sides[(int)ETriadGameSide.Down]:X}-{selectedCard.Sides[(int)ETriadGameSide.Right]:X}");
            if (selectedCard.Type != ETriadCardType.None)
            {
                ImGui.SameLine();
                ImGui.TextColored(colorGray, LocalizationDB.Get().LocCardTypes[(int)selectedCard.Type].Text);
            }

            ImGui.NewLine();

            if (numRewardSources > 1)
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowAltCircleRight))
                {
                    rewardSourceIdx = (rewardSourceIdx + 1) % numRewardSources;
                    UpdateRewardSource();
                }

                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();
            }

            ImGui.Text(locNpcReward);

            if (selectedCardInfo != null && rewardNpc != null && rewardNpcInfo != null && rewardNpcInfo.Location != null)
            {
                ImGui.SameLine();
                ImGui.TextColored(colorName, rewardNpc.Name.GetLocalized());

                if (numRewardSources > 1)
                {
                    ImGui.Spacing();
                }

                TriadNpcMapUi.DrawMapLocationRow(rewardNpcInfo.Location, locShowOnMap ?? string.Empty);
                ImGui.TextColored(colorGray, rewardNpcRules);
            }
            else
            {
                ImGui.TextColored(colorGray, locNoAvail);
            }
        }
    }

    private void UpdateRewardSource()
    {
        rewardNpc = null;
        rewardNpcInfo = null;
        rewardNpcRules = "";

        if (numRewardSources <= 0 || selectedCardInfo == null)
        {
            return;
        }

        var npcId = (rewardSourceIdx < 0 || rewardSourceIdx >= selectedCardInfo.RewardNpcs.Count) ? -1 : selectedCardInfo.RewardNpcs[rewardSourceIdx];
        if (npcId >= 0 && npcId < TriadNpcDB.Get().npcs.Count)
        {
            rewardNpc = TriadNpcDB.Get().npcs[npcId];
            if (!GameNpcDB.Get().mapNpcs.TryGetValue(npcId, out rewardNpcInfo))
            {
                rewardNpc = null;
            }
        }

        if (rewardNpc != null)
        {
            if (rewardNpc != null && rewardNpc.Rules.Count > 0)
            {
                foreach (var rule in rewardNpc.Rules)
                {
                    if (rewardNpcRules.Length > 0) { rewardNpcRules += ", "; }
                    rewardNpcRules += rule.GetLocalizedName();
                }
            }
            else
            {
                rewardNpcRules = "--";
            }
        }
    }
}
