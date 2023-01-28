using Dalamud;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using ImGuiNET;
using System;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class PluginWindowCardInfo : Window, IDisposable
    {
        private readonly UIReaderTriadCardList uiReaderCardList;
        private readonly GameGui gameGui;

        private TriadCard selectedCard;
        private GameCardInfo selectedCardInfo;
        private GameNpcInfo rewardNpcInfo;
        private TriadNpc rewardNpc;
        private string rewardNpcRules;
        private int rewardSourceIdx;
        private int numRewardSources;

        private string locNpcReward;
        private string locShowOnMap;
        private string locNoAvail;

        public PluginWindowCardInfo(UIReaderTriadCardList uiReaderCardList, GameGui gameGui) : base("Card Info")
        {
            this.uiReaderCardList = uiReaderCardList;
            this.gameGui = gameGui;

            uiReaderCardList.OnVisibilityChanged += (_) => UpdateWindowData();
            uiReaderCardList.OnUIStateChanged += (_) => UpdateWindowData();
            UpdateWindowData();

            // doesn't matter will be updated on next draw
            PositionCondition = ImGuiCond.None;
            SizeCondition = ImGuiCond.None;
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

            Plugin.CurrentLocManager.LocalizationChanged += (_) => CacheLocalization();
            CacheLocalization();
        }

        public void Dispose()
        {
            // meh
        }

        private void CacheLocalization()
        {
            locNpcReward = Localization.Localize("CI_NpcReward", "NPC reward:");
            locShowOnMap = Localization.Localize("CI_ShowMap", "Show on map");
            locNoAvail = Localization.Localize("CI_NotAvail", "Not available");
        }

        private void UpdateWindowData()
        {
            bool canShow = (uiReaderCardList != null) && uiReaderCardList.IsVisible && (uiReaderCardList.cachedState?.iconId == 0);
            if (canShow)
            {
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
                var colorName = new Vector4(0.9f, 0.9f, 0.2f, 1);
                var colorGray = new Vector4(0.6f, 0.6f, 0.6f, 1);

                ImGui.TextColored(colorName, selectedCard.Name.GetLocalized());

                ImGui.Text($"{(int)selectedCard.Rarity + 1}★");
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

                if (selectedCardInfo != null && rewardNpc != null && rewardNpcInfo != null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(colorName, rewardNpc.Name.GetLocalized());

                    if (numRewardSources > 1)
                    {
                        ImGui.Spacing();
                    }

                    //ImGui.NewLine();
                    var cursorY = ImGui.GetCursorPosY();
                    ImGui.SetCursorPosY(cursorY - ImGui.GetStyle().FramePadding.Y);
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Map))
                    {
                        gameGui.OpenMapWithMapLink(rewardNpcInfo.Location);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(locShowOnMap);
                    }

                    ImGui.SetCursorPosY(cursorY);
                    ImGui.SameLine();

                    var indentPosX = ImGui.GetCursorPosX();
                    ImGui.Text($"{rewardNpcInfo.Location.PlaceName} {rewardNpcInfo.Location.CoordinateString}");

                    ImGui.NewLine();
                    ImGui.SameLine(indentPosX);
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

            if (numRewardSources <= 0)
            {
                return;
            }

            int npcId = (rewardSourceIdx < 0 || rewardSourceIdx >= selectedCardInfo.RewardNpcs.Count) ? -1 : selectedCardInfo.RewardNpcs[rewardSourceIdx];
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
}
