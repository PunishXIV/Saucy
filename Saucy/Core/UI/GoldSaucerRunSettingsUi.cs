using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Saucy.IPC;
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
        if (ImGui.Checkbox("Fixed match count", ref playFixedCount))
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

        // PauseForAutoRetainer is a global flag and the pause applies to both arcade machines
        // (Cuff-a-Cur and Out on a Limb), so surface the toggle under both, not Cuff only.
        ImGui.Dummy(new(0, 4));
        SaucyTheme.DrawCard("AutoRetainer", "Placed bell must be in range", DrawAutoRetainerIntegration);
    }

    private static void DrawAutoRetainerIntegration()
    {
        var pause = C.PauseForAutoRetainer;
        if (ImGui.Checkbox("Pause when retainers are ready (bell nearby)", ref pause))
        {
            C.PauseForAutoRetainer = pause;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Cuff-a-Cur and Out on a Limb only. When a placed summoning bell is in range " +
            "and AutoRetainer reports retainers ready, Saucy finishes the current game, opens the bell, runs " +
            "waits for Autoretainer to finish, then resumes.");

        if (!C.PauseForAutoRetainer)
        {
            return;
        }

        using var indent = ImRaii.PushIndent();

        if (AutoRetainerPause.IsHandling)
        {
            ImGui.TextDisabled("Waiting for AutoRetainer…");
        }
        else if (AutoRetainerPause.IsBlocking)
        {
            ImGui.TextDisabled("Retainers ready — finishing current game…");
        }
        else if (!AutoRetainerPause.HasBellInRange())
        {
            ImGui.TextDisabled("No placed summoning bell in range.");
        }

        ImGui.Dummy(new(0, 4));
        PluginDependenciesUi.Draw(
            "Optional plugin for retainer venture automation.",
            [AutoRetainerDependency()]);
    }

    private static PluginDependenciesUi.DependencyEntry AutoRetainerDependency() =>
        new(
            "AutoRetainer",
            IPCNames.AutoRetainer,
            "Collects and reassigns retainer ventures. Saucy enables it at the bell automatically.",
            "https://love.puni.sh/ment.json",
            [],
            () => AutoRetainerIpc.IsInstalled);

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
        if (ImGui.DragInt("Play time before break (minutes)", ref playMinutes, 1f, 1, 24 * 60))
        {
            settings.FakeBreakPlayMinutes = playMinutes;
            C.Save();
        }

        ImGui.SetNextItemWidth(120f);
        var breakMinutes = settings.FakeBreakMinutes;
        if (ImGui.DragInt("Break length (minutes)", ref breakMinutes, 1f, 1, 120))
        {
            settings.FakeBreakMinutes = breakMinutes;
            C.Save();
        }
    }
}
