using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Linq;
using System.Numerics;
namespace Saucy;

public partial class PluginUI
{
    private void DrawStatsTab()
    {
        DrawStatsToolbar();

        (var life, var sess) = (C.Stats, C.SessionStats);

        DrawStatsCard(Loc.T("Triple Triad"), TriadHeadline(life), () => DrawTriadRows(life, sess));
        DrawStatsCard(Loc.T("Cuff-a-Cur"), CuffHeadline(life), () => DrawCuffRows(life, sess));
        DrawStatsCard(Loc.T("Out on a Limb"), LimbHeadline(life), () => DrawLimbRows(life, sess));
        DrawStatsCard(Loc.T("Air Force One"), AirForceHeadline(life), () => DrawAirForceRows(life, sess));
    }

    private static void DrawStatsToolbar()
    {
        ImGui.TextDisabled(Loc.T("Hold Ctrl to reset stats."));
        ImGui.SameLine();
        var lifeLbl = Loc.T("Reset Lifetime");
        var sessLbl = Loc.T("Reset Session");
        var pad = ImGui.GetStyle().FramePadding.X * 2f;
        var lifeW = ImGui.CalcTextSize(lifeLbl).X + pad;
        var sessW = ImGui.CalcTextSize(sessLbl).X + pad;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rightX = ImGui.GetWindowContentRegionMax().X - lifeW - sessW - spacing;
        if (rightX > ImGui.GetCursorPosX())
        {
            ImGui.SetCursorPosX(rightX);
        }
        using var disabled = ImRaii.Disabled(!ImGui.GetIO().KeyCtrl);
        if (ImGui.Button(lifeLbl))
        {
            C.Stats = new();
            C.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(sessLbl))
        {
            C.SessionStats = new();
            C.SessionStartTime = DateTime.UtcNow;
            StatsSessionClock.ResetAll();
        }
        ImGui.Dummy(new(0, 2));
    }

    private static string TriadHeadline(Stats s)
    {
        if (s.GamesPlayedWithSaucy == 0)
        {
            return Loc.T("no games played");
        }
        var pct = Math.Round(s.GamesWonWithSaucy / (double)s.GamesPlayedWithSaucy * 100, 1);
        return Loc.T("{0} games \u00b7 {1}% win", s.GamesPlayedWithSaucy.ToString("N0"), pct);
    }

    private static string CuffHeadline(Stats s) =>
        s.CuffGamesPlayed == 0 ? Loc.T("no games played") : Loc.T("{0} games", s.CuffGamesPlayed.ToString("N0"));

    private static string LimbHeadline(Stats s) =>
        s.LimbGamesPlayed == 0 ? Loc.T("no games played") : Loc.T("{0} games", s.LimbGamesPlayed.ToString("N0"));

    private static string AirForceHeadline(Stats s) =>
        s.AirForceGamesPlayed == 0 ? Loc.T("no games played") : Loc.T("{0} games", s.AirForceGamesPlayed.ToString("N0"));

