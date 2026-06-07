#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
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
    private int moveCalcGeneration;
    private volatile bool moveCalcInFlight;
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

        if (ShouldBuildOptimizedDeck() && OptimizerInProgress && TriadDeckOptimizerJobs.TryGetActive(out var job))
        {
            var best = job.FormatBestWinChance();
            if (string.IsNullOrEmpty(best) || best == "…")
            {
                return $"Building deck… {job.ProgressPercent}%";
            }

            return $"Building deck… {job.ProgressPercent}% ({best})";
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
            else if (ShouldBuildOptimizedDeck() && Vnavmesh.ShouldDeferHeavyWork())
            {
                return "Waiting for vnavmesh…";
            }
            else if (ShouldBuildOptimizedDeck() && !optimizerTimedOut && !OptimizerInProgress)
            {
                return "Waiting for optimized deck…";
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

    public bool IsMoveReadyForPlacement() =>
        !hasMove ||
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
