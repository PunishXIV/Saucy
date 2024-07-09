using Dalamud;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using ImGuiNET;
using System;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class PluginWindowNpcStats : Window, IDisposable
    {
        private readonly StatTracker statTracker;

        private GameNpcInfo? npcInfo;
        private string? npcName;

        private string? locTitle;
        private string? locNumTracked;
        private string? locBtnReset;
        private string? locBtnCopy;
        private string? locGameStats;
        private string? locGameWins;
        private string? locGameDraws;
        private string? locGameLosses;
        private string? locDropStats;
        private string? locDropMGP;
        private string? locDropCard;
        private string? locEstMGP;
        private string? locEstMGPHint;
        private bool hasCachedLocStrings;

        public PluginWindowNpcStats(StatTracker statTracker) : base("NPC stats")
        {
            this.statTracker = statTracker;

            IsOpen = false;

            SizeConstraints = new WindowSizeConstraints() { MinimumSize = new Vector2(350, 0), MaximumSize = new Vector2(700, 800) };

            Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar;
            RespectCloseHotkey = false;

            if (Plugin.CurrentLocManager != null)
            {
                Plugin.CurrentLocManager.LocalizationChanged += (_) => { hasCachedLocStrings = false; };
            }
        }

        public void Dispose()
        {
            // meh
        }

        private void UpdateLocalizationCache()
        {
            if (hasCachedLocStrings) { return; }
            hasCachedLocStrings = true;

            locTitle = Localization.Localize("NS_Title", "NPC stats");
            locNumTracked = Localization.Localize("NS_NumMacthes", "Num tracked matches: {0}");
            locBtnReset = Localization.Localize("NS_Reset", "Reset");
            locBtnCopy = Localization.Localize("NS_Copy", "Copy");
            locGameStats = Localization.Localize("NS_GameStats", "Game stats:");
            locGameWins = Localization.Localize("NS_GameStatsWins", "{0} wins");
            locGameDraws = Localization.Localize("NS_GameStatsDraws", "{0} draws");
            locGameLosses = Localization.Localize("NS_GameStatsLosses", "{0} losses");
            locDropStats = Localization.Localize("NS_DropStats", "Reward stats:");
            locDropMGP = Localization.Localize("NS_DropMGPAvg", "MGP: {0}");
            locDropCard = Localization.Localize("NS_DropCardName", "{0} card: {1}");
            locEstMGP = Localization.Localize("NS_DropPerMatch", "MGP per match:");
            locEstMGPHint = Localization.Localize("NS_DropIncludesSelling", "Includes MGP from selling cards");
        }

        public void SetupAndOpen(TriadNpc? triadNpc)
        {
            this.npcInfo = null;

            if (triadNpc == null)
            {
                return;
            }

            if (GameNpcDB.Get().mapNpcs.TryGetValue(triadNpc.Id, out var npcInfo))
            {
                this.npcInfo = npcInfo;
                npcName = triadNpc.Name.GetLocalized();

                IsOpen = true;
            }
        }

        public override void Draw()
        {
            var colorName = new Vector4(0.9f, 0.9f, 0.2f, 1);
            var colorValue = new Vector4(0.2f, 0.9f, 0.9f, 1);
            var colorGray = new Vector4(0.6f, 0.6f, 0.6f, 1);
            UpdateLocalizationCache();

            if (npcInfo != null)
            {
                ImGui.TextColored(colorName, npcName);

                var savedStats = statTracker.GetNpcStatsOrDefault(npcInfo);
                int numMatches = savedStats.GetNumMatches();

                ImGui.Text(string.Format(locNumTracked ?? "", numMatches));
                ImGui.Spacing();

                ImGui.Text(locGameStats);
                ImGui.Indent();
                ImGui.Text(string.Format(locGameWins ?? "", savedStats.NumWins) + ",");
                ImGui.SameLine();
                ImGui.Text(string.Format(locGameDraws ?? "", savedStats.NumDraws) + ",");
                ImGui.SameLine();
                ImGui.Text(string.Format(locGameLosses ?? "", savedStats.NumLosses));
                if (numMatches > 0)
                {
                    string winPctDesc = (1.0f * savedStats.NumWins / numMatches).ToString("P1").Replace("%", "%%");
                    ImGui.TextColored(colorValue, string.Format(locGameWins ?? "", winPctDesc));
                }
                ImGui.Unindent();
                ImGui.Spacing();

                ImGui.Text(locDropStats ?? "");
                ImGui.Indent();
                ImGui.Text(string.Format(locDropMGP ?? "", savedStats.NumCoins));

                var cardDB = TriadCardDB.Get();
                var gameCardDB = GameCardDB.Get();
                int sumNetGain = savedStats.NumCoins - (numMatches * npcInfo.matchFee);
                foreach (var kvp in savedStats.Cards)
                {
                    if (kvp.Key >= 0 && kvp.Key < cardDB.cards.Count && kvp.Value > 0)
                    {
                        var cardOb = cardDB.FindById(kvp.Key);
                        if (cardOb != null && cardOb.IsValid() && gameCardDB.mapCards.TryGetValue(kvp.Key, out var cardInfo))
                        {
                            ImGui.Text(string.Format(locDropCard ?? "", cardOb.Name.GetLocalized(), kvp.Value));
                            sumNetGain += kvp.Value * cardInfo.SaleValue;

                            if (savedStats.NumWins > 0)
                            {
                                float dropPct = 1.0f * kvp.Value / savedStats.NumWins;

                                ImGui.SameLine();
                                ImGui.TextColored(colorValue, dropPct.ToString("P1").Replace("%", "%%"));
                            }
                        }
                    }
                }

                ImGui.Unindent();
                ImGui.Spacing();

                ImGui.Text(locEstMGP);
                ImGui.SameLine();
                if (numMatches > 0)
                {
                    ImGui.TextColored(colorValue, $"{(1.0f * sumNetGain / numMatches):0.#}");
                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(locEstMGPHint ?? "");
                }
                else
                {
                    ImGui.TextColored(colorGray, "--");
                }

                ImGui.NewLine();

                if (ImGui.Button(locBtnCopy))
                {
                    CopyStatstoClipboard(savedStats);
                }
                ImGui.SameLine();
                if (ImGui.Button(locBtnReset))
                {
                    statTracker.RemoveNpcStats(npcInfo);
                }
            }
            else
            {
                ImGui.Text(locTitle);
                ImGui.SameLine();
                ImGui.TextColored(colorGray, "--");
            }
        }

        private void CopyStatstoClipboard(Configuration.NpcStatInfo savedStats)
        {
            string desc = $"{npcName} stats:\n{savedStats.GetNumMatches()} matches (W:{savedStats.NumWins}/D:{savedStats.NumDraws}/L:{savedStats.NumLosses})";
            if (savedStats.Cards.Count > 0)
            {
                var cardDB = TriadCardDB.Get();
                foreach (var kvp in savedStats.Cards)
                {
                    if (kvp.Key >= 0 && kvp.Key < cardDB.cards.Count && kvp.Value > 0)
                    {
                        var cardOb = cardDB.FindById(kvp.Key);
                        if (cardOb != null && cardOb.IsValid())
                        {
                            desc += $"\n[{cardOb.Id}]:{cardOb.Name.GetCodeName()} => {kvp.Value}";
                        }
                    }
                }
            }
            else
            {
                desc += "\nno card drops";
            }

            ImGui.SetClipboardText(desc);
        }
    }
}