    private static void DrawTriadRows(Stats life, Stats sess)
    {
        using var table = ImRaii.Table("##stats_triad", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }
        StatsHeader();
        StatsRow(Loc.T("Games"), life.GamesPlayedWithSaucy, sess.GamesPlayedWithSaucy,
            perHour: SessionCountPerHour(sess.GamesPlayedWithSaucy, StatsSessionClock.GetTriadElapsedHours()));
        StatsRow(Loc.T("Wins"), life.GamesWonWithSaucy, sess.GamesWonWithSaucy);
        StatsRow(Loc.T("Losses"), life.GamesLostWithSaucy, sess.GamesLostWithSaucy);
        StatsRow(Loc.T("Draws"), life.GamesDrawnWithSaucy, sess.GamesDrawnWithSaucy);
        StatsRow(Loc.T("Cards won"), life.CardsDroppedWithSaucy, sess.CardsDroppedWithSaucy);
        StatsRow(Loc.T("Card resale value"), $"{GetDroppedCardValues(life):N0}", $"{GetDroppedCardValues(sess):N0}");
        StatsRow(Loc.T("MGP won"), $"{life.MGPWon:N0}", $"{sess.MGPWon:N0}", true,
            perHour: SessionMgpPerHour(sess.MGPWon, StatsSessionClock.GetTriadElapsedHours()));

        (var lifeNpc, var lifeNpcTip) = TopNpcCell(life);
        (var sessNpc, var sessNpcTip) = TopNpcCell(sess);
        StatsRow(Loc.T("Most played NPC"), lifeNpc, sessNpc, tooltipLife: lifeNpcTip, tooltipSess: sessNpcTip);

        (var lifeCard, var lifeCardTip) = TopCardCell(life);
        (var sessCard, var sessCardTip) = TopCardCell(sess);
        StatsRow(Loc.T("Most won card"), lifeCard, sessCard, tooltipLife: lifeCardTip, tooltipSess: sessCardTip);
    }

    private static void DrawCuffRows(Stats life, Stats sess)
    {
        using var table = ImRaii.Table("##stats_cuff", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }
        StatsHeader();
        StatsRow(Loc.T("Games"), life.CuffGamesPlayed, sess.CuffGamesPlayed,
            perHour: SessionCountPerHour(sess.CuffGamesPlayed, StatsSessionClock.GetCuffElapsedHours()));
        StatsRow(Loc.T("Bruisings"), life.CuffBruisings, sess.CuffBruisings);
        StatsRow(Loc.T("Punishings"), life.CuffPunishings, sess.CuffPunishings);
        StatsRow(Loc.T("Brutals"), life.CuffBrutals, sess.CuffBrutals);
        StatsRow(Loc.T("MGP won"), $"{life.CuffMGP:N0}", $"{sess.CuffMGP:N0}", true,
            perHour: SessionMgpPerHour(sess.CuffMGP, StatsSessionClock.GetCuffElapsedHours()));
    }

    private static void DrawLimbRows(Stats life, Stats sess)
    {
        using var table = ImRaii.Table("##stats_limb", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }
        StatsHeader();
        StatsRow(Loc.T("Games"), life.LimbGamesPlayed, sess.LimbGamesPlayed,
            perHour: SessionCountPerHour(sess.LimbGamesPlayed, StatsSessionClock.GetLimbElapsedHours()));
        StatsRow(Loc.T("MGP won"), $"{life.LimbMGP:N0}", $"{sess.LimbMGP:N0}", true,
            perHour: SessionMgpPerHour(sess.LimbMGP, StatsSessionClock.GetLimbElapsedHours()));
    }

    private static void DrawAirForceRows(Stats life, Stats sess)
    {
        using var table = ImRaii.Table("##stats_airforce", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.SizingStretchProp);
        if (!table)
        {
            return;
        }
        StatsHeader();
        StatsRow(Loc.T("Games"), life.AirForceGamesPlayed, sess.AirForceGamesPlayed,
            perHour: SessionCountPerHour(sess.AirForceGamesPlayed, StatsSessionClock.GetAirForceElapsedHours()));
        StatsRow(Loc.T("MGP won"), $"{life.AirForceMGP:N0}", $"{sess.AirForceMGP:N0}", true,
            perHour: SessionMgpPerHour(sess.AirForceMGP, StatsSessionClock.GetAirForceElapsedHours()));
    }

    private static (string display, string? tooltip) TopNpcCell(Stats s)
    {
        if (s.NPCsPlayed.Count == 0)
        {
            return ("\u2014", null);
        }
        var top = s.NPCsPlayed.OrderByDescending(x => x.Value).First();
        return ($"{top.Key} ({top.Value:N0})", Loc.T("{0} games vs {1}", top.Value.ToString("N0"), top.Key));
    }

