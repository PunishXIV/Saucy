using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
namespace Saucy.TripleTriad;

internal static class TriadDeckOptimizerStatusUi
{
    public static void DrawInline(string? contextLabel = null)
    {
        if (!TriadRun.ShouldBuildOptimizedDeck() ||
            !TriadDeckOptimizerJobs.TryGetActive(out var job))
        {
            return;
        }

        DrawActiveJob(job, contextLabel);
    }

    private static void DrawActiveJob(TriadDeckOptimizerJobSnapshot job, string? contextLabel)
    {
        ImGui.Spacing();

        var header = string.IsNullOrEmpty(contextLabel)
            ? $"Building deck for {job.NpcName}…"
            : $"{contextLabel}: {job.NpcName}";

        SaucyTheme.TextWarning(header);

        var openingLabel = job.FormatBestWinChance();
        if (!string.IsNullOrEmpty(openingLabel))
        {
            ImGui.Text($"Opening win chance: {openingLabel}");
            if (job.OpeningEvalInFlight)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(updating…)");
            }
        }
        else if (job.OpeningEvalInFlight)
        {
            ImGui.TextDisabled("Opening win chance: calculating…");
        }

        var progress = Math.Clamp(job.ProgressPercent, 0, 100) / 100f;
        ImGui.ProgressBar(progress, new Vector2(-1, 0));

        ImGui.TextDisabled($"Cards owned: {job.NumOwnedCards:N0}");
        ImGui.TextDisabled($"Possible decks: {job.NumPossibleDecksDesc}");
        ImGui.TextDisabled($"Tested: {job.NumTestedDecksDesc}");
        ImGui.TextDisabled($"Progress: {job.ProgressPercent}%");
        ImGui.TextDisabled($"Time left: {job.FormatTimeLeftDesc()}");

        if (ImGui.Button("Cancel build", new(-1, 0)))
        {
            TriadRun.CancelDeckOptimizerJob(userCancelled: true);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Stops the current background deck build.");
        }
    }
}
