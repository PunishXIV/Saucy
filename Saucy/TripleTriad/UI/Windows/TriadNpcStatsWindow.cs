using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using System;
namespace Saucy.TripleTriad;

public class TriadNpcStatsWindow : Window, IDisposable
{
    private readonly StatTracker statTracker;

    private GameNpcInfo? npcInfo;
    private string? npcName;

    public TriadNpcStatsWindow(StatTracker statTracker) : base("NPC stats")
    {
        this.statTracker = statTracker;

        IsOpen = false;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(350, 0), MaximumSize = new(700, 800)
        };

        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar;
        RespectCloseHotkey = false;
    }

    public void Dispose()
    {
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
            npcName = triadNpc.Name;

            IsOpen = true;
        }
    }

    public override void Draw()
    {
        var colorName = SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.Text);
        var colorValue = SaucyTheme.ColorOr(SaucyTheme.BodyTextAccent, ImGuiCol.Text);
        var colorGray = SaucyTheme.TextMutedColor;

        if (npcInfo != null)
        {
            ImGui.TextColored(colorName, npcName);

            var savedStats = statTracker.GetNpcStatsOrDefault(npcInfo);
            var numMatches = savedStats.GetNumMatches();

            ImGui.Text($"Matches tracked: {numMatches}");
            ImGui.Spacing();

            ImGui.Text("Game stats:");
            ImGui.Indent();
            ImGui.Text($"{savedStats.NumWins} wins,");
            ImGui.SameLine();
            ImGui.Text($"{savedStats.NumDraws} draws,");
            ImGui.SameLine();
            ImGui.Text($"{savedStats.NumLosses} losses");
            if (numMatches > 0)
            {
                var winPctDesc = (1.0f * savedStats.NumWins / numMatches).ToString("P1").Replace("%", "%%");
                ImGui.TextColored(colorValue, $"{winPctDesc} wins");
            }
            ImGui.Unindent();
            ImGui.Spacing();

            ImGui.Text("Reward stats:");
            ImGui.Indent();
            ImGui.Text($"MGP: {savedStats.NumCoins}");

            var cardDB = TriadCardDB.Get();
            var gameCardDB = GameCardDB.Get();
            var sumNetGain = savedStats.NumCoins - (numMatches * npcInfo.matchFee);
            foreach (var kvp in savedStats.Cards)
            {
                if (kvp.Key >= 0 && kvp.Key < cardDB.cards.Count && kvp.Value > 0)
                {
                    var cardOb = cardDB.FindById(kvp.Key);
                    if (cardOb != null && cardOb.IsValid() && gameCardDB.mapCards.TryGetValue(kvp.Key, out var cardInfo))
                    {
                        ImGui.Text($"{cardOb.Name} card: {kvp.Value}");
                        sumNetGain += kvp.Value * cardInfo.SaleValue;

                        if (savedStats.NumWins > 0)
                        {
                            var dropPct = 1.0f * kvp.Value / savedStats.NumWins;

                            ImGui.SameLine();
                            ImGui.TextColored(colorValue, dropPct.ToString("P1").Replace("%", "%%"));
                        }
                    }
                }
            }

            ImGui.Unindent();
            ImGui.Spacing();

            ImGui.Text("MGP per match:");
            ImGui.SameLine();
            if (numMatches > 0)
            {
                ImGui.TextColored(colorValue, $"{(1.0f * sumNetGain / numMatches):0.#}");
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Includes MGP from selling cards");
            }
            else
            {
                ImGui.TextColored(colorGray, "--");
            }

            ImGui.NewLine();

            if (ImGui.Button("Copy"))
            {
                CopyStatstoClipboard(savedStats);
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset"))
            {
                statTracker.RemoveNpcStats(npcInfo);
            }
        }
        else
        {
            ImGui.Text("NPC stats");
            ImGui.SameLine();
            ImGui.TextColored(colorGray, "--");
        }
    }

    private void CopyStatstoClipboard(TriadNpcStatRecord savedStats)
    {
        var desc = $"{npcName} stats:\n{savedStats.GetNumMatches()} matches (W:{savedStats.NumWins}/D:{savedStats.NumDraws}/L:{savedStats.NumLosses})";
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
                        desc += $"\n[{cardOb.Id}]:{cardOb.Name} => {kvp.Value}";
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
