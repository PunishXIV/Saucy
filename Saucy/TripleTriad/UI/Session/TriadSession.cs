#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    private readonly Lock _preGameLock = new();
    private readonly HashSet<string> _previewEvalInFlight = [];

    private readonly Dictionary<int, List<TriadGameModifier>> _rememberedRegionalModsByNpcId = new();
    private string _cachedDeckSlottedSessionKey = string.Empty;

    public TriadNpc currentNpc;

    private int _deckSelectPostWriteCooldownFrames;
    private float? _deferredPostMatchEstWinChance;
    private TriadDeck _deferredPostMatchOptimizedDeck;
    public bool hasMove;

    private int _lastAppliedRunTargetNpcId = -1;

    public TriadNpc lastGameNpc;
    private string _lastOptimizerSkipKey = string.Empty;
    public int moveBoardIdx;
    public int moveCardIdx;
    private DateTime? _moveReadyUtc;
    private int _navigationOptimizerRetryCount;
    private string _navigationOptimizerRetrySessionKey = string.Empty;

    public Dictionary<string, Dictionary<int, DeckData>> npcEvalSnapshots = [];
    private int _optimizerPassId;
    private string _optimizerSessionKey = string.Empty;
    private string _optimizerStartBlockedSessionKey = string.Empty;
    private DateTime _optimizerStartBlockedUntilUtc = DateTime.MinValue;
    private int _optimizerTargetDeckId = -1;
    private bool _optimizerTimedOut;
    private bool _pauseOptimizerForActiveTriad;
    private bool _pauseOptimizerForNavmesh;
    private bool _pauseOptimizerForSolver;

    public int preGameBestId = -1;
    public Dictionary<int, DeckData> preGameDecks = [];
    public List<TriadGameModifier> preGameMods = [];

    public TriadNpc preGameNpc;
    private int _previewEvalGeneration;

    public TriadProfileDeckReader profileGS;

    public Status status;

    public TriadSession() => TriadGameSimulation.StaticInitialize();

    public bool OptimizerInProgress => TriadDeckOptimizerJobs.InProgressAny;

    public TriadGameScreenMemory DebugScreenMemory { get; } = new();
    public bool HasOptimizedDeckApplied { get; private set; }

    public int OptimizedDeckSlotId => HasOptimizedDeckApplied ? _optimizerTargetDeckId : -1;
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
        lock (_preGameLock)
        {
            if (ShouldBuildOptimizedDeck() && HasOptimizedDeckApplied && _optimizerTargetDeckId >= 0)
            {
                deckId = _optimizerTargetDeckId;
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
        lock (_preGameLock)
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

        if (!_optimizerTimedOut && !OptimizerInProgress && !HasOptimizedDeckApplied)
        {
            lock (_preGameLock)
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

        if (_optimizerTimedOut && !HasOptimizedDeckApplied)
        {
            return WithFallbackNote("Last build timed out · still generating new…");
        }

        return null;
    }

    public bool IsMoveReadyForPlacement() =>
        !hasMove ||
        (TriadRunSession.ModuleEnabled && !TriadBuddyIntegration.IsLoaded()) ||
        TriadCardFarmSession.IsModeActive() ||
        (_moveReadyUtc.HasValue && DateTime.UtcNow - _moveReadyUtc.Value >= MoveHighlightGracePeriod);

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
