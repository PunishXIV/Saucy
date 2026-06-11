#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.UI;

internal static class TriadDeckOptimizerJobs
{
    private static readonly object Sync = new();
    private static readonly TimeSpan StalePausedCancelAfter = TimeSpan.FromMinutes(3);
    private static int passId;
    private static TriadDeckOptimizerJob activeJob;

    public static bool InProgressAny
    {
        get
        {
            lock (Sync)
            {
                return IsJobRunningLocked(activeJob);
            }
        }
    }

    public static bool TryGetActive(out TriadDeckOptimizerJobSnapshot snapshot)
    {
        lock (Sync)
        {
            if (!IsJobRunningLocked(activeJob))
            {
                snapshot = default;
                return false;
            }

            snapshot = activeJob.ToSnapshot();
            return true;
        }
    }

    public static bool IsRunningForSessionKey(string sessionKey)
    {
        lock (Sync)
        {
            return IsJobRunningLocked(activeJob) &&
                   string.Equals(activeJob.SessionKey, sessionKey, StringComparison.Ordinal);
        }
    }

    public static bool TryGetActiveForNpc(int npcId, out TriadDeckOptimizerJobSnapshot snapshot)
    {
        lock (Sync)
        {
            if (!IsJobRunningLocked(activeJob) || activeJob.Npc?.Id != npcId)
            {
                snapshot = default;
                return false;
            }

            snapshot = activeJob.ToSnapshot();
            return true;
        }
    }

    public static bool TryGetActivePassIdForNpc(int npcId, out int passId)
    {
        lock (Sync)
        {
            if (!IsJobRunningLocked(activeJob) || activeJob.Npc?.Id != npcId)
            {
                passId = 0;
                return false;
            }

            passId = activeJob.PassId;
            return true;
        }
    }

    public static void CancelActive(bool userCancelled = false, bool markTimedOut = false)
    {
        lock (Sync)
        {
            if (activeJob == null)
            {
                return;
            }

            if (userCancelled)
            {
                activeJob.UserCancelled = true;
            }

            if (markTimedOut)
            {
                activeJob.TimedOut = true;
            }

            CancelJobLocked(activeJob, markTimedOut);
        }
    }

