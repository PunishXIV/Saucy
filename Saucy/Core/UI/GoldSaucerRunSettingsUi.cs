using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using System;
namespace Saucy;

internal static class GoldSaucerRunSettingsUi
{
    internal const float CompactCountInputWidth = 56f;

    private static readonly int[] DraftMatchCounts = [1, 1];

    public static void CommitDraftMatchCount(GoldSaucerArcadeMachine machine)
    {
        if (!GoldSaucerArcadeRunSession.PlayXTimes(machine))
        {
            return;
        }

        ApplyMatchCount(machine, GoldSaucerArcadeRunSession.GetSettings(machine), DraftMatchCounts[(int)machine]);
    }

    public static void Draw(GoldSaucerArcadeMachine machine, string moduleBlurb)
    {
        ImGui.TextWrapped(moduleBlurb);
        ImGui.Dummy(new(0, 4));

        var settings = GoldSaucerArcadeRunSession.GetSettings(machine);
        var playFixedCount = settings.PlayXTimes;
        if (ImGui.Checkbox("Play X amount of times", ref playFixedCount))
        {
            settings.PlayXTimes = playFixedCount;
            if (playFixedCount && settings.MatchCount <= 0)
            {
                settings.MatchCount = 1;
            }

            C.Save();
        }

        if (!settings.PlayXTimes)
        {
            ImGui.TextDisabled("No stop condition — runs until automation is disabled.");
            ImGui.TextDisabled("Stops queuing new games while Duty Finder is ready.");
        }
        else
        {
            ImGui.Text("How many times:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(CompactCountInputWidth * ImGuiHelpers.GlobalScale);
            var count = Math.Max(1, settings.MatchCount);
            if (ImGui.InputInt($"###GoldSaucerPlayCount{(int)machine}", ref count) ||
                ImGui.IsItemDeactivatedAfterEdit())
            {
                ApplyMatchCount(machine, settings, count);
            }

            DraftMatchCounts[(int)machine] = Math.Max(1, count);

            var remaining = GoldSaucerArcadeMachineHelper.IsEnabled(machine)
                ? GoldSaucerArcadeRunSession.GetRemaining(machine)
                : Math.Max(1, settings.MatchCount);
            ImGui.TextDisabled($"Matches left this session: {remaining}");
        }

        ImGui.Dummy(new(0, 4));
        DrawFakeBreakSettings(machine, settings);

        if (machine == GoldSaucerArcadeMachine.Cuff)
        {
            ImGui.Dummy(new(0, 4));
            SaucyTheme.DrawCard("AutoRetainer", "Bell must be in range", AutoRetainerIntegrationUi.Draw);
        }
    }

    private static void ApplyMatchCount(
        GoldSaucerArcadeMachine machine,
        GoldSaucerArcadeRunSettings settings,
        int count)
    {
        settings.MatchCount = Math.Max(1, count);
        if (GoldSaucerArcadeMachineHelper.IsEnabled(machine))
        {
            GoldSaucerArcadeRunSession.SyncSessionCount(machine);
        }

        C.Save();
    }

    private static void DrawFakeBreakSettings(GoldSaucerArcadeMachine machine, GoldSaucerArcadeRunSettings settings)
    {
        var enableFakeBreak = settings.EnableFakeBreak;
        if (ImGui.Checkbox("Take a break", ref enableFakeBreak))
        {
            settings.EnableFakeBreak = enableFakeBreak;
            if (enableFakeBreak)
            {
                GoldSaucerArcadeFakeBreak.ResetPlayWindow(machine);
            }
            else
            {
                GoldSaucerArcadeFakeBreak.Clear(machine);
            }

            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "After playing for a while, pause starting new games for a short break. Current games can still finish.");

        if (!settings.EnableFakeBreak)
        {
            return;
        }

        if (GoldSaucerArcadeFakeBreak.TryGetStatusLine(machine, out var status))
        {
            ImGui.TextDisabled(status);
        }

        ImGui.SetNextItemWidth(120f);
        var playMinutes = settings.FakeBreakPlayMinutes;
        if (ImGui.DragInt("How long before taking a break (in minutes)", ref playMinutes, 1f, 1, 24 * 60))
        {
            settings.FakeBreakPlayMinutes = playMinutes;
            C.Save();
        }

        ImGui.SetNextItemWidth(120f);
        var breakMinutes = settings.FakeBreakMinutes;
        if (ImGui.DragInt("How long is the break (in minutes)", ref breakMinutes, 1f, 1, 120))
        {
            settings.FakeBreakMinutes = breakMinutes;
            C.Save();
        }
    }
}
