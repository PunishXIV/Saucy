using ECommons.DalamudServices;
using ImGuiNET;
using PunishLib.ImGuiMethods;
using Saucy.CuffACur;
using Saucy.TripleTriad;
using System;
using System.Linq;
using System.Numerics;
using TriadBuddyPlugin;

namespace Saucy
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    class PluginUI : IDisposable
    {
        private Configuration configuration;

        // this extra bool exists for ImGui, since you can't ref a property
        private bool visible = false;
        public bool Visible
        {
            get { return this.visible; }
            set { this.visible = value; }
        }

        private bool settingsVisible = false;
        public bool SettingsVisible
        {
            get { return this.settingsVisible; }
            set { this.settingsVisible = value; }
        }

        public PluginUI(Configuration configuration)
        {
            this.configuration = configuration;
        }

        public void Dispose()
        {
        }

        public bool Enabled { get; set; } = false;

        public void Draw()
        {
            // This is our only draw handler attached to UIBuilder, so it needs to be
            // able to draw any windows we might have open.
            // Each method checks its own visibility/state to ensure it only draws when
            // it actually makes sense.
            // There are other ways to do this, but it is generally best to keep the number of
            // draw delegates as low as possible.

            DrawMainWindow();
        }

        public void DrawMainWindow()
        {
            if (!Visible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(520, 420), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(520, 420), new Vector2(float.MaxValue, float.MaxValue));
            if (ImGui.Begin("Saucy Config", ref this.visible, ImGuiWindowFlags.AlwaysAutoResize))
            {
                if (ImGui.BeginTabBar("Games"))
                {
                    if (ImGui.BeginTabItem("Cuff-a-Cur"))
                    {
                        DrawCufTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Triple Triad"))
                    {
                        DrawTriadTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("About"))
                    {
                        AboutTab.Draw(Saucy.P);
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        public void DrawTriadTab()
        {
            bool enabled = TriadAutomater.ModuleEnabled;

            ImGui.TextWrapped(@"How to use: Challenge an NPC you wish to play cards with. Once you have initiated the challenge, click ""Enable Triad Module"".");
            ImGui.Separator();

            if (ImGui.Checkbox("Enable Triad Module", ref enabled))
            {
                TriadAutomater.ModuleEnabled = enabled;
            }

            int selectedDeck = configuration.SelectedDeckIndex;

            if (Saucy.TTSolver.preGameDecks.Count > 0)
            {
                string preview = selectedDeck >= 0 ? Saucy.TTSolver.preGameDecks[selectedDeck].name : string.Empty;
                if (ImGui.BeginCombo("Select Deck", preview))
                {
                    if (ImGui.Selectable(""))
                    {
                        configuration.SelectedDeckIndex = -1;
                    }

                    foreach (var deck in Saucy.TTSolver.preGameDecks.Values)
                    {
                        var index = Saucy.TTSolver.preGameDecks.Where(x => x.Value == deck).First().Key;
                        if (ImGui.Selectable(deck.name, index == selectedDeck))
                        {
                            configuration.SelectedDeckIndex = index;
                            configuration.Save();
                        }
                    }

                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextWrapped("Please initiate a challenge with an NPC to populate your deck list.");
            }

            if (ImGui.Checkbox("Play X Amount of Times", ref TriadAutomater.PlayXTimes) && (TriadAutomater.NumberOfTimes <= 0 || TriadAutomater.PlayUntilCardDrops))
            {
                TriadAutomater.NumberOfTimes = 1;
                TriadAutomater.PlayUntilCardDrops = false;
            }

            if (ImGui.Checkbox("Play Until Cards Drop", ref TriadAutomater.PlayUntilCardDrops) && (TriadAutomater.NumberOfTimes <= 0 || TriadAutomater.PlayXTimes))
            {
                TriadAutomater.NumberOfTimes = 1;
                TriadAutomater.PlayXTimes = false;
            }

            if (TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops)
            {
                ImGui.Text("How many times:");
                ImGui.SameLine();
                if (ImGui.InputInt("", ref TriadAutomater.NumberOfTimes))
                {
                    if (TriadAutomater.NumberOfTimes <= 0)
                        TriadAutomater.NumberOfTimes = 1;
                }
            }

        }
        public void DrawCufTab()
        {
            bool enabled = CufModule.ModuleEnabled;

            ImGui.TextWrapped(@"How to use: Click ""Enable Cuff Module"" then walk up to a Cuff-a-cur machine.");
            ImGui.Separator();

            if (ImGui.Checkbox("Enable Cuff Module", ref enabled))
            {
                CufModule.ModuleEnabled = enabled;
            }
        }
    }
}
