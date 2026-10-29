using Dalamud.Bindings.ImGui;
using System;
namespace Saucy;

public unsafe partial class PluginUI
{
    private const int TimelineRowsPerPage = 20;

    private int _timelineRowsVisible = TimelineRowsPerPage;

    private void DrawTimelinePanel()
    {
        DrawPanelHeader(Loc.T("Timeline"), Loc.T("predicted Gold Saucer GATE schedule"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var pinned = GoldSaucerGateTimeline.Current(now);

        SaucyTheme.DrawCard(pinned.State == TimelineState.Coming ? Loc.T("Coming up") : Loc.T("Now"), null, () =>
        {
            ImGui.TextWrapped(pinned.Gate.Name.Localized);
            ImGui.TextWrapped(pinned.Gate.Variant.Localized);
            ImGui.TextDisabled(FormatTimelineStart(pinned.Gate.Slot, now));
            var remaining = Math.Max(0, pinned.EndsAt - now);
            var countdown = $"{remaining / 60}:{remaining % 60:D2}";
            ImGui.TextUnformatted(pinned.State switch
            {
                TimelineState.Entry => Loc.T("Entry closes in {0}", countdown),
                TimelineState.InProgress => Loc.T("Ends in {0}", countdown),
                _ => Loc.T("Starts in {0}", countdown)
            });
        });

        ImGuiLayout.DrawCollapsingSection(Loc.T("Options"), "SaucyTimelineOptions", ImGuiTreeNodeFlags.DefaultOpen, () =>
        {
            for (var i = 0; i < GoldSaucerGateTimeline.Names.Length; i++)
            {
                var selected = (C.GoldSaucerGates.TimelineGateFilter & (1 << i)) != 0;
                if (ImGui.Checkbox($"{GoldSaucerGateTimeline.Names[i].Localized}###SaucyTimelineFilter{i}", ref selected))
                {
                    C.GoldSaucerGates.TimelineGateFilter ^= 1 << i;
                    _timelineRowsVisible = TimelineRowsPerPage;
                    C.Save();
                }
            }

            var notify = C.GoldSaucerGates.TimelineNotifyOnEntry;
            if (ImGui.Checkbox($"{Loc.T("Notify in chat when entry opens")}###SaucyTimelineNotify", ref notify))
            {
                C.GoldSaucerGates.TimelineNotifyOnEntry = notify;
                C.Save();
            }
        });

        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Upcoming"));
        if ((C.GoldSaucerGates.TimelineGateFilter & GoldSaucerGateTimeline.AllGateMask) == 0)
        {
            ImGui.TextDisabled(Loc.T("Select at least one GATE above."));
            return;
        }

        var slot = pinned.Gate.Slot + GoldSaucerGateTimeline.SlotSeconds;
        var shown = 0;
        while (shown < _timelineRowsVisible)
        {
            var gate = GoldSaucerGateTimeline.Predict(slot);
            if (GoldSaucerGateTimeline.IsShown(C.GoldSaucerGates.TimelineGateFilter, gate))
            {
                ImGui.Separator();
                ImGui.TextWrapped($"{FormatTimelineStart(slot, now)}  {gate.Name.Localized}");
                ImGui.TextWrapped(gate.Variant.Localized);
                shown++;
            }

            slot += GoldSaucerGateTimeline.SlotSeconds;
        }

        if (ImGui.Button($"{Loc.T("Show {0} more", TimelineRowsPerPage)}###SaucyTimelineMore"))
        {
            _timelineRowsVisible += TimelineRowsPerPage;
        }
    }

    private static string FormatTimelineStart(long slot, long now)
    {
        var local = DateTimeOffset.FromUnixTimeSeconds(slot).ToLocalTime();
        return slot - now < 86400 ? local.ToString("t") : local.ToString("ddd, MMM d, HH:mm");
    }
}
