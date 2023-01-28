using FFTriadBuddy;
using System;
using System.Collections.Generic;

namespace TriadBuddyPlugin
{
    public enum GameCardCollectionFilter
    {
        All,
        OnlyOwned,
        OnlyMissing,
        DeckEditDefault,
    }

    public class GameCardInfo
    {
        public struct CollectionPos
        {
            public int PageIndex;
            public int CellIndex;
        }

        public int CardId;
        public int SortKey;
        public int SaleValue;

        // available only when it's an NPC fight reward
        public uint ItemId;

        public List<int> RewardNpcs = new();

        // call GameCardDB.Refresh() before reading fields below
        public bool IsOwned;
        public CollectionPos[] Collection = new CollectionPos[4];
    }

    // aguments TriadCardDB with stuff not related to game logic
    public class GameCardDB
    {
        private static GameCardDB instance = new();
        public static readonly int MaxGridPages = 15;
        public static readonly int MaxGridCells = 30;

        public UnsafeReaderTriadCards memReader;
        public Dictionary<int, GameCardInfo> mapCards = new();
        public List<int> ownedCardIds = new();
        private int maxCardId = 0;

        public static GameCardDB Get() { return instance; }

        public GameCardInfo FindById(int cardId)
        {
            if (mapCards.TryGetValue(cardId, out var cardInfo))
            {
                return cardInfo;
            }

            return null;
        }

        public void OnLoaded()
        {
            // find & cache max available card Id
            // can't trust number of entries since list is no longer continuous
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

            if (memReader == null || memReader.HasErrors || maxCardId <= 0)
            {
                return;
            }

            // consider switching to memory read for bulk checks? not that UI itself cares about it...
            // check IsTriadCardOwned() for details, uiState+0x15ce5 is a byte array of szie 0x29 used as a bitmask with cardId => buffer[id / 8] & (1 << (id % 8))

            for (int id = 1; id <= maxCardId; id++)
            {
                bool isOwned = memReader.IsCardOwned(id);
                if (isOwned)
                {
                    ownedCardIds.Add(id);
                }
            }

            // update list for game logic classes
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
            sortedTriadCards.AddRange(cardsDB.cards);
            sortedTriadCards.RemoveAll(x => (x == null) || !x.IsValid());
            sortedTriadCards.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

            var noCollectionData = new GameCardInfo.CollectionPos() { PageIndex = -1, CellIndex = -1 };

            for (int filterIdx = 0; filterIdx < 3; filterIdx++)
            {
                int groupIdx = 0;
                int pageIdx = 0;
                int cellIdx = 0;

                foreach (var cardOb in sortedTriadCards)
                {
                    bool isValid = mapCards.TryGetValue(cardOb.Id, out var cardInfoOb);
                    if (isValid)
                    {
                        bool isOwned = ownedCardIds.Contains(cardOb.Id);
                        bool isMatchingFilter =
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

                            cardInfoOb.Collection[filterIdx] = new GameCardInfo.CollectionPos() { PageIndex = pageIdx, CellIndex = cellIdx };
                            cellIdx++;
                        }
                        else
                        {
                            cardInfoOb.Collection[filterIdx] = new GameCardInfo.CollectionPos() { PageIndex = -1, CellIndex = -1 };
                        }
                    }
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

            var noCollectionData = new GameCardInfo.CollectionPos() { PageIndex = -1, CellIndex = -1 };

            int filterIdx = (int)GameCardCollectionFilter.DeckEditDefault;
            int pageIdx = 0;
            int cellIdx = 0;

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

                        cardInfoOb.Collection[filterIdx] = new GameCardInfo.CollectionPos() { PageIndex = pageIdx, CellIndex = cellIdx };
                        cellIdx++;
                    }
                    else
                    {
                        cardInfoOb.Collection[filterIdx] = new GameCardInfo.CollectionPos() { PageIndex = -1, CellIndex = -1 };
                    }
                }
            }
        }
    }
}
