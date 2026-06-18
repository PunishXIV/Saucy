#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    public enum Status
    {
        NoErrors,
        FailedToParseCards,
        FailedToParseRules,
        FailedToParseNpc
    }

    private const int SaucyProfileDeckSlotIndex = 4;
    private const int MaxNavigationOptimizerRetries = 2;

    public const int DeckSelectPostProfileWriteFrames = 45;
    private static readonly TimeSpan OptimizedDeckRebuildCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OptimizerStartFailureCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MoveHighlightGracePeriod = TimeSpan.FromMilliseconds(800);
    private static readonly List<TriadCard> UnlockedDeckSlots = [null, null, null, null, null];

    private readonly object preGameLock = new();
    private readonly HashSet<string> previewEvalInFlight = [];

    private readonly Dictionary<int, List<TriadGameModifier>> rememberedRegionalModsByNpcId = new();
    private string cachedDeckSlottedSessionKey = string.Empty;

    public TriadNpc currentNpc;

    private int deckSelectPostWriteCooldownFrames;
    private float? deferredPostMatchEstWinChance;
    private TriadDeck deferredPostMatchOptimizedDeck;
    public bool hasMove;

    private int lastAppliedRunTargetNpcId = -1;

    public TriadNpc lastGameNpc;
    private string lastOptimizerSkipKey = string.Empty;
    public int moveBoardIdx;
    public int moveCardIdx;
    private DateTime? moveReadyUtc;
    private int navigationOptimizerRetryCount;
    private string navigationOptimizerRetrySessionKey = string.Empty;

    public Dictionary<string, Dictionary<int, DeckData>> npcEvalSnapshots = [];
    private int optimizerPassId;
    private string optimizerSessionKey = string.Empty;
    private string optimizerStartBlockedSessionKey = string.Empty;
    private DateTime optimizerStartBlockedUntilUtc = DateTime.MinValue;
    private int optimizerTargetDeckId = -1;
    private bool optimizerTimedOut;
    private bool pauseOptimizerForActiveTriad;
    private bool pauseOptimizerForNavmesh;
    private bool pauseOptimizerForSolver;

    public int preGameBestId = -1;
    public Dictionary<int, DeckData> preGameDecks = [];
    public List<TriadGameModifier> preGameMods = [];

    public TriadNpc preGameNpc;
    private int previewEvalGeneration;

    public TriadProfileDeckReader profileGS;

    public Status status;

    public TriadSession() => TriadGameSimulation.StaticInitialize();

    public bool OptimizerInProgress => TriadDeckOptimizerJobs.InProgressAny;

    public TriadGameScreenMemory DebugScreenMemory { get; } = new();
    public bool HasOptimizedDeckApplied { get; private set; }

    public int OptimizedDeckSlotId => HasOptimizedDeckApplied ? optimizerTargetDeckId : -1;
    public bool HasErrors => status != Status.NoErrors;

    public string GetExpectedSaucyDeckName() =>
        preGameNpc != null ? $"{preGameNpc.Name} (Saucy)" : string.Empty;

    public string GetProfileDeckName(int deckId)
    {
        if (profileGS == null || profileGS.HasErrors)
        {
            return null;
        }

        var profileDecks = profileGS.GetPlayerDecks();
        if (profileDecks == null || deckId < 0 || deckId >= profileDecks.Length)
        {
            return null;
        }

        return profileDecks[deckId]?.name;
    }

    public bool ShouldBuildOptimizedDeck() =>
        C.UseSimmedDeck &&
        (C.AlwaysBuildOptimizedDeck || TriadRunSession.NavigationRequiresOptimizedDeckBuild);

    public bool ShouldUseCachedOptimizedDeckIfAvailable() =>
        C.UseSimmedDeck &&
        C.UseCachedOptimizedDeckIfAvailable &&
        !ShouldBuildOptimizedDeck();

    public string GetAutoPickDeckSummary(TriadNpc npc)
    {
        if (!C.UseSimmedDeck || npc == null)
        {
            return null;
        }

        RefreshPrepRulesFromLive();
        var previewRules = ResolvePreviewRulesForNpc(npc);
        if (TriadUiState.IsBoardVisible())
        {
            if (OptimizerInProgress || IsPreviewEvalPendingForNpc(npc, previewRules))
            {
                return "In match…";
            }
        }
        else if (TriadUiState.IsAutomationFlowActive())
        {
            if (OptimizerInProgress || IsPreviewEvalPendingForNpc(npc, previewRules))
            {
                return OptimizerInProgress && TriadDeckOptimizerJobs.TryGetActive(out var activeJob)
                    ? $"Building deck… {activeJob.ProgressPercent}%"
                    : "Calculating…";
            }
        }

        if (!dataLoader.IsDataReady)
        {
            return "Loading card data…";
        }

        if (CountSimmableProfileDecks() == 0)
        {
            return DescribeMissingSimmableDecks();
        }

        if (ShouldBuildOptimizedDeck())
        {
            var buildStatus = DescribeOptimizedDeckBuildStatus(npc);
            if (!string.IsNullOrEmpty(buildStatus))
            {
                return buildStatus;
            }
        }

        if (IsPreviewEvalPendingForNpc(npc, previewRules))
        {
            return "Calculating…";
        }

        int deckId;
        string deckName;
        DeckData deckData;
        lock (preGameLock)
        {
            if (ShouldBuildOptimizedDeck() && HasOptimizedDeckApplied && optimizerTargetDeckId >= 0)
            {
                deckId = optimizerTargetDeckId;
            }
            else
            {
                deckId = ResolveAutoPickDeckIdLocked(npc);
            }

            if (deckId < 0)
            {
                return null;
            }

            deckData = TryGetDeckPreviewDataLocked(npc, deckId, previewRules);
            deckName = deckData?.name;
            if (string.IsNullOrWhiteSpace(deckName) && profileGS?.GetPlayerDecks() is { } profileDecks &&
                deckId >= 0 && deckId < profileDecks.Length)
            {
                deckName = profileDecks[deckId]?.name;
            }
        }

        var winLabel = deckData != null ? TriadDeckEvalDisplay.FormatWinChanceLabel(deckData) : null;
        if (winLabel == null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(deckName) ? winLabel : $"{winLabel} · {deckName}";
    }

    private string DescribeOptimizedDeckBuildStatus(TriadNpc npc)
    {
        var regionMods = ResolveRegionModsForNpc(npc);
        var sessionKey = BuildOptimizerSessionKey(npc, regionMods);
        var skipCache = TriadOptimizerSessionKey.ShouldSkipDeckCache(npc, regionMods);
        var hasUsableCache = !skipCache &&
                             TriadOptimizedDeckCacheValidator.TryGetUsableEntry(sessionKey, out var _);
        var hasAnyCache = TriadOptimizedDeckCacheStore.HasAnyEntryForNpc(npc.Id);
        var rebuildingForNewCards = hasUsableCache &&
                                    TriadOptimizedDeckCacheValidator.ShouldRebuildDeckForNewCards(sessionKey, npc.Id);

        var hasProfileSaucyDeck = false;
        lock (preGameLock)
        {
            hasProfileSaucyDeck = TryFindSaucyDeckProfileSlot(npc, out var _);
        }

        var hasFallbackDeck = hasUsableCache || hasAnyCache || hasProfileSaucyDeck;
        string WithFallbackNote(string message) =>
            hasFallbackDeck ? $"{message} · cached deck exists" : message;

        if (OptimizerInProgress && TriadDeckOptimizerJobs.TryGetActive(out var job))
        {
            var progress = $"Building deck… {job.ProgressPercent}%";
            var best = job.FormatBestWinChance();
            if (!string.IsNullOrEmpty(best) && best != "…")
            {
                progress += $" ({best})";
            }

            if (rebuildingForNewCards)
            {
                return $"{progress} · rebuilding after new cards";
            }

            if (hasFallbackDeck)
            {
                return $"{progress} · generating new (cached deck exists)";
            }

            return progress;
        }

        if (Vnavmesh.ShouldDeferDeckOptimizerWork())
        {
            return WithFallbackNote("Waiting for vnavmesh…");
        }

        if (!optimizerTimedOut && !OptimizerInProgress && !HasOptimizedDeckApplied)
        {
            lock (preGameLock)
            {
                if (IsOptimizerStartBlockedForSessionLocked(sessionKey))
                {
                    return WithFallbackNote("Optimizer cooling down · still generating new");
                }
            }

            if (rebuildingForNewCards)
            {
                return "Cached deck outdated · generating new…";
            }

            if (hasUsableCache || hasAnyCache)
            {
                return "Cached deck exists · still generating new…";
            }

            if (hasProfileSaucyDeck)
            {
                return $"Profile deck in slot {SaucyProfileDeckSlotIndex + 1} · still generating new…";
            }

            return "Waiting for optimized deck…";
        }

        if (optimizerTimedOut && !HasOptimizedDeckApplied)
        {
            return WithFallbackNote("Last build timed out · still generating new…");
        }

        return null;
    }

    public bool IsMoveReadyForPlacement() =>
        !hasMove ||
        (TriadRunSession.ModuleEnabled && !TriadBuddyIntegration.IsLoaded()) ||
        TriadCardFarmSession.IsModeActive() ||
        (moveReadyUtc.HasValue && DateTime.UtcNow - moveReadyUtc.Value >= MoveHighlightGracePeriod);

    public class DeckData
    {
        public SolverResult chance;
        public int id;
        public string name;
        public TriadDeck solverDeck;
    }
}

internal static class TriadBuddyIntegration
{
    private const string PluginInternalName = "TriadBuddy";

    public static bool IsLoaded() =>
        Svc.PluginInterface.InstalledPlugins.Any(p => p.InternalName == PluginInternalName && p.IsLoaded);
}
