using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.Data;

public enum GameCardCollectionFilter
{
    All,
    OnlyOwned,
    OnlyMissing,
    DeckEditDefault
}

public class GameCardInfo
{
    public int CardId;
    public CollectionPos[] Collection = new CollectionPos[4];

    public bool IsOwned;

    public uint ItemId;

    public List<int> RewardNpcs = [];
    public int SaleValue;
    public int SortKey;

    public struct CollectionPos
    {
        public int PageIndex;
        public int CellIndex;
    }
}

public class GameCardDB
{
    public const int MaxGridCells = 30;
    private const int MinGridPages = 20;
    private static readonly GameCardDB instance = new();

    public Dictionary<int, GameCardInfo> mapCards = [];
    private int maxCardId;
    private int maxGridPageIndex;

    public List<int> ownedCardIds = [];

    public static int MaxGridPages => Math.Max(instance.maxGridPageIndex + 1, MinGridPages);

    public static GameCardDB Get() => instance;

    public GameCardInfo? FindById(int cardId)
    {
        if (mapCards.TryGetValue(cardId, out var cardInfo))
        {
            return cardInfo;
        }

        return null;
    }

    public GameCardInfo? FindByItemId(uint itemId)
    {
        if (itemId == 0)
        {
            return null;
        }

        foreach (var kvp in mapCards)
        {
            if (kvp.Value != null && kvp.Value.ItemId == itemId)
            {
                return kvp.Value;
            }
        }

        return null;
    }

    public GameCardInfo? FindByGridLocation(int pageIdx, int cellIdx, int filterMode)
    {
        if (pageIdx < 0 || cellIdx < 0 || filterMode < 0 || filterMode > (int)GameCardCollectionFilter.DeckEditDefault)
        {
            return null;
        }

        foreach (var kvp in mapCards)
        {
            if (kvp.Value != null)
            {
                var pos = kvp.Value.Collection[filterMode];
                if (pos.PageIndex == pageIdx && pos.CellIndex == cellIdx)
                {
                    return kvp.Value;
                }
            }
        }

        return null;
    }

