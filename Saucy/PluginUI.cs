using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using FFTriadBuddy;
using Dalamud.Bindings.ImGui;
using PunishLib.ImGuiMethods;
using Saucy.CuffACur;
using Saucy.OtherGames;
using Saucy.TripleTriad;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using TriadBuddyPlugin;


namespace Saucy
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    public unsafe class PluginUI : IDisposable
    {
        private Configuration configuration;

        // this extra bool exists for ImGui, since you can't ref a property
        private bool visible = false;
        public bool Visible
        {
            get { return visible; }
            set { visible = value; }
        }

        private bool settingsVisible = false;
        private GameNpcInfo currentNPC;

        public bool SettingsVisible
        {
            get { return settingsVisible; }
            set { settingsVisible = value; }
        }

        public GameNpcInfo CurrentNPC
        {
            get => currentNPC;
            set
            {
                if (currentNPC != value)
                {
                    TriadAutomater.TempCardsWonList.Clear();
                    currentNPC = value;
                }
            }
        }

        public PluginUI(Configuration configuration)
        {
            this.configuration = Saucy.Config;
        }

        public void Dispose()
        {
        }

        public bool Enabled { get; set; } = false;

        public void Draw()
        {
            DrawMainWindow();
        }

        public void DrawMainWindow()
        {
            if (!Visible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(520, 420), ImGuiCond.FirstUseEver);
            //ImGui.SetNextWindowSizeConstraints(new Vector2(520, 420), new Vector2(float.MaxValue, float.MaxValue));
            if (ImGui.Begin("Saucy Config", ref visible))
            {
                if (ImGui.BeginTabBar("###Games", ImGuiTabBarFlags.Reorderable))
                {
                    if (ImGui.BeginTabItem("Cuff-a-Cur"))
                    {
                        DrawCufTab();
                        ImGui.EndTabItem();
                    }

                    if (Saucy.openTT)
                    {
                        Saucy.openTT = false;
                        if (ImGui.BeginTabItem("Triple Triad", ImGuiTabItemFlags.SetSelected))
                        {
                            DrawTriadTab();
                            ImGui.EndTabItem();
                        }
                    }
                    else
                    {
                        if (ImGui.BeginTabItem("Triple Triad"))
                        {
                            DrawTriadTab();
                            ImGui.EndTabItem();
                        }
                    }

                    if (ImGui.BeginTabItem("Out on a Limb"))
                    {
                        if (ImGui.BeginTabBar($"LimbTab"))
                        {
                            if (ImGui.BeginTabItem("Main"))
                            {
                                Saucy.P.LimbManager.DrawSettings();
                                ImGui.EndTabItem();
                            }
                            if (ImGui.BeginTabItem($"Debug"))
                            {
                                Saucy.P.LimbManager.DrawDebug();
                                ImGui.EndTabItem();
                            }

                            ImGui.EndTabBar();
                        }
                       
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Other Games"))
                    {
                        DrawOtherGamesTab();
                        ImGui.EndTabItem();
                    }


                    if (ImGui.BeginTabItem("Stats"))
                    {
                        DrawStatsTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("About"))
                    {
                        AboutTab.Draw("Saucy");
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        private void DrawOtherGamesTab()
        {
            //ImGui.Checkbox("Enable Air Force One Module", ref AirForceOneModule.ModuleEnabled);

            var sliceIsRightEnabled = SliceIsRightModule.ModuleEnabled;
            if (ImGui.Checkbox("Enable Slice is Right Module", ref sliceIsRightEnabled))
            {
                SliceIsRightModule.ModuleEnabled = sliceIsRightEnabled;
                Saucy.Config.Save();
            }

            if (ImGui.Checkbox("Enable Auto Mini-Cactpot", ref Saucy.Config.EnableAutoMiniCactpot))
                Saucy.Config.Save();
        }

        private void DrawStatsTab()
        {
            if (ImGui.BeginTabBar("Stats"))
            {
                if (ImGui.BeginTabItem("Lifetime"))
                {
                    this.DrawStatsTab(Saucy.Config.Stats, out bool reset);

                    if (reset)
                    {
                        Saucy.Config.Stats = new();
                        Saucy.Config.Save();
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Session"))
                {
                    this.DrawStatsTab(Saucy.Config.SessionStats, out bool reset);
                    if (reset)
                        Saucy.Config.SessionStats = new();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }

        private void DrawStatsTab(Stats stat, out bool reset)
        {
            if (ImGui.BeginTabBar("Games"))
            {
                if (ImGui.BeginTabItem("Cuff-a-Cur"))
                {
                    DrawCuffStats(stat);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Triple Triad"))
                {
                    DrawTTStats(stat);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem($"Out on a Limb"))
                {
                    DrawLimbStats(stat);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
            reset = ImGui.Button("RESET STATS (Hold Ctrl)", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y)) && ImGui.GetIO().KeyCtrl;
        }

        private void DrawLimbStats(Stats stat)
        {
            ImGui.BeginChild("Limb Stats", new Vector2(0, ImGui.GetContentRegionAvail().Y - 30f), true);
            ImGui.Columns(3, default, false);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText(ImGuiColors.DalamudRed, "Out on a Limb", true);
            ImGuiHelpers.ScaledDummy(10f);
            ImGui.Columns(2, default, false);
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Games Played", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("MGP Won", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.LimbGamesPlayed.ToString("N0")}");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.LimbMGP.ToString("N0")}");

            ImGui.EndChild();
        }

        private void DrawCuffStats(Stats stat)
        {
            ImGui.BeginChild("Cuff Stats", new Vector2(0, ImGui.GetContentRegionAvail().Y - 30f), true);
            ImGui.Columns(3, default, false);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText(ImGuiColors.DalamudRed, "Cuff-a-cur", true);
            ImGuiHelpers.ScaledDummy(10f);
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Games Played", true);
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CuffGamesPlayed.ToString("N0")}");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.Spacing();
            ImGuiEx.CenterColumnText("BRUISING!", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("PUNISHING!!", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("BRUTAL!!!!", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CuffBruisings.ToString("N0")}");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CuffPunishings.ToString("N0")}");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CuffBrutals.ToString("N0")}");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("MGP Won", true);
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CuffMGP.ToString("N0")}");

            ImGui.EndChild();
        }

        private void DrawTTStats(Stats stat)
        {
            ImGui.BeginChild("TT Stats", new Vector2(0, ImGui.GetContentRegionAvail().Y - 30f), true);
            ImGui.Columns(3, default, false);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText(ImGuiColors.DalamudRed, "Triple Triad", true);
            ImGuiHelpers.ScaledDummy(10f);
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Games Played", true);
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.GamesPlayedWithSaucy.ToString("N0")}");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.Spacing();
            ImGuiEx.CenterColumnText("Wins", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Losses", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Draws", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.GamesWonWithSaucy.ToString("N0")}");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.GamesLostWithSaucy.ToString("N0")}");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.GamesDrawnWithSaucy.ToString("N0")}");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Win Rate", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Cards Won", true);
            ImGui.NextColumn();
            if (stat.NPCsPlayed.Count > 0)
            {
                ImGuiEx.CenterColumnText("Most Played NPC", true);
                ImGui.NextColumn();
            }
            else
            {
                ImGui.NextColumn();
            }

            if (stat.GamesPlayedWithSaucy > 0)
            {
                ImGuiEx.CenterColumnText($"{Math.Round(((double)stat.GamesWonWithSaucy / (double)stat.GamesPlayedWithSaucy) * 100, 2)}%");
            }
            else
            {
                ImGuiEx.CenterColumnText("");
            }
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CardsDroppedWithSaucy.ToString("N0")}");
            ImGui.NextColumn();

            if (stat.NPCsPlayed.Count > 0)
            {
                ImGuiEx.CenterColumnText($"{stat.NPCsPlayed.OrderByDescending(x => x.Value).First().Key}");
                ImGuiEx.CenterColumnText($"{stat.NPCsPlayed.OrderByDescending(x => x.Value).First().Value.ToString("N0")} times");
                ImGui.NextColumn();
                ImGui.NextColumn();
                ImGui.NextColumn();
            }

            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("MGP Won", true);
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText("Total Card Drop Value", true);
            ImGui.NextColumn();
            if (stat.CardsWon.Count > 0)
            {
                ImGuiEx.CenterColumnText("Most Won Card", true);
            }
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.MGPWon.ToString("N0")} MGP");
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{GetDroppedCardValues(stat).ToString("N0")} MGP");
            ImGui.NextColumn();
            if (stat.CardsWon.Count > 0)
            {
                ImGuiEx.CenterColumnText($"{TriadCardDB.Get().FindById((int)stat.CardsWon.OrderByDescending(x => x.Value).First().Key).Name.GetLocalized()}");
                ImGui.NextColumn();
                ImGui.NextColumn();
                ImGui.NextColumn();
                ImGuiEx.CenterColumnText($"{stat.CardsWon.OrderByDescending(x => x.Value).First().Value.ToString("N0")} times");
            }

            ImGui.Columns(1);
            ImGui.EndChild();
        }

        private int GetDroppedCardValues(Stats stat)
        {
            int output = 0;
            foreach (var card in stat.CardsWon)
                output += GameCardDB.Get().FindById((int)card.Key).SaleValue * stat.CardsWon[card.Key];

            return output;
        }

        public void DrawTriadTab()
        {
            bool enabled = TriadAutomater.ModuleEnabled;

            ImGui.TextWrapped(@"How to use: Challenge an NPC you wish to play cards with. Once you have initiated the challenge, click ""Enable Triad Module"".");
            ImGui.Separator();

            if (ImGui.Checkbox("Enable Triad Module", ref enabled))
            {
                TriadAutomater.ModuleEnabled = enabled;

                if (enabled)
                    CufModule.ModuleEnabled = false;
            }

            bool autoOpen = configuration.OpenAutomatically;

            if (ImGui.Checkbox("Open Saucy When Challenging an NPC", ref autoOpen))
            {
                configuration.OpenAutomatically = autoOpen;
                configuration.Save();
            }

            int selectedDeck = configuration.SelectedDeckIndex;

            if (Saucy.TTSolver.profileGS.GetPlayerDecks().Count() > 0)
            {
                bool useAutoDeck = Saucy.Config.UseRecommendedDeck;
                if (ImGui.Checkbox("Automatically choose your deck with the best win chance", ref useAutoDeck))
                {
                    Saucy.Config.UseRecommendedDeck = useAutoDeck;
                    Saucy.Config.Save();
                }

                if (!Saucy.Config.UseRecommendedDeck)
                {
                    ImGui.PushItemWidth(200);
                    string preview = "";

                    if (selectedDeck == -1 || Saucy.TTSolver.profileGS.GetPlayerDecks()[selectedDeck] is null)
                    {
                        preview = "";
                    }
                    else
                    {
                        preview = selectedDeck >= 0 ? Saucy.TTSolver.profileGS.GetPlayerDecks()[selectedDeck].name : string.Empty;
                    }

                    if (ImGui.BeginCombo("Select Deck", preview))
                    {
                        if (ImGui.Selectable(""))
                        {
                            configuration.SelectedDeckIndex = -1;
                        }

                        foreach (var deck in Saucy.TTSolver.profileGS.GetPlayerDecks())
                        {
                            if (deck is null) continue;
                            var index = deck.id;
                            //var index = Saucy.TTSolver.preGameDecks.Where(x => x.Value == deck).First().Key;
                            if (ImGui.Selectable(deck.name, index == selectedDeck))
                            {
                                configuration.SelectedDeckIndex = index;
                                configuration.Save();
                            }
                        }

                        ImGui.EndCombo();
                    }

                }
            }
            else
            {
                ImGui.TextWrapped("Please initiate a challenge with an NPC to populate your deck list.");
            }

            if (ImGui.Checkbox("Play X Amount of Times", ref TriadAutomater.PlayXTimes) && (TriadAutomater.NumberOfTimes <= 0 || TriadAutomater.PlayUntilCardDrops || TriadAutomater.PlayUntilAllCardsDropOnce))
            {
                TriadAutomater.NumberOfTimes = 1;
                TriadAutomater.PlayUntilCardDrops = false;
                TriadAutomater.PlayUntilAllCardsDropOnce = false;
            }

            if (ImGui.Checkbox("Play Until Any Cards Drop", ref TriadAutomater.PlayUntilCardDrops) && (TriadAutomater.NumberOfTimes <= 0 || TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilAllCardsDropOnce))
            {
                TriadAutomater.NumberOfTimes = 1;
                TriadAutomater.PlayXTimes = false;
                TriadAutomater.PlayUntilAllCardsDropOnce = false;
            }


            if (GameNpcDB.Get().mapNpcs.TryGetValue(Saucy.TTSolver.preGameNpc?.Id ?? -1, out var npcInfo))
            {
                CurrentNPC = npcInfo;
            }
            else
            {
                CurrentNPC = null;
            }

            if (ImGui.Checkbox($"Play Until All Cards Drop from NPC at Least X Times {(CurrentNPC is null ? "" : $"({TriadNpcDB.Get().FindByID(CurrentNPC.npcId).Name.GetLocalized()})")}", ref TriadAutomater.PlayUntilAllCardsDropOnce))
            {
                TriadAutomater.TempCardsWonList.Clear();
                TriadAutomater.PlayUntilCardDrops = false;
                TriadAutomater.PlayXTimes = false;
                TriadAutomater.NumberOfTimes = 1;
            }

            bool onlyUnobtained = Saucy.Config.OnlyUnobtainedCards;

            if (TriadAutomater.PlayUntilAllCardsDropOnce)
            {
                ImGui.SameLine();
                if (ImGui.Checkbox("Only Unobtained Cards", ref onlyUnobtained))
                {
                    TriadAutomater.TempCardsWonList.Clear();
                    Saucy.Config.OnlyUnobtainedCards = onlyUnobtained;
                    Saucy.Config.Save();
                }
            }

            if (TriadAutomater.PlayUntilAllCardsDropOnce && CurrentNPC != null)
            {
                ImGui.Indent();
                GameCardDB.Get().Refresh();
                foreach (var card in CurrentNPC.rewardCards)
                {
                    if ((Saucy.Config.OnlyUnobtainedCards && !GameCardDB.Get().FindById(card).IsOwned) || !Saucy.Config.OnlyUnobtainedCards)
                    {
                        TriadAutomater.TempCardsWonList.TryAdd((uint)card, 0);
                        ImGui.Text($"- {TriadCardDB.Get().FindById((int)GameCardDB.Get().FindById(card).CardId).Name.GetLocalized()} {TriadAutomater.TempCardsWonList[(uint)card]}/{TriadAutomater.NumberOfTimes}");
                    }

                }

                if (Saucy.Config.OnlyUnobtainedCards && TriadAutomater.TempCardsWonList.Count == 0)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                    ImGui.TextWrapped($@"You already have all cards from this NPC. This feature will not work until you untick ""Only Unobtained Cards"" or choose a different NPC.");
                    ImGui.PopStyleColor();
                }
                ImGui.Unindent();
            }


            if (TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops || TriadAutomater.PlayUntilAllCardsDropOnce)
            {
                ImGui.PushItemWidth(150f);
                ImGui.Text("How many times:");
                ImGui.SameLine();

                if (ImGui.InputInt("###NumberOfTimes", ref TriadAutomater.NumberOfTimes))
                {
                    if (TriadAutomater.NumberOfTimes <= 0)
                        TriadAutomater.NumberOfTimes = 1;
                }

                ImGui.Checkbox("Log out after finishing", ref TriadAutomater.LogOutAfterCompletion);

                bool playSound = Saucy.Config.PlaySound;

                ImGui.Columns(2, default, false);
                if (ImGui.Checkbox("Play sound upon completion", ref playSound))
                {
                    Saucy.Config.PlaySound = playSound;
                    Saucy.Config.Save();
                }

                if (playSound)
                {
                    ImGui.NextColumn();
                    ImGui.Text("Select Sound");
                    if (ImGui.BeginCombo("###SelectSound", Saucy.Config.SelectedSound))
                    {
                        string path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds");
                        foreach (var file in new DirectoryInfo(path).GetFiles())
                        {
                            if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(file.FullName)}", Saucy.Config.SelectedSound == Path.GetFileNameWithoutExtension(file.FullName)))
                            {
                                Saucy.Config.SelectedSound = Path.GetFileNameWithoutExtension(file.FullName);
                                Saucy.Config.Save();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.Button("Open Sound Folder"))
                    {
                        Process.Start("explorer.exe", @$"{Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds")}");
                    }
                    ImGuiComponents.HelpMarker("Drop any MP3 files into the sound folder to add your own custom sounds.");
                }
                ImGui.Columns(1);
            }
        }

        public unsafe void DrawCufTab()
        {
            bool enabled = CufModule.ModuleEnabled;

            ImGui.TextWrapped(@"How to use: Click ""Enable Cuff Module"" then walk up to a Cuff-a-cur machine.");
            ImGui.Separator();

            if (ImGui.Checkbox("Enable Cuff Module", ref enabled))
            {
                CufModule.ModuleEnabled = enabled;
                if (enabled && TriadAutomater.ModuleEnabled)
                    TriadAutomater.ModuleEnabled = false;
            }

            if (ImGui.Checkbox("Play X Amount of Times", ref TriadAutomater.PlayXTimes) && TriadAutomater.NumberOfTimes <= 0)
            {
                TriadAutomater.NumberOfTimes = 1;
            }

            if (TriadAutomater.PlayXTimes)
            {
                ImGui.PushItemWidth(150f);
                ImGui.Text("How many times:");
                ImGui.SameLine();

                if (ImGui.InputInt("###NumberOfTimes", ref TriadAutomater.NumberOfTimes))
                {
                    if (TriadAutomater.NumberOfTimes <= 0)
                        TriadAutomater.NumberOfTimes = 1;
                }

                ImGui.Checkbox("Log out after finishing", ref TriadAutomater.LogOutAfterCompletion);

                bool playSound = Saucy.Config.PlaySound;

                ImGui.Columns(2, default, false);
                if (ImGui.Checkbox("Play sound upon completion", ref playSound))
                {
                    Saucy.Config.PlaySound = playSound;
                    Saucy.Config.Save();
                }

                if (playSound)
                {
                    ImGui.NextColumn();
                    ImGui.Text("Select Sound");
                    if (ImGui.BeginCombo("###SelectSound", Saucy.Config.SelectedSound))
                    {
                        string path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds");
                        foreach (var file in new DirectoryInfo(path).GetFiles())
                        {
                            if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(file.FullName)}", Saucy.Config.SelectedSound == Path.GetFileNameWithoutExtension(file.FullName)))
                            {
                                Saucy.Config.SelectedSound = Path.GetFileNameWithoutExtension(file.FullName);
                                Saucy.Config.Save();
                            }
                        }

                        ImGui.EndCombo();
                    }

                    if (ImGui.Button("Open Sound Folder"))
                    {
                        Process.Start("explorer.exe", @$"{Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds")}");
                    }
                    ImGuiComponents.HelpMarker("Drop any MP3 files into the sound folder to add your own custom sounds.");
                }
                ImGui.Columns(1);
            }
        }
    }
}
