using System;
using System.Collections.Generic;
using System.Linq;
namespace Saucy.TripleTriad;

internal static unsafe class TriadCardFarmSession
{
    private const int DisplaySyncIntervalMs = 500;
    private const int OwnershipRefreshIntervalMs = 1000;
    public static Dictionary<uint, int> TempCardsWonList = [];

    private static int lastTargetNpcId = -1;
    private static int pendingCardDropVerifyFrames;
    private static int pendingCardDropVerifyAttemptsLeft;
    private static uint pendingCardDropVerifyItemId;
    private static readonly HashSet<int> farmDropsCountedThisMatch = [];
    private static bool anyListedFarmTargetDropped;
    private static DateTime lastDisplaySyncUtc = DateTime.MinValue;
    private static DateTime lastOwnershipRefreshUtc = DateTime.MinValue;

    public static bool SessionActive { get; private set; }

    public static bool IsModeActive() => TriadRunSession.ModuleEnabled && TriadRunSession.PlayUntilAllCardsDropOnce;

    public static bool IsComplete()
    {
        var npcInfo = TriadRunTarget.Resolve();
        if (npcInfo == null)
        {
            return false;
        }

        if (C.OnlyUnobtainedCards)
        {
            if (TempCardsWonList.Count > 0)
            {
                return TempCardsWonList.Values.All(wins => wins >= 1);
            }

            if (anyListedFarmTargetDropped)
            {
                return true;
            }

            return !HasUnobtainedNpcRewards(npcInfo);
        }

        if (TempCardsWonList.Count == 0)
        {
            return HasAllNpcRewardsOwned(npcInfo);
        }

        return TempCardsWonList.Values.All(wins => wins >= 1);
    }

    public static int GetCompletedCount()
    {
        var completed = 0;
        foreach (var wins in TempCardsWonList.Values)
        {
            if (wins >= 1)
            {
                completed++;
            }
        }

        return completed;
    }

    public static bool HasPendingDrops()
    {
        if (!SessionActive && !IsModeActive())
        {
            return false;
        }

        return !IsComplete();
    }

