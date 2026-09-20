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

    public TriadNpcStatsWindow(StatTracker statTracker) : base(Loc.T("NPC stats") + "###SaucyNpcStats")
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

    public override void PreDraw()
    {
        // Re-resolve every frame so the title follows a language change without a reload.
        WindowName = Loc.T("NPC stats") + "###SaucyNpcStats";
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

            ImGui.Text(Loc.T("Matches tracked: {0}", numMatches));
            ImGui.Spacing();

            ImGui.Text(Loc.T("Game stats:"));
            ImGui.Indent();
            // One key rather than three fragments: the separators and their order are the
            // translator's to choose, and CJK does not want an ASCII ", " welded on.
            ImGui.Text(Loc.T(
                "{0} wins, {1} draws, {2} losses",
                savedStats.NumWins,
                savedStats.NumDraws,
                savedStats.NumLosses));
            if (numMatches > 0)
            {
                // No "%%" escaping: Dalamud's ImGui.Text* overloads all route to
                // igTextUnformatted, so a bare '%' is rendered literally.
                var winPctDesc = (1.0f * savedStats.NumWins / numMatches).ToString("P1");
                ImGui.TextColored(colorValue, Loc.T("Win rate: {0}", winPctDesc));
            }
            ImGui.Unindent();
            ImGui.Spacing();

            ImGui.Text(Loc.T("Reward stats:"));
            ImGui.Indent();
            ImGui.Text(Loc.T("MGP: {0}", savedStats.NumCoins));

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
                        ImGui.Text(Loc.T("{0} card: {1}", cardOb.Name, kvp.Value));
                        sumNetGain += kvp.Value * cardInfo.SaleValue;

                        if (savedStats.NumWins > 0)
                        {
                            var dropPct = 1.0f * kvp.Value / savedStats.NumWins;

                            ImGui.SameLine();
                            ImGui.TextColored(colorValue, dropPct.ToString("P1"));
                        }
                    }
                }
            }

            ImGui.Unindent();
            ImGui.Spacing();

            ImGui.Text(Loc.T("MGP per match:"));
            ImGui.SameLine();
            if (numMatches > 0)
            {
                ImGui.TextColored(colorValue, $"{(1.0f * sumNetGain / numMatches):0.#}");
                ImGui.SameLine();
                ImGuiComponents.HelpMarker(Loc.T("Includes MGP from selling cards"));
            }
            else
            {
                ImGui.TextColored(colorGray, "--");
            }

            ImGui.NewLine();

            if (ImGui.Button(Loc.T("Copy")))
            {
                CopyStatstoClipboard(savedStats);
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Reset")))
            {
                statTracker.RemoveNpcStats(npcInfo);
            }
        }
        else
        {
            ImGui.Text(Loc.T("NPC stats"));
            ImGui.SameLine();
            ImGui.TextColored(colorGray, "--");
        }
    }

    private void CopyStatstoClipboard(TriadNpcStatRecord savedStats)
    {
        var desc = Loc.T("{0} stats:", npcName) + "\n" +
                   Loc.T("{0} matches (W:{1}/D:{2}/L:{3})", savedStats.GetNumMatches(), savedStats.NumWins, savedStats.NumDraws, savedStats.NumLosses);
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
            desc += "\n" + Loc.T("no card drops");
        }

        ImGui.SetClipboardText(desc);
    }
}
