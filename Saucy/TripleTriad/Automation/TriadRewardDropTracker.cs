using System.Collections.Generic;
namespace Saucy.TripleTriad;

internal static class TriadRewardDropTracker
{
    internal static readonly HashSet<int> OwnedRewardCardsAtMatchStart = [];

    internal static bool MatchRewardOwnershipSnapshotted { get; private set; }

    internal static bool PlayUntilAnyCardDropped { get; private set; }

    public static void ResetSessionFlag() => PlayUntilAnyCardDropped = false;

    public static void ResetSnapshot()
    {
        MatchRewardOwnershipSnapshotted = false;
        OwnedRewardCardsAtMatchStart.Clear();
        TriadCardFarmSession.ClearMatchDropCounts();
    }

    public static void SnapshotAtMatchStart()
    {
        TriadCardFarmSession.ClearMatchDropCounts();
        OwnedRewardCardsAtMatchStart.Clear();
        var npc = TriadRunTarget.Resolve();
        if (npc == null)
        {
            MatchRewardOwnershipSnapshotted = false;
            return;
        }

        GameCardDB.Get().Refresh();
        foreach (var cardId in npc.rewardCards)
        {
            if (TriadMemoryReads.TryIsCardOwned(cardId))
            {
                OwnedRewardCardsAtMatchStart.Add(cardId);
            }
        }

        MatchRewardOwnershipSnapshotted = true;
    }

    public static bool TryGetVerifiedNpcCardDrop(out GameCardInfo? droppedCard, uint resultRewardItemId = 0)
    {
        droppedCard = null;
        if (!MatchRewardOwnershipSnapshotted)
        {
            return false;
        }

        GameCardDB.Get().Refresh();
        var npc = TriadRunTarget.Resolve();
        if (npc == null)
        {
            return false;
        }

        if (resultRewardItemId > 0)
        {
            var hinted = GameCardDB.Get().FindByItemId(resultRewardItemId);
            if (hinted != null && npc.rewardCards.Contains(hinted.CardId) &&
                CanCountNpcRewardDrop(hinted.CardId))
            {
                droppedCard = hinted;
                return true;
            }
        }

        foreach (var cardId in npc.rewardCards)
        {
            if (!CanCountNpcRewardDrop(cardId))
            {
                continue;
            }

            droppedCard = GameCardDB.Get().FindById(cardId);
            if (droppedCard != null)
            {
                return true;
            }
        }

        return false;
    }

    public static void ProcessVerifiedCardDrop(GameCardInfo droppedCard)
    {
        if (droppedCard == null)
        {
            return;
        }

        if (TriadCardFarmSession.IsModeActive() &&
            TriadCardFarmSession.TempCardsWonList.ContainsKey((uint)droppedCard.CardId))
        {
            TriadCardFarmSession.TryProcessDrop(droppedCard);
            return;
        }

        if (TriadRunSession.PlayUntilCardDrops)
        {
            NotifyPlayUntilAnyCardDropped();
        }

        C.UpdateStats(stats =>
        {
            stats.CardsDroppedWithSaucy++;

            if (stats.CardsWon.ContainsKey((uint)droppedCard.CardId))
            {
                stats.CardsWon[(uint)droppedCard.CardId] += 1;
            }
            else
            {
                stats.CardsWon[(uint)droppedCard.CardId] = 1;
            }
        });
        C.Save();
    }

    public static void NotifyPlayUntilAnyCardDropped()
    {
        if (!TriadRunSession.PlayUntilCardDrops)
        {
            return;
        }

        PlayUntilAnyCardDropped = true;
    }

    private static bool CanCountNpcRewardDrop(int cardId)
    {
        if (OwnedRewardCardsAtMatchStart.Contains(cardId))
        {
            return false;
        }

        if (!TriadMemoryReads.TryIsCardOwned(cardId))
        {
            return false;
        }

        if (TriadRunSession.PlayUntilAllCardsDropOnce && TriadCardFarmSession.TempCardsWonList.Count > 0 &&
            !TriadCardFarmSession.TempCardsWonList.ContainsKey((uint)cardId))
        {
            return false;
        }

        return true;
    }
}
