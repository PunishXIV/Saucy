using MgAl2O4.Utils;
using System;
using System.Collections.Generic;

namespace FFTriadBuddy;

public enum ETriadDeckState
{
    Valid,
    MissingCards,
    HasDuplicates,
    TooMany4Star,
    TooMany5Star,
};

public class TriadDeck
{
    public List<TriadCard> knownCards;
    public List<TriadCard> unknownCardPool;
    public string deckId;

    public TriadDeck()
    {
        knownCards = [];
        unknownCardPool = [];
    }

    public TriadDeck(List<TriadCard> knownCards, List<TriadCard> unknownCardPool)
    {
        this.knownCards = [];
        this.unknownCardPool = [];

        this.knownCards.AddRange(knownCards);
        this.unknownCardPool.AddRange(unknownCardPool);
        UpdateDeckId();
    }

    public TriadDeck(IEnumerable<TriadCard> knownCards)
    {
        this.knownCards = [];
        unknownCardPool = [];

        this.knownCards.AddRange(knownCards);
        UpdateDeckId();
    }

    public TriadDeck(IEnumerable<int> knownCardIds, IEnumerable<int> unknownCardlIds)
    {
        var cardDB = TriadCardDB.Get();

        knownCards = [];
        foreach (var id in knownCardIds)
        {
            var card = cardDB.cards[id];
            if (card != null && card.IsValid())
            {
                knownCards.Add(card);
            }
        }

        unknownCardPool = [];
        foreach (var id in unknownCardlIds)
        {
            var card = cardDB.cards[id];
            if (card != null && card.IsValid())
            {
                unknownCardPool.Add(card);
            }
        }

        UpdateDeckId();
    }

    public TriadDeck(IEnumerable<int> knownCardIds)
    {
        var cardDB = TriadCardDB.Get();

        knownCards = [];
        foreach (var id in knownCardIds)
        {
            var card = cardDB.cards[id];
            if (card != null && card.IsValid())
            {
                knownCards.Add(card);
            }
        }

        unknownCardPool = [];
        UpdateDeckId();
    }

    public ETriadDeckState GetDeckState()
    {
        var playerDB = PlayerSettingsDB.Get();
        var rarityCounters = new int[5];

        for (var DeckIdx = 0; DeckIdx < knownCards.Count; DeckIdx++)
        {
            var deckCard = knownCards[DeckIdx];
            var bIsOwned = playerDB.ownedCards.Contains(deckCard);
            if (!bIsOwned)
            {
                return ETriadDeckState.MissingCards;
            }

            for (var TestIdx = 0; TestIdx < knownCards.Count; TestIdx++)
            {
                if ((TestIdx != DeckIdx) && knownCards[TestIdx].Equals(deckCard))
                {
                    return ETriadDeckState.HasDuplicates;
                }
            }

            rarityCounters[(int)deckCard.Rarity]++;
        }

        var numRare5 = rarityCounters[(int)ETriadCardRarity.Legendary];
        var numRare45 = rarityCounters[(int)ETriadCardRarity.Epic] + numRare5;

        if (numRare5 > 1)
        {
            return ETriadDeckState.TooMany5Star;
        }
        else if (numRare45 > 2)
        {
            return ETriadDeckState.TooMany4Star;
        }

        return ETriadDeckState.Valid;
    }

    public TriadCard GetCard(int Idx)
    {
        if (Idx < knownCards.Count)
        {
            return knownCards[Idx];
        }
        else if (Idx < (knownCards.Count + unknownCardPool.Count))
        {
            return unknownCardPool[Idx - knownCards.Count];
        }

        return null;
    }

    public int GetCardIndex(TriadCard card)
    {
        var cardIdx = knownCards.IndexOf(card);
        if (cardIdx >= 0)
        {
            return cardIdx;
        }

        cardIdx = unknownCardPool.IndexOf(card);
        if (cardIdx >= 0)
        {
            return cardIdx + knownCards.Count;
        }

        return -1;
    }

    public bool SetCard(int Idx, TriadCard card)
    {
        var bResult = false;
        if (Idx < knownCards.Count)
        {
            knownCards[Idx] = card;
            bResult = true;
        }
        else if (Idx < (knownCards.Count + unknownCardPool.Count))
        {
            unknownCardPool[Idx - knownCards.Count] = card;
            bResult = true;
        }

        return bResult;
    }

