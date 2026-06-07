using System.Collections.Generic;
using System.Linq;
namespace Saucy.TripleTriad.GameLogic;

internal static class TriadOptimizedDeckCacheValidator
{
    public static int[] CaptureOwnedCardSnapshot()
    {
        GameCardDB.Get().Refresh();
        return
        [
            .. PlayerSettingsDB.Get().ownedCards
                .Select(card => card?.Id ?? 0)
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
        ];
    }

    public static int CountNewOwnedCardsSinceBuild(string sessionKey, int npcId)
    {
        GameCardDB.Get().Refresh();
        var owned = PlayerSettingsDB.Get().ownedCards;
        if (owned.Count == 0)
        {
            return 0;
        }

        if (!TriadOptimizedDeckCacheStore.TryGetOwnedSnapshotForNpc(npcId, sessionKey, out var ownedAtBuild))
        {
            return 0;
        }

        var ownedIds = new HashSet<int>(owned.Select(card => card.Id));
        return CountNewOwnedCards(ownedAtBuild, ownedIds);
    }

    public static bool ShouldRebuildDeckForNewCards(string sessionKey, int npcId) =>
        CountNewOwnedCardsSinceBuild(sessionKey, npcId) >= TriadOptimizedDeckCacheStore.RebuildAfterNewCardCount;

    private static int CountNewOwnedCards(int[] ownedAtBuild, IReadOnlySet<int> currentOwnedIds)
    {
        if (ownedAtBuild == null || ownedAtBuild.Length == 0)
        {
            return 0;
        }

        var snapshot = new HashSet<int>(ownedAtBuild);
        var count = 0;
        foreach (var id in currentOwnedIds)
        {
            if (!snapshot.Contains(id))
            {
                count++;
            }
        }

        return count;
    }

    public static bool TryGetUsableEntry(string sessionKey, out TriadOptimizedDeckCacheEntry? entry)
    {
        entry = null;

        if (!TriadOptimizedDeckCacheStore.TryGetEntry(sessionKey, out var cached))
        {
            return false;
        }

        if (!HasValidCardIds(cached!.CardIds))
        {
            TriadOptimizedDeckCacheStore.Remove(sessionKey);
            return false;
        }

        GameCardDB.Get().Refresh();
        var owned = PlayerSettingsDB.Get().ownedCards;
        if (owned.Count == 0)
        {
            return false;
        }

        var ownedIds = new HashSet<int>(owned.Select(card => card.Id));
        foreach (var cardId in cached.CardIds)
        {
            if (!ownedIds.Contains(cardId))
            {
                TriadOptimizedDeckCacheStore.Remove(sessionKey);
                return false;
            }
        }

        if (!TryBuildSolverDeck(cached.CardIds, out var _))
        {
            TriadOptimizedDeckCacheStore.Remove(sessionKey);
            return false;
        }

        entry = cached;
        return true;
    }

    private static bool HasValidCardIds(ushort[] cardIds)
    {
        if (cardIds == null || cardIds.Length != TriadOptimizedDeckCacheEntry.DeckSize)
        {
            return false;
        }

        return cardIds.All(id => id > 0);
    }

    public static bool TryBuildSolverDeck(ushort[] cardIds, out TriadDeck? deck)
    {
        deck = null;
        var parseCtx = new GameUIParser();
        var cards = new TriadCard[TriadOptimizedDeckCacheEntry.DeckSize];
        for (var idx = 0; idx < TriadOptimizedDeckCacheEntry.DeckSize; idx++)
        {
            var card = parseCtx.cards.FindById(cardIds[idx]);
            if (card == null)
            {
                parseCtx.OnFailedCard($"id:{cardIds[idx]}");
                return false;
            }

            cards[idx] = card;
        }

        if (parseCtx.HasErrors)
        {
            return false;
        }

        deck = new(cards);
        return deck.GetDeckState() == ETriadDeckState.Valid;
    }
}