    private static (string display, string? tooltip) TopCardCell(Stats s)
    {
        if (s.CardsWon.Count == 0)
        {
            return ("\u2014", null);
        }
        var top = s.CardsWon.OrderByDescending(x => x.Value).First();
        var name = TriadCardDB.Get().FindById((int)top.Key)?.Name ?? Loc.T("Card #{0}", top.Key);
        return ($"{name} ({top.Value:N0})", Loc.T("{0} won {1}\u00d7", name, top.Value.ToString("N0")));
    }

    private static void StatsHeader()
    {
        ImGui.TableSetupColumn("Metric", ImGuiTableColumnFlags.WidthStretch, 0.30f);
        ImGui.TableSetupColumn("Lifetime", ImGuiTableColumnFlags.WidthStretch, 0.25f);
        ImGui.TableSetupColumn("Session", ImGuiTableColumnFlags.WidthStretch, 0.25f);
        ImGui.TableSetupColumn("Per Hour", ImGuiTableColumnFlags.WidthStretch, 0.20f);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        RightAlignCellText(Loc.T("Lifetime"), SaucyTheme.ColorOr(SaucyTheme.ColumnHeader, ImGuiCol.Text));
        ImGui.TableNextColumn();
        RightAlignCellText(Loc.T("Session"), SaucyTheme.ColorOr(SaucyTheme.ColumnHeader, ImGuiCol.Text));
        ImGui.TableNextColumn();
        RightAlignCellText(Loc.T("Per Hour"), SaucyTheme.ColorOr(SaucyTheme.ColumnHeader, ImGuiCol.Text));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Session rate since the first counted game of this minigame."));
        }
    }

    private static void StatsRow(string label, int life, int sess, bool accent = false, string? perHour = null) =>
        StatsRow(label, life.ToString("N0"), sess.ToString("N0"), accent, perHour: perHour);

    private static void StatsRow(string label, string life, string sess, bool accent = false,
        string? tooltipLife = null, string? tooltipSess = null, string? perHour = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);

        var col = accent
            ? SaucyTheme.ColorOr(SaucyTheme.BodyTextAccent, ImGuiCol.Text)
            : SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.Text);

        ImGui.TableNextColumn();
        RightAlignCellText(life, col);
        if (tooltipLife != null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltipLife);
        }

        ImGui.TableNextColumn();
        RightAlignCellText(sess, col);
        if (tooltipSess != null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltipSess);
        }

        ImGui.TableNextColumn();
        if (!string.IsNullOrEmpty(perHour))
        {
            RightAlignCellText(perHour, col);
        }
    }

    private static void RightAlignCellText(string text, Vector4 color)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var pad = ImGui.GetStyle().CellPadding;
        var avail = ImGui.GetContentRegionAvail();
        var tw = ImGui.CalcTextSize(text).X;
        var offset = Math.Max(0f, avail.X - tw - pad.X);
        if (offset > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        }

        ImGui.TextColored(color, text);
    }

    private static void DrawStatsCard(string name, string subtitle, Action body) =>
        SaucyTheme.DrawCard(name, subtitle, body);

    private static string SessionMgpPerHour(int sessionMgp, double elapsedHours)
    {
        if (sessionMgp <= 0)
        {
            return "-";
        }

        return $"{(int)Math.Round(sessionMgp / elapsedHours):N0}";
    }

    private static string SessionCountPerHour(int sessionCount, double elapsedHours)
    {
        if (sessionCount <= 0)
        {
            return "-";
        }

        return $"{(int)Math.Round(sessionCount / elapsedHours):N0}";
    }

    private static int GetDroppedCardValues(Stats stat)
    {
        var output = 0;
        foreach (var card in stat.CardsWon)
        {
            output += GameCardDB.Get().FindById((int)card.Key)!.SaleValue * stat.CardsWon[card.Key];
        }
        return output;
    }
}