    public static void TickDisplaySync()
    {
        if (!TriadRunSession.PlayUntilAllCardsDropOnce)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - lastDisplaySyncUtc).TotalMilliseconds < DisplaySyncIntervalMs)
        {
            return;
        }

        lastDisplaySyncUtc = now;
        EnsureArmed();
        SyncDisplay(TriadRunTarget.Resolve());
    }

    public static void TickVerificationCycle()
    {
        var wasPending = IsDropVerificationPending();
        if (TickDropVerification())
        {
            DetectAndProcessDrops(pendingCardDropVerifyItemId);
        }

        if (wasPending && !IsDropVerificationPending() &&
            IsModeActive() && TriadUiState.IsResultVisible())
        {
            FinalizeResultIfReady();
        }
    }

    public static void EnsureArmed()
    {
        if (!TriadRunSession.ModuleEnabled || !TriadRunSession.PlayUntilAllCardsDropOnce)
        {
            return;
        }

        SessionActive = true;
        if (TempCardsWonList.Count == 0)
        {
            StartTargets(TriadRunTarget.Resolve());
        }
    }

    public static void ActivateSession(GameNpcInfo? npcInfo = null, bool resetProgress = false)
    {
        SessionActive = true;
        TriadRun.EnsureRunTargetNpcSynced();
        var targetNpc = npcInfo ?? TriadRunTarget.Resolve();
        if (resetProgress || TempCardsWonList.Count == 0)
        {
            StartTargets(targetNpc);
        }
        else
        {
            SyncDisplay(targetNpc);
        }
    }

    public static void DeactivateSession(bool clearProgress = false)
    {
        SessionActive = false;
        if (clearProgress)
        {
            ClearProgress();
        }
    }

    public static void ClearProgress()
    {
        TempCardsWonList.Clear();
        lastTargetNpcId = -1;
        anyListedFarmTargetDropped = false;
        farmDropsCountedThisMatch.Clear();
    }

    public static void ClearMatchDropCounts() => farmDropsCountedThisMatch.Clear();

    public static void ResetDropVerification() => ClearDropVerification();

    public static void StartTargets(GameNpcInfo? npcInfo)
    {
        if (npcInfo == null)
        {
            return;
        }

        lastTargetNpcId = npcInfo.npcId;
        TempCardsWonList.Clear();
        anyListedFarmTargetDropped = false;

        RefreshOwnershipCache(force: true);
        foreach (var cardId in npcInfo.rewardCards)
        {
            if (GameCardDB.Get().FindById(cardId) == null)
            {
                continue;
            }

            if (C.OnlyUnobtainedCards && TriadMemoryReads.TryIsCardOwned(cardId))
            {
                continue;
            }

            TempCardsWonList[(uint)cardId] = 0;
        }

        farmDropsCountedThisMatch.Clear();
    }

    public static bool IsDropVerificationPending() => pendingCardDropVerifyAttemptsLeft > 0;

    public static bool ShouldScheduleDropVerification(uint resultRewardItemId)
    {
        if (resultRewardItemId == 0 || !IsModeActive())
        {
            return false;
        }

        return ResolveFarmCardForRewardItem(resultRewardItemId) != null;
    }

    public static void DetectAndProcessDrops(uint resultRewardItemId = 0)
    {
        if (TempCardsWonList.Count == 0)
        {
            return;
        }

        RefreshOwnershipCache();

        if (resultRewardItemId > 0)
        {
            var hinted = ResolveFarmCardForRewardItem(resultRewardItemId);
            if (hinted != null && TryProcessDrop(hinted, resultRewardItemId))
            {
                return;
            }
        }

        if (!TriadRewardDropTracker.MatchRewardOwnershipSnapshotted)
        {
            return;
        }

        foreach (var cardId in TempCardsWonList.Keys.ToList())
        {
            if (TriadRewardDropTracker.OwnedRewardCardsAtMatchStart.Contains((int)cardId))
            {
                continue;
            }

            if (!TriadMemoryReads.TryIsCardOwned((int)cardId))
            {
                continue;
            }

            var cardInfo = GameCardDB.Get().FindById((int)cardId);
            if (cardInfo != null && TryProcessDrop(cardInfo, resultRewardItemId))
            {
                return;
            }
        }
    }

    public static bool TryProcessDrop(GameCardInfo cardInfo, uint resultRewardItemId = 0)
    {
        if (!IsFarmTargetCard(cardInfo.CardId))
        {
            return false;
        }

        if (farmDropsCountedThisMatch.Contains(cardInfo.CardId))
        {
            return false;
        }

        if (resultRewardItemId > 0 && MatchesResultRewardItem(cardInfo, resultRewardItemId))
        {
            RecordDrop(cardInfo);
            return true;
        }

        if (!TriadRewardDropTracker.MatchRewardOwnershipSnapshotted ||
            TriadRewardDropTracker.OwnedRewardCardsAtMatchStart.Contains(cardInfo.CardId))
        {
            return false;
        }

        if (!TriadMemoryReads.TryIsCardOwned(cardInfo.CardId))
        {
            return false;
        }

        RecordDrop(cardInfo);
        return true;
    }

    public static void ScheduleDropVerification(uint resultRewardItemId)
    {
        if (!ShouldScheduleDropVerification(resultRewardItemId))
        {
            return;
        }

        var hinted = ResolveFarmCardForRewardItem(resultRewardItemId);
        if (hinted != null && farmDropsCountedThisMatch.Contains(hinted.CardId))
        {
            return;
        }

        pendingCardDropVerifyFrames = 5;
        pendingCardDropVerifyAttemptsLeft = 30;
        pendingCardDropVerifyItemId = resultRewardItemId;
    }

    public static bool TickDropVerification()
    {
        if (pendingCardDropVerifyAttemptsLeft <= 0)
        {
            return false;
        }

        if (--pendingCardDropVerifyFrames > 0)
        {
            return false;
        }

        pendingCardDropVerifyFrames = 5;
        pendingCardDropVerifyAttemptsLeft--;

        if (pendingCardDropVerifyItemId > 0)
        {
            var hinted = ResolveFarmCardForRewardItem(pendingCardDropVerifyItemId);
            if (hinted != null && farmDropsCountedThisMatch.Contains(hinted.CardId))
            {
                pendingCardDropVerifyAttemptsLeft = 0;
                return false;
            }
        }

        return TempCardsWonList.Count > 0;
    }

    public static void SyncDisplay(GameNpcInfo? npcInfo)
    {
        if (!TriadRunSession.PlayUntilAllCardsDropOnce || npcInfo == null)
        {
            return;
        }

        if (lastTargetNpcId != npcInfo.npcId)
        {
            StartTargets(npcInfo);
            return;
        }

        if (C.OnlyUnobtainedCards)
        {
            PruneOwnedTargetsFromList();
            if (TempCardsWonList.Count == 0 && HasUnobtainedNpcRewards(npcInfo))
            {
                StartTargets(npcInfo);
            }
        }
        else if (TempCardsWonList.Count == 0 && !HasAllNpcRewardsOwned(npcInfo))
        {
            StartTargets(npcInfo);
        }
    }

    public static bool HasAllNpcRewardsOwned(GameNpcInfo npcInfo) => !HasUnobtainedNpcRewards(npcInfo);

    public static bool HasUnobtainedNpcRewards(GameNpcInfo npcInfo)
    {
        if (npcInfo.rewardCards.Count == 0)
        {
            return false;
        }

        foreach (var cardId in npcInfo.rewardCards)
        {
            if (GameCardDB.Get().FindById(cardId) == null)
            {
                continue;
            }

            if (!TriadMemoryReads.TryIsCardOwned(cardId))
            {
                return true;
            }
        }

        return false;
    }

    private static void RefreshOwnershipCache(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - lastOwnershipRefreshUtc).TotalMilliseconds < OwnershipRefreshIntervalMs)
        {
            return;
        }

        lastOwnershipRefreshUtc = now;
        GameCardDB.Get().Refresh();
    }

    private static bool IsFarmTargetCard(int cardId) => TempCardsWonList.ContainsKey((uint)cardId);

    private static void PruneOwnedTargetsFromList()
    {
        foreach (var cardId in TempCardsWonList.Keys.ToList())
        {
            if (TriadMemoryReads.TryIsCardOwned((int)cardId))
            {
                TempCardsWonList.Remove(cardId);
            }
        }
    }

    private static bool MatchesResultRewardItem(GameCardInfo cardInfo, uint resultRewardItemId)
    {
        if (resultRewardItemId == 0 || !IsFarmTargetCard(cardInfo.CardId))
        {
            return false;
        }

        if (cardInfo.ItemId == resultRewardItemId)
        {
            return true;
        }

        if (cardInfo.ItemId != 0)
        {
            return false;
        }

        var hinted = GameCardDB.Get().FindByItemId(resultRewardItemId);
        return hinted != null && hinted.CardId == cardInfo.CardId;
    }

    private static GameCardInfo? ResolveFarmCardForRewardItem(uint resultRewardItemId)
    {
        if (resultRewardItemId == 0)
        {
            return null;
        }

        var hinted = GameCardDB.Get().FindByItemId(resultRewardItemId);
        if (hinted != null && IsFarmTargetCard(hinted.CardId))
        {
            return hinted;
        }

        foreach (var cardId in TempCardsWonList.Keys)
        {
            var info = GameCardDB.Get().FindById((int)cardId);
            if (info != null && info.ItemId == resultRewardItemId)
            {
                return info;
            }
        }

        return null;
    }

    private static void RecordDrop(GameCardInfo cardInfo)
    {
        farmDropsCountedThisMatch.Add(cardInfo.CardId);
        anyListedFarmTargetDropped = true;
        TempCardsWonList[(uint)cardInfo.CardId] = TempCardsWonList[(uint)cardInfo.CardId] + 1;
        ClearDropVerification();

        C.UpdateStats(stats =>
        {
            stats.CardsDroppedWithSaucy++;

            if (stats.CardsWon.ContainsKey((uint)cardInfo.CardId))
            {
                stats.CardsWon[(uint)cardInfo.CardId] += 1;
            }
            else
            {
                stats.CardsWon[(uint)cardInfo.CardId] = 1;
            }
        });
        C.Save();
        FinalizeResultIfReady();
    }

    private static void ClearDropVerification()
    {
        pendingCardDropVerifyAttemptsLeft = 0;
        pendingCardDropVerifyFrames = 0;
        pendingCardDropVerifyItemId = 0;
    }

    private static void FinalizeResultIfReady()
    {
        if (!TriadRunSession.ModuleEnabled || !IsModeActive() || IsDropVerificationPending())
        {
            return;
        }

        if (!TriadUiState.IsResultVisible())
        {
            return;
        }

        if (IsComplete())
        {
            TriadRematchAutomation.ClearRematchPending();
            DeactivateSession();
            TriadRematchAutomation.RequestSessionEndDismiss();
            Svc.Framework.Run(TriadRematchAutomation.TryDismissResultIfSessionEnded);
            return;
        }

        if (!TriadLocalClientStructs.TryGetResult(out var resultAddon))
        {
            return;
        }

        if (!TriadRematchAutomation.IsResultMatchRecorded((nint)resultAddon))
        {
            TriadRematchAutomation.RecordMatchResultIfNeeded((nint)resultAddon);
        }
        else if (!TriadRematchAutomation.RematchPending)
        {
            TriadRematchAutomation.RequestRematch();
        }
    }
}
