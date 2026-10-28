using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Saucy.KeyboundBrawler;
namespace Saucy;

public partial class PluginUI
{
    private static void DrawKeyboundBrawlerPanel()
    {
        DrawPanelHeader(Loc.T(ModuleDisplayNames.KeyboundBrawler), Loc.T("typing duty"));
        KeyboundBrawlerModule.DrawSettings();
    }

    private static void DrawKeyboundStatsCard(Stats life, Stats sess)
    {
        var headline = life.KeyboundGamesPlayed == 0
            ? Loc.T("no games played")
            : Loc.T("{0} games", life.KeyboundGamesPlayed.ToString("N0"));
        DrawStatsCard(Loc.T(ModuleDisplayNames.KeyboundBrawler), headline, () =>
        {
            using var table = ImRaii.Table("##stats_keybound", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.SizingStretchProp);
            if (!table)
            {
                return;
            }

            var hours = StatsSessionClock.GetKeyboundElapsedHours();
            StatsHeader();
            StatsRow(Loc.T("Games"), life.KeyboundGamesPlayed, sess.KeyboundGamesPlayed,
                perHour: SessionCountPerHour(sess.KeyboundGamesPlayed, hours));
            StatsRow(Loc.T("MGP won"), $"{life.KeyboundMGP:N0}", $"{sess.KeyboundMGP:N0}", true,
                perHour: SessionMgpPerHour(sess.KeyboundMGP, hours));
            StatsRow(Loc.T("MGC won"), life.KeyboundMGC, sess.KeyboundMGC,
                perHour: SessionCountPerHour(sess.KeyboundMGC, hours));
            StatsRow(Loc.T("Keybound coffers"), life.KeyboundCoffers, sess.KeyboundCoffers,
                perHour: SessionCountPerHour(sess.KeyboundCoffers, hours));
        });
    }
}