    public int GetPower()
    {
        var SumRating = 0;
        foreach (var card in knownCards)
        {
            SumRating += (int)card.Rarity + 1;
        }

        foreach (var card in unknownCardPool)
        {
            SumRating += (int)card.Rarity + 1;
        }

        var NumCards = knownCards.Count + unknownCardPool.Count;
        var DeckPower = (NumCards > 0) ? Math.Min(Math.Max((SumRating * 2 / NumCards), 1), 10) : 1;

        return DeckPower;
    }

    public void UpdateDeckId()
    {
        deckId = "K";

        List<TriadCard> sortedList = [.. knownCards];
        sortedList.Sort((a, b) => a.Id.CompareTo(b.Id));

        foreach (var card in sortedList)
        {
            deckId += ":";
            deckId += card.Id;
        }

        deckId += "U";

        sortedList.Clear();
        sortedList.AddRange(unknownCardPool);
        sortedList.Sort((a, b) => a.Id.CompareTo(b.Id));

        foreach (var card in sortedList)
        {
            deckId += ":";
            deckId += card.Id;
        }
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as TriadDeck);
    }

    public bool Equals(TriadDeck otherDeck)
    {
        if (deckId != null && otherDeck.deckId != null)
        {
            return deckId.Equals(otherDeck.deckId);
        }

        if ((knownCards.Count != otherDeck.knownCards.Count) ||
            (unknownCardPool.Count != otherDeck.unknownCardPool.Count))
        {
            return false;
        }

        for (var Idx = 0; Idx < knownCards.Count; Idx++)
        {
            if (!knownCards[Idx].Equals(otherDeck.knownCards[Idx]))
            {
                return false;
            }
        }

        for (var Idx = 0; Idx < unknownCardPool.Count; Idx++)
        {
            if (!unknownCardPool[Idx].Equals(otherDeck.unknownCardPool[Idx]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(knownCards, unknownCardPool);
    }

    public override string ToString()
    {
        var desc = "";
        foreach (var card in knownCards)
        {
            desc += card.ToShortCodeString() + ", ";
        }

        desc = (desc.Length > 2) ? desc[..^2] : "(none)";

        if (unknownCardPool.Count > 0)
        {
            desc += " + unknown(";
            foreach (var card in unknownCardPool)
            {
                desc += card.ToShortCodeString() + ", ";
            }

            desc = desc[..^2];
            desc += ")";
        }

        var power = GetPower();
        desc += ", power:" + power;

        return desc;
    }
}

public class TriadDeckNamed : TriadDeck
{
    public string Name;

    public TriadDeckNamed() { }
    public TriadDeckNamed(TriadDeck copyFrom) : base(copyFrom.knownCards)
    {
    }
}

public abstract class TriadDeckInstance
{
    public abstract void OnCardPlacedFast(int Idx);
    public abstract int GetFirstAvailableCardFast();
    public abstract TriadCard GetCard(int Idx);
    public abstract int GetCardIndex(TriadCard card);
    public abstract TriadDeckInstance CreateCopy();

    public TriadDeck deck;
    public int availableCardMask;
    public int numUnknownPlaced;
    public int numPlaced;

    // manual, player = 5
    // manual, npc = (up to 5 fixed) + (up to 5 variable)
    // screen, npc = (up to 5 hidden) + (up to 5 fixed) + (up to 5 variable)
    public const int maxAvailableCards = 15;

    public bool IsPlaced(int cardIdx)
    {
        return (availableCardMask & (1 << cardIdx)) == 0;
    }

    public TriadCard GetFirstAvailableCard()
    {
        var cardIdx = GetFirstAvailableCardFast();
        return (cardIdx < 0) ? null : GetCard(cardIdx);
    }

    public List<TriadCard> GetAvailableCards()
    {
        List<TriadCard> cards = [];
        for (var Idx = 0; Idx < maxAvailableCards; Idx++)
        {
            var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
            if (bIsAvailable)
            {
                var card = GetCard(Idx);
                cards.Add(card);
            }
        }

        return cards;
    }
}

public class TriadDeckInstanceManual : TriadDeckInstance
{
    public TriadDeckInstanceManual(TriadDeck deck)
    {
        this.deck = deck;
        numUnknownPlaced = 0;
        numPlaced = 0;
        availableCardMask = (1 << (deck.knownCards.Count + deck.unknownCardPool.Count)) - 1;
    }

    public TriadDeckInstanceManual(TriadDeckInstanceManual copyFrom)
    {
        deck = copyFrom.deck;
        numUnknownPlaced = copyFrom.numUnknownPlaced;
        numPlaced = copyFrom.numPlaced;
        availableCardMask = copyFrom.availableCardMask;
    }

    public override TriadDeckInstance CreateCopy()
    {
        var deckCopy = new TriadDeckInstanceManual(this);
        return deckCopy;
    }

    public override void OnCardPlacedFast(int cardIdx)
    {
        availableCardMask &= ~(1 << cardIdx);
        numPlaced++;

        if (cardIdx >= deck.knownCards.Count)
        {
            const int maxCardsToPlace = ((TriadGameSimulationState.boardSize * TriadGameSimulationState.boardSize) / 2) + 1;
            var maxUnknownToPlace = maxCardsToPlace - deck.knownCards.Count;

            numUnknownPlaced++;
            if (numUnknownPlaced >= maxUnknownToPlace)
            {
                availableCardMask &= ((1 << deck.knownCards.Count) - 1);
            }
        }
    }

    public override int GetFirstAvailableCardFast()
    {
        for (var Idx = 0; Idx < deck.knownCards.Count; Idx++)
        {
            var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
            if (bIsAvailable)
            {
                return Idx;
            }
        }

        return -1;
    }

    public override TriadCard GetCard(int Idx)
    {
        return deck.GetCard(Idx);
    }

    public override int GetCardIndex(TriadCard card)
    {
        var cardIdx = deck.knownCards.IndexOf(card);
        if (cardIdx < 0)
        {
            cardIdx = deck.unknownCardPool.IndexOf(card) + deck.knownCards.Count;
        }

        return cardIdx;
    }

    public override string ToString()
    {
        var desc = "Placed: " + numPlaced + ", Available: ";

        if (availableCardMask > 0)
        {
            for (var Idx = 0; Idx < maxAvailableCards; Idx++)
            {
                var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
                if (bIsAvailable)
                {
                    var card = GetCard(Idx);
                    desc += card.ToShortCodeString() + ", ";
                }
            }

            desc = desc[..^2];
        }
        else
        {
            desc += "none";
        }

        return desc;
    }
}

public class TriadDeckInstanceScreen : TriadDeckInstance
{
    public TriadCard[] cards;
    public TriadCard swappedCard;
    public int unknownPoolMask;
    public int swappedCardIdx;

    public TriadDeckInstanceScreen()
    {
        cards = new TriadCard[5];
        availableCardMask = 0;
        unknownPoolMask = 0;
        numUnknownPlaced = 0;
        numPlaced = 0;
        swappedCardIdx = -1;
        swappedCard = null;
    }

    public TriadDeckInstanceScreen(TriadDeckInstanceScreen copyFrom)
    {
        cards = new TriadCard[copyFrom.cards.Length];
        for (var Idx = 0; Idx < copyFrom.cards.Length; Idx++)
        {
            cards[Idx] = copyFrom.cards[Idx];
        }

        deck = copyFrom.deck;
        numUnknownPlaced = copyFrom.numUnknownPlaced;
        numPlaced = copyFrom.numPlaced;
        availableCardMask = copyFrom.availableCardMask;
        unknownPoolMask = copyFrom.unknownPoolMask;
        swappedCardIdx = copyFrom.swappedCardIdx;
        swappedCard = copyFrom.swappedCard;
    }

    public override TriadDeckInstance CreateCopy()
    {
        var deckCopy = new TriadDeckInstanceScreen(this);
        return deckCopy;
    }

    public override int GetFirstAvailableCardFast()
    {
        for (var Idx = 0; Idx < cards.Length; Idx++)
        {
            var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
            if (bIsAvailable)
            {
                return Idx;
            }
        }

        return -1;
    }

    public void UpdateAvailableCards(TriadCard[] screenCards)
    {
        availableCardMask = 0;
        numPlaced = 0;
        numUnknownPlaced = 0;

        Array.Copy(screenCards, cards, 5);

        var hiddenCardId = TriadCardDB.Get().hiddenCard.Id;
        for (var Idx = 0; Idx < cards.Length; Idx++)
        {
            if (cards[Idx] != null)
            {
                if (cards[Idx].Id != hiddenCardId)
                {
                    availableCardMask |= (1 << Idx);
                }
            }
            else
            {
                numPlaced++;
            }
        }
    }

    public void SetSwappedCard(TriadCard swappedCard, int swappedCardIdx)
    {
        this.swappedCard = swappedCard;
        this.swappedCardIdx = swappedCardIdx;
        unknownPoolMask &= ~(1 << swappedCardIdx);
    }

    public override void OnCardPlacedFast(int cardIdx)
    {
        var cardMask = (1 << cardIdx);
        availableCardMask &= ~cardMask;

        if (deck != null)
        {
            var bIsUnknown = (unknownPoolMask & cardMask) != 0;
            if (bIsUnknown)
            {
                numUnknownPlaced++;

                var maxUnknownToUse = cards.Length - deck.knownCards.Count;
                if (numUnknownPlaced >= maxUnknownToUse)
                {
                    availableCardMask &= ~unknownPoolMask;
                }
            }
        }
    }

    public override TriadCard GetCard(int Idx)
    {
        return (Idx < 0) ? null :
            (Idx == swappedCardIdx) ? swappedCard :
            (Idx < cards.Length) ? cards[Idx] :
            deck?.GetCard(Idx - cards.Length);
    }

    public override int GetCardIndex(TriadCard card)
    {
        if (card == swappedCard)
        {
            return swappedCardIdx;
        }

        var cardIdx = Array.IndexOf(cards, card);
        if (cardIdx < 0 && deck != null)
        {
            cardIdx = deck.GetCardIndex(card);
            if (cardIdx >= 0)
            {
                cardIdx += cards.Length;
            }
        }

        return cardIdx;
    }

    public override string ToString()
    {
        var desc = "[SCREEN] Available: ";

        if (availableCardMask > 0)
        {
            for (var Idx = 0; Idx < cards.Length; Idx++)
            {
                var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
                if (bIsAvailable)
                {
                    var card = GetCard(Idx);
                    desc += (card != null ? card.Name.GetCodeName() : "") + (Idx == swappedCardIdx ? ":SWAP" : "") + ", ";
                }
            }

            desc = desc[..^2];
        }
        else
        {
            desc += "none";
        }

        var visibleCardsMask = (cards != null) ? ((1 << cards.Length) - 1) : 0;
        var hasHiddenCards = (availableCardMask & ~visibleCardsMask) != 0;
        if (hasHiddenCards)
        {
            desc += ", Unknown: ";
            if (deck != null)
            {
                for (var Idx = cards.Length; Idx < maxAvailableCards; Idx++)
                {
                    var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
                    if (bIsAvailable)
                    {
                        var card = GetCard(Idx);
                        var bIsKnownPool = (unknownPoolMask & (1 << Idx)) == 0;
                        desc += card.ToShortCodeString() + ":" + Idx + ":" + (bIsKnownPool ? "K" : "U") + (Idx == swappedCardIdx ? ":SWAP" : "") + ", ";
                    }
                }

                desc = desc[..^2];
            }
            else
            {
                desc += "(missing deck!)";
            }
        }

        return desc;
    }

    public void LogAvailableCards(string deckName)
    {
        Logger.WriteLine(deckName + " state> numPlaced:" + numPlaced + ", numUnknownPlaced:" + numUnknownPlaced);
        for (var Idx = 0; Idx < maxAvailableCards; Idx++)
        {
            var bIsAvailable = (availableCardMask & (1 << Idx)) != 0;
            var bIsUnknown = (unknownPoolMask & (1 << Idx)) != 0;
            var card = GetCard(Idx);

            Logger.WriteLine("   [" + Idx + "]:" + (card != null ? card.Name.GetCodeName() : "??") +
                (Idx == swappedCardIdx ? " (SWAP)" : bIsUnknown ? " (U)" : "") +
                " => " + (bIsAvailable ? "available" : "nope"));
        }
    }
}