    public static bool TryStart(
        TriadDeckOptimizerStartRequest request,
        out int startedPassId,
        out bool alreadyRunningSameKey)
    {
        startedPassId = 0;
        alreadyRunningSameKey = false;

        lock (Sync)
        {
            if (IsJobRunningLocked(activeJob) &&
                string.Equals(activeJob.SessionKey, request.SessionKey, StringComparison.Ordinal))
            {
                alreadyRunningSameKey = true;
                startedPassId = activeJob.PassId;
                return false;
            }

            CancelJobLocked(activeJob, false);

            var timeoutMinutes = Math.Clamp(request.TimeoutMinutes, 1, 15);
            var jobPassId = ++passId;
            var optimizer = new TriadDeckOptimizer();
            var cts = request.NavigationRequest
                ? new()
                : new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
            optimizer.OnFoundDeck += (deck, estWinChance) =>
            {
                lock (Sync)
                {
                    if (activeJob?.PassId == jobPassId)
                    {
                        activeJob.BestEstWinChance = estWinChance;
                        activeJob.LatestCandidateDeck = deck;
                    }
                }
            };

            var job = new TriadDeckOptimizerJob
            {
                PassId = jobPassId,
                SessionKey = request.SessionKey,
                Npc = request.Npc,
                NavigationRequest = request.NavigationRequest,
                RegionMods = request.RegionMods,
                LockedCards = request.LockedCards,
                StartUtc = DateTime.UtcNow,
                LastStatsPollUtc = DateTime.UtcNow,
                TimeoutMinutes = timeoutMinutes,
                Cts = cts,
                Optimizer = optimizer,
                TimedOut = false,
                UserCancelled = false
            };

            activeJob = job;

            if (!request.NavigationRequest)
            {
                cts.Token.Register(() =>
                {
                    lock (Sync)
                    {
                        if (activeJob?.PassId == jobPassId)
                        {
                            activeJob.TimedOut = true;
                        }
                    }

                    try
                    {
                        optimizer.AbortProcess();
                    }
                    catch
                    {
                        // ignored
                    }
                });
            }

            optimizer.Initialize(request.Npc, request.RegionMods, request.LockedCards);
            startedPassId = jobPassId;

            _ = optimizer.Process(request.Npc, request.RegionMods, request.LockedCards)
                .ContinueWith(
                    _ => FinishOnThreadPool(jobPassId),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
        }

        return true;
    }

    public static void UpdatePause(bool paused)
    {
        lock (Sync)
        {
            if (activeJob == null)
            {
                return;
            }

            activeJob.Optimizer.SetPaused(paused);
            activeJob.PausedSinceUtc = paused ? activeJob.PausedSinceUtc ?? DateTime.UtcNow : null;
        }
    }

    public static void Tick()
    {
        lock (Sync)
        {
            if (!IsJobRunningLocked(activeJob))
            {
                return;
            }

            if (TriadUiState.IsBoardVisible() && !activeJob.NavigationRequest)
            {
                CancelActive(false, true);
                return;
            }

            if (!activeJob.Optimizer.IsPaused)
            {
                activeJob.PausedSinceUtc = null;
                return;
            }

            activeJob.PausedSinceUtc ??= DateTime.UtcNow;
            if (DateTime.UtcNow - activeJob.PausedSinceUtc.Value < StalePausedCancelAfter)
            {
                return;
            }

            Svc.Log.Warning(
                "[Saucy] Cancelling deck optimizer after {Minutes} min paused (solver/navmesh busy).",
                StalePausedCancelAfter.TotalMinutes);
            CancelActive(markTimedOut: true);
        }
    }

    private static void FinishOnThreadPool(int finishedPassId)
    {
        TriadDeckOptimizerJob job;
        TriadDeckOptimizerResult result;

        lock (Sync)
        {
            if (activeJob == null || activeJob.PassId != finishedPassId)
            {
                return;
            }

            job = activeJob;
            job.CompletionSignaled = true;
            activeJob = null;
            QueueOpeningEvalLocked(job);
            result = BuildResultLocked(job);
        }

        Svc.Framework.Run(() => TriadRun.OnDeckOptimizerJobFinished(result));
    }

    private static TriadDeckOptimizerResult BuildResultLocked(TriadDeckOptimizerJob job)
    {
        var timedOut = job.TimedOut || job.Cts.IsCancellationRequested;
        var userCancelled = job.UserCancelled;
        var aborted = job.Optimizer.IsAborted();
        var deck = job.Optimizer.optimizedDeck;
        if ((deck == null || deck.GetDeckState() != ETriadDeckState.Valid) &&
            job.LatestCandidateDeck != null &&
            job.LatestCandidateDeck.GetDeckState() == ETriadDeckState.Valid)
        {
            deck = job.LatestCandidateDeck;
        }

        if (!aborted && (deck == null || deck.GetDeckState() != ETriadDeckState.Valid))
        {
            job.Optimizer.GuessDeck(job.LockedCards);
            deck = job.Optimizer.optimizedDeck;
        }

        var validDeck = deck != null && deck.GetDeckState() == ETriadDeckState.Valid;

        return new(
            job.PassId,
            job.SessionKey,
            job.Npc,
            job.NavigationRequest,
            timedOut,
            userCancelled,
            aborted,
            validDeck ? deck : null,
            job.BestEstWinChance);
    }

    private static bool IsJobRunningLocked(TriadDeckOptimizerJob job) =>
        job != null && !job.CompletionSignaled;

    private static void CancelJobLocked(TriadDeckOptimizerJob job, bool markTimedOut)
    {
        if (job == null || job.CompletionSignaled)
        {
            return;
        }

        if (markTimedOut)
        {
            job.TimedOut = true;
        }

        try
        {
            job.Cts.Cancel();
        }
        catch
        {
            // ignored
        }

        job.Optimizer.AbortProcess();
    }

    private static void QueueOpeningEvalLocked(TriadDeckOptimizerJob job)
    {
        var debounceVersion = ++job.OpeningEvalDebounceVersion;
        var passId = job.PassId;
        var npc = job.Npc;
        var regionMods = job.RegionMods;

        _ = Task.Run(async () =>
        {
            await Task.Delay(500).ConfigureAwait(false);

            TriadDeck deckToEval;
            lock (Sync)
            {
                if (activeJob?.PassId != passId ||
                    job.OpeningEvalDebounceVersion != debounceVersion ||
                    job.LatestCandidateDeck == null)
                {
                    return;
                }

                deckToEval = job.LatestCandidateDeck;
                job.OpeningEvalInFlight = true;
            }

            try
            {
                if (TriadUiState.IsBoardVisible())
                {
                    return;
                }

                var chance = TriadDeckEvaluator.EvaluateOpeningMoveThrottled(deckToEval, npc, regionMods);
                lock (Sync)
                {
                    if (activeJob?.PassId != passId || job.OpeningEvalDebounceVersion != debounceVersion)
                    {
                        return;
                    }

                    if (job.BestOpeningChance.numGames <= 0 || chance.IsBetterThan(job.BestOpeningChance))
                    {
                        job.BestOpeningChance = chance;
                    }
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[Saucy] Optimizer opening eval failed for {Npc}", npc?.Name ?? "NPC");
            }
            finally
            {
                lock (Sync)
                {
                    if (activeJob?.PassId == passId && job.OpeningEvalDebounceVersion == debounceVersion)
                    {
                        job.OpeningEvalInFlight = false;
                    }
                }
            }
        });
    }

    private sealed class TriadDeckOptimizerJob
    {
        public float? BestEstWinChance;
        public SolverResult BestOpeningChance;
        public bool CompletionSignaled;
        public CancellationTokenSource Cts;
        public DateTime LastStatsPollUtc;
        public TriadDeck LatestCandidateDeck;
        public List<TriadCard> LockedCards;
        public bool NavigationRequest;
        public TriadNpc Npc;
        public int OpeningEvalDebounceVersion;
        public bool OpeningEvalInFlight;
        public TriadDeckOptimizer Optimizer;
        public int PassId;
        public DateTime? PausedSinceUtc;
        public TriadGameModifier[] RegionMods;
        public string SessionKey = string.Empty;
        public DateTime StartUtc;
        public bool TimedOut;
        public int TimeoutMinutes;
        public bool UserCancelled;

        public TriadDeckOptimizerJobSnapshot ToSnapshot()
        {
            var optimizer = Optimizer;
            var progress = optimizer?.GetProgress() ?? 0;
            var numOwned = PlayerSettingsDB.Get().ownedCards.Count;
            var numPossible = optimizer?.GetNumPossibleDecksDesc() ?? "0";
            var numTested = optimizer?.GetNumTestedDesc() ?? "0";

            var now = DateTime.UtcNow;
            var elapsedMs = (int)Math.Min(int.MaxValue, (now - LastStatsPollUtc).TotalMilliseconds);
            LastStatsPollUtc = now;
            var secondsRemaining = optimizer != null && elapsedMs > 0
                ? optimizer.GetSecondsRemaining(elapsedMs)
                : int.MaxValue;

            return new(
                SessionKey,
                Npc?.Name ?? "NPC",
                Npc?.Id ?? 0,
                progress,
                BestEstWinChance,
                BestOpeningChance,
                OpeningEvalInFlight,
                numOwned,
                numPossible,
                numTested,
                secondsRemaining,
                StartUtc,
                TimeoutMinutes,
                TimedOut,
                UserCancelled);
        }
    }
}

internal readonly struct TriadDeckOptimizerStartRequest
(
    string sessionKey,
    TriadNpc npc,
    TriadGameModifier[] regionMods,
    List<TriadCard> lockedCards,
    bool navigationRequest,
    int timeoutMinutes)
{
    public string SessionKey { get; } = sessionKey;
    public TriadNpc Npc { get; } = npc;
    public TriadGameModifier[] RegionMods { get; } = regionMods;
    public List<TriadCard> LockedCards { get; } = lockedCards;
    public bool NavigationRequest { get; } = navigationRequest;
    public int TimeoutMinutes { get; } = timeoutMinutes;
}

internal readonly struct TriadDeckOptimizerJobSnapshot
(
    string sessionKey,
    string npcName,
    int npcId,
    int progressPercent,
    float? bestEstWinChance,
    SolverResult bestOpeningChance,
    bool openingEvalInFlight,
    int numOwnedCards,
    string numPossibleDecksDesc,
    string numTestedDecksDesc,
    int secondsRemaining,
    DateTime startUtc,
    int timeoutMinutes,
    bool timedOut,
    bool userCancelled)
{
    public string SessionKey { get; } = sessionKey;
    public string NpcName { get; } = npcName;
    public int NpcId { get; } = npcId;
    public int ProgressPercent { get; } = progressPercent;
    public float? BestEstWinChance { get; } = bestEstWinChance;
    public SolverResult BestOpeningChance { get; } = bestOpeningChance;
    public bool OpeningEvalInFlight { get; } = openingEvalInFlight;
    public int NumOwnedCards { get; } = numOwnedCards;
    public string NumPossibleDecksDesc { get; } = numPossibleDecksDesc;
    public string NumTestedDecksDesc { get; } = numTestedDecksDesc;
    public int SecondsRemaining { get; } = secondsRemaining;
    public DateTime StartUtc { get; } = startUtc;
    public int TimeoutMinutes { get; } = timeoutMinutes;
    public bool TimedOut { get; } = timedOut;
    public bool UserCancelled { get; } = userCancelled;

    public string FormatBestWinChance()
    {
        var opening = TriadDeckEvalDisplay.FormatWinChanceLabel(BestOpeningChance);
        if (!string.IsNullOrEmpty(opening))
        {
            return opening;
        }

        if (OpeningEvalInFlight)
        {
            return "…";
        }

        return BestEstWinChance is float chance ? $"{chance * 100f:F0}%" : null;
    }

    public string FormatTimeLeftDesc()
    {
        if (ProgressPercent <= 0 || ProgressPercent >= 100 || SecondsRemaining == int.MaxValue)
        {
            return "--";
        }

        var tspan = TimeSpan.FromSeconds(SecondsRemaining);
        if (tspan.Hours > 0 || tspan.Minutes > 55)
        {
            return $"{tspan.Hours:D2}h:{tspan.Minutes:D2}m:{tspan.Seconds:D2}s";
        }

        if (tspan.Minutes > 0 || tspan.Seconds > 55)
        {
            return $"{tspan.Minutes:D2}m:{tspan.Seconds:D2}s";
        }

        return $"{tspan.Seconds:D2}s";
    }
}

internal readonly struct TriadDeckOptimizerResult
(
    int passId,
    string sessionKey,
    TriadNpc npc,
    bool navigationRequest,
    bool timedOut,
    bool userCancelled,
    bool aborted,
    TriadDeck deck,
    float? bestEstWinChance)
{
    public int PassId { get; } = passId;
    public string SessionKey { get; } = sessionKey;
    public TriadNpc Npc { get; } = npc;
    public bool NavigationRequest { get; } = navigationRequest;
    public bool TimedOut { get; } = timedOut;
    public bool UserCancelled { get; } = userCancelled;
    public bool Aborted { get; } = aborted;
    public TriadDeck Deck { get; } = deck;
    public float? BestEstWinChance { get; } = bestEstWinChance;
}