    public GameCardInfo? FindByGridLocationAnyFilter(int pageIdx, int cellIdx, int preferredFilter = 0)
    {
        var match = FindByGridLocation(pageIdx, cellIdx, preferredFilter);
        if (match != null)
        {
            return match;
        }

        for (var filterIdx = 0; filterIdx <= (int)GameCardCollectionFilter.DeckEditDefault; filterIdx++)
        {
            if (filterIdx == preferredFilter)
            {
                continue;
            }

            match = FindByGridLocation(pageIdx, cellIdx, filterIdx);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    public List<DeckEditCardEntry> GetDeckEditCardEntries()
    {
        var result = new List<DeckEditCardEntry>();
        var cardsDB = TriadCardDB.Get();
        var sorted = new List<Tuple<TriadCard, GameCardInfo>>();

        foreach (var kvp in mapCards)
        {
            var cardOb = cardsDB.FindById(kvp.Key);
            if (cardOb != null && cardOb.IsValid() && ownedCardIds.Contains(cardOb.Id))
            {
                sorted.Add(new(cardOb, kvp.Value));
            }
        }

        sorted.Sort((a, b) =>
        {
            if (a.Item1.Rarity != b.Item1.Rarity)
            {
                return a.Item1.Rarity.CompareTo(b.Item1.Rarity);
            }

            if (a.Item2.SortKey != b.Item2.SortKey)
            {
                return a.Item2.SortKey.CompareTo(b.Item2.SortKey);
            }

            return a.Item1.SortOrder.CompareTo(b.Item1.SortOrder);
        });

        var displayNo = 1;
        foreach (var entry in sorted)
        {
            result.Add(new(entry.Item1, entry.Item2, displayNo++));
        }

        return result;
    }

    public void OnLoaded()
    {
        maxCardId = 0;
        foreach (var kvp in mapCards)
        {
            if (maxCardId < kvp.Key)
            {
                maxCardId = kvp.Key;
            }
        }
    }

    public void Refresh()
    {
        ownedCardIds.Clear();
        maxGridPageIndex = 0;

        if (!TriadMemoryReads.IsAvailable || maxCardId <= 0)
        {
            return;
        }

        for (var id = 1; id <= maxCardId; id++)
        {
            if (TriadMemoryReads.TryIsCardOwned(id))
            {
                ownedCardIds.Add(id);
            }
        }

        var cardDB = TriadCardDB.Get();
        var settingsDB = PlayerSettingsDB.Get();

        settingsDB.ownedCards.Clear();
        foreach (var cardOb in cardDB.cards)
        {
            if (cardOb != null && ownedCardIds.Contains(cardOb.Id))
            {
                settingsDB.ownedCards.Add(cardOb);
            }
        }

        foreach (var kvp in mapCards)
        {
            kvp.Value.IsOwned = ownedCardIds.Contains(kvp.Value.CardId);
        }

        RebuildCollectionPages();
        RebuildDeckEditPages();
    }

    private void RebuildCollectionPages()
    {
        var cardsDB = TriadCardDB.Get();

        var sortedTriadCards = new List<TriadCard>();
        foreach (var cardOb in cardsDB.cards)
        {
            if (cardOb != null && cardOb.IsValid())
            {
                sortedTriadCards.Add(cardOb);
            }
        }

        sortedTriadCards.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

        var noCollectionData = new GameCardInfo.CollectionPos
        {
            PageIndex = -1, CellIndex = -1
        };

        for (var filterIdx = 0; filterIdx < 3; filterIdx++)
        {
            var groupIdx = 0;
            var pageIdx = 0;
            var cellIdx = 0;
            var filterMaxPage = 0;

            foreach (var cardOb in sortedTriadCards)
            {
                var isValid = mapCards.TryGetValue(cardOb.Id, out var cardInfoOb);
                if (isValid && cardInfoOb != null)
                {
                    var isOwned = ownedCardIds.Contains(cardOb.Id);
                    var isMatchingFilter =
                        ((GameCardCollectionFilter)filterIdx == GameCardCollectionFilter.All) ||
                        ((GameCardCollectionFilter)filterIdx == GameCardCollectionFilter.OnlyOwned && isOwned) ||
                        ((GameCardCollectionFilter)filterIdx == GameCardCollectionFilter.OnlyMissing && !isOwned);

                    if (isMatchingFilter)
                    {
                        if (groupIdx != cardOb.Group)
                        {
                            groupIdx = cardOb.Group;
                            pageIdx++;
                            cellIdx = 0;
                        }

                        if (cellIdx >= MaxGridCells)
                        {
                            cellIdx = 0;
                            pageIdx++;
                        }

                        cardInfoOb.Collection[filterIdx] = new()
                        {
                            PageIndex = pageIdx, CellIndex = cellIdx
                        };
                        if (pageIdx > filterMaxPage)
                        {
                            filterMaxPage = pageIdx;
                        }
                        cellIdx++;
                    }
                    else
                    {
                        cardInfoOb.Collection[filterIdx] = new()
                        {
                            PageIndex = -1, CellIndex = -1
                        };
                    }
                }
            }

            if (filterMaxPage > maxGridPageIndex)
            {
                maxGridPageIndex = filterMaxPage;
            }
        }
    }

    private void RebuildDeckEditPages()
    {
        var cardsDB = TriadCardDB.Get();

        var sortedDeckEditCards = new List<Tuple<TriadCard, GameCardInfo>>();
        foreach (var kvp in mapCards)
        {
            var cardOb = cardsDB.FindById(kvp.Key);
            if (cardOb != null && cardOb.IsValid())
            {
                var entry = new Tuple<TriadCard, GameCardInfo>(cardOb, kvp.Value);
                sortedDeckEditCards.Add(entry);
            }
        }

        sortedDeckEditCards.Sort((a, b) =>
        {
            if (a.Item1.Rarity != b.Item1.Rarity)
            {
                return a.Item1.Rarity.CompareTo(b.Item1.Rarity);
            }

            if (a.Item2.SortKey != b.Item2.SortKey)
            {
                return a.Item2.SortKey.CompareTo(b.Item2.SortKey);
            }

            return a.Item1.SortOrder.CompareTo(b.Item1.SortOrder);
        });

        var noCollectionData = new GameCardInfo.CollectionPos
        {
            PageIndex = -1, CellIndex = -1
        };

        var filterIdx = (int)GameCardCollectionFilter.DeckEditDefault;
        var pageIdx = 0;
        var cellIdx = 0;
        var deckEditMaxPage = 0;

        foreach (var entry in sortedDeckEditCards)
        {
            var cardOb = entry.Item1;
            var cardInfoOb = entry.Item2;
            if (cardOb != null && cardInfoOb != null)
            {
                if (ownedCardIds.Contains(cardOb.Id))
                {
                    if (cellIdx >= MaxGridCells)
                    {
                        cellIdx = 0;
                        pageIdx++;
                    }

                    cardInfoOb.Collection[filterIdx] = new()
                    {
                        PageIndex = pageIdx, CellIndex = cellIdx
                    };
                    if (pageIdx > deckEditMaxPage)
                    {
                        deckEditMaxPage = pageIdx;
                    }
                    cellIdx++;
                }
                else
                {
                    cardInfoOb.Collection[filterIdx] = new()
                    {
                        PageIndex = -1, CellIndex = -1
                    };
                }
            }
        }

        if (deckEditMaxPage > maxGridPageIndex)
        {
            maxGridPageIndex = deckEditMaxPage;
        }
    }

    public readonly struct DeckEditCardEntry(TriadCard card, GameCardInfo info, int displayNo)
    {
        public readonly TriadCard Card = card;
        public readonly GameCardInfo Info = info;
        public readonly int DisplayNo = displayNo;
    }
}
