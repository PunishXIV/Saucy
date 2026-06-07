using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
namespace Saucy.TripleTriad;

public class TriadCardInfoWindow : Window, IDisposable
{
    private readonly TriadCardSearchWindow cardSearchWindow;
    private readonly UIReaderTriadCardList uiReaderCardList;
    private int numRewardSources;
    private TriadNpc? rewardNpc;
    private GameNpcInfo? rewardNpcInfo;
    private string rewardNpcRules = string.Empty;
    private int rewardSourceIdx = -1;

    private TriadCard? selectedCard;
    private GameCardInfo? selectedCardInfo;

    public TriadCardInfoWindow(UIReaderTriadCardList uiReaderCardList, TriadCardSearchWindow cardSearchWindow) : base("Card Info")
    {
        this.uiReaderCardList = uiReaderCardList;
        this.cardSearchWindow = cardSearchWindow;

        uiReaderCardList.OnVisibilityChanged += _ => UpdateWindowData();
        uiReaderCardList.OnUIStateChanged += _ => UpdateWindowData();

        PositionCondition = ImGuiCond.Always;
        SizeCondition = ImGuiCond.Always;
        RespectCloseHotkey = false;
        ForceMainWindow = true;

        Flags = ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;
    }

    public void Dispose()
    {
    }

    internal void SyncVisibility() => UpdateWindowData();

    private void UpdateWindowData()
    {
        var canShow = false;

        if (uiReaderCardList != null &&
            uiReaderCardList.IsVisible &&
            uiReaderCardList.cachedState != null)
        {
            if (!IsOpen && TriadMemoryReads.IsAvailable)
            {
                GameCardDB.Get().Refresh();
            }

            uiReaderCardList.RefreshLiveSelectionState();
            selectedCard = uiReaderCardList.ResolveSelectedCard();

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
        uiReaderCardList.RefreshLiveSelectionState();
        SyncSelectedCardFromReader();

        var lineHeight = ImGui.GetTextLineHeight();
        var requestedSize = uiReaderCardList.cachedState.descriptionSize / ImGuiHelpers.GlobalScale;
        if (ImGuiHelpers.GlobalScale > 1.0f)
        {
            requestedSize.Y = Math.Max(requestedSize.Y, lineHeight * 6.5f);
        }

        requestedSize.Y += lineHeight;
        Position = uiReaderCardList.cachedState.descriptionPos - new Vector2(0, lineHeight);
        Size = requestedSize;
    }

    private void SyncSelectedCardFromReader()
    {
        if (!uiReaderCardList.IsVisible)
        {
            return;
        }

        var card = uiReaderCardList.ResolveSelectedCard();
        if (card?.Id == selectedCard?.Id)
        {
            return;
        }

        selectedCard = card;
        selectedCardInfo = card != null ? GameCardDB.Get().FindById(card.Id) : null;
        numRewardSources = selectedCardInfo?.RewardNpcs.Count ?? 0;
        rewardSourceIdx = 0;
        UpdateRewardSource();
    }

    public override void Draw()
    {
        SyncSelectedCardFromReader();

        if (selectedCard != null)
        {
            var colorName = SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.Text);
            var colorGray = SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled);

            using (ImRaii.PushColor(ImGuiCol.WindowBg, ImGui.GetColorU32(ImGuiCol.PopupBg)))
            {
                ImGui.TextColored(colorGray, CardUtils.GetOrderDesc(selectedCard));

                ImGui.TextColored(colorName, selectedCard.Name);

                ImGui.Text(CardUtils.GetRarityDesc(selectedCard));
                ImGui.SameLine();
                ImGui.Text($"{selectedCard.Sides[(int)ETriadGameSide.Up]:X}-{selectedCard.Sides[(int)ETriadGameSide.Left]:X}-{selectedCard.Sides[(int)ETriadGameSide.Down]:X}-{selectedCard.Sides[(int)ETriadGameSide.Right]:X}");
                if (selectedCard.Type != ETriadCardType.None)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(colorGray, GameDataText.GetCardTypeName(selectedCard.Type));
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

                ImGui.Text("Reward from:");

                if (selectedCardInfo != null && rewardNpc != null && rewardNpcInfo != null && rewardNpcInfo.Location != null)
                {
                    ImGui.SameLine();
                    DrawNpcNameLink(rewardNpc.Name, colorName, rewardNpc.Id);

                    if (numRewardSources > 1)
                    {
                        ImGui.Spacing();
                    }

                    if (TriadBattleHall.ShouldBlockMapNavigation(rewardNpc, rewardNpcInfo.Location))
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, colorGray);
                        ImGui.TextWrapped(TriadBattleHall.NavigationBlockedMessage);
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        TriadNpcMapUi.DrawMapLocationRow(rewardNpcInfo.Location, "Show on map", rewardNpc);
                    }

                    ImGui.TextColored(colorGray, rewardNpcRules);
                }
                else
                {
                    ImGui.TextColored(colorGray, "Not available");
                }
            }
        }
    }

    private void DrawNpcNameLink(string npcName, Vector4 color, int npcId)
    {
        using (ImRaii.PushColor(ImGuiCol.Header, 0))
        {
            using (ImRaii.PushColor(ImGuiCol.HeaderHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered)))
            {
                using (ImRaii.PushColor(ImGuiCol.HeaderActive, ImGui.GetColorU32(ImGuiCol.ButtonActive)))
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, color))
                    {
                        if (ImGui.Selectable(npcName))
                        {
                            cardSearchWindow.FocusNpcById(npcId);
                        }
                    }
                }
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Show in NPC tab");
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
        var npcByIndex = TriadNpcDB.Get().GetByIndex(npcId);
        if (npcByIndex != null)
        {
            rewardNpc = npcByIndex;
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
