using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.Data;

public class TriadCardDB
{
    private static readonly TriadCardDB instance = new();

    public List<TriadCard?> cards = [];
    public TriadCard hiddenCard;
    public Dictionary<int, List<TriadCard>> sameNumberMap = [];

    public TriadCardDB() =>
        hiddenCard = new(0, ETriadCardRarity.Common, ETriadCardType.None, 0, 0, 0, 0, 0, 0)
        {
            Name = "(hidden)"
        };

    public static TriadCardDB Get() => instance;

    public TriadCard? Find(string Name) => cards.Find(x => (x != null) && x.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));

    public TriadCard? Find(int numUp, int numLeft, int numDown, int numRight) =>
        cards.Find(x =>
            (x != null) &&
            (x.Sides[(int)ETriadGameSide.Up] == numUp) &&
            (x.Sides[(int)ETriadGameSide.Down] == numDown) &&
            (x.Sides[(int)ETriadGameSide.Left] == numLeft) &&
            (x.Sides[(int)ETriadGameSide.Right] == numRight));

    public TriadCard? Find(int numUp, int numLeft, int numDown, int numRight, ETriadCardType type, ETriadCardRarity rarity) =>
        cards.Find(x =>
            (x != null) &&
            (x.Sides[(int)ETriadGameSide.Up] == numUp) &&
            (x.Sides[(int)ETriadGameSide.Down] == numDown) &&
            (x.Sides[(int)ETriadGameSide.Left] == numLeft) &&
            (x.Sides[(int)ETriadGameSide.Right] == numRight) &&
            (x.Rarity == rarity) &&
            (x.Type == type));

    public TriadCard? FindById(int cardId)
    {
        if (cardId < 0 || cardId >= cards.Count)
        {
            return null;
        }

        var knownCard = cards[cardId];
        if (knownCard != null && knownCard.Id == cardId)
        {
            return knownCard;
        }

        return cards.Find(x => (x != null) && x.Id == cardId);
    }

    public TriadCard? FindBySortOrder(int sortOrder) =>
        cards.Find(x => x != null && x.SortOrder == sortOrder);

    public TriadCard? FindByTexture(string texPath)
    {
        if (string.IsNullOrEmpty(texPath) || !texPath.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (TryMapLegacy082Texture(texPath, out var legacyCardId))
        {
            return FindById(legacyCardId);
        }

        if (TryMapStandardTexture(texPath, "088000/088", out var smallIconId) ||
            TryMapStandardTexture(texPath, "087000/087", out smallIconId))
        {
            return FindById(smallIconId);
        }

        return null;
    }

    private static bool TryMapLegacy082Texture(string texPath, out int cardId)
    {
        cardId = -1;
        const string pattern = "082000/082";
        var patternPos = texPath.IndexOf(pattern, StringComparison.Ordinal);
        if (patternPos <= 0 || !TryReadThreeDigitId(texPath, patternPos + pattern.Length, out var rawId))
        {
            return false;
        }

        if (rawId >= 500)
        {
            cardId = rawId - 500;
        }
        else if (rawId >= 100)
        {
            cardId = rawId - 100;
        }

        return cardId is >= 0 and < 1000;
    }

    private static bool TryMapStandardTexture(string texPath, string pattern, out int cardId)
    {
        cardId = -1;
        var patternPos = texPath.IndexOf(pattern, StringComparison.Ordinal);
        if (patternPos <= 0 || !TryReadThreeDigitId(texPath, patternPos + pattern.Length, out cardId))
        {
            return false;
        }

        return cardId >= 0;
    }

    private static bool TryReadThreeDigitId(string texPath, int startIndex, out int cardId)
    {
        cardId = -1;
        if (startIndex + 3 > texPath.Length)
        {
            return false;
        }

        var idStr = texPath.Substring(startIndex, 3);
        return int.TryParse(idStr, out cardId);
    }

    public static uint GetCardTextureId(int cardId) => (cardId is < 0 or >= 1000) ? 87000 : (uint)cardId + 87000;

    public static uint GetCardIconTextureId(int cardId) => (cardId is < 0 or >= 1000) ? 88000 : (uint)cardId + 88000;

    public int TryGetCardIdFromIconId(int iconId)
    {
        if (iconId <= 0)
        {
            return -1;
        }

        const int iconBase = 88000;
        if (iconId >= iconBase)
        {
            var cardId = iconId - iconBase;
            if (FindById(cardId) != null)
            {
                return cardId;
            }
        }

        return -1;
    }

    public void ProcessSameSideLists()
    {
        sameNumberMap.Clear();
        var sameNumberId = 0;
        for (var Idx1 = 0; Idx1 < cards.Count; Idx1++)
        {
            var card1 = cards[Idx1];
            if (card1 != null && card1.SameNumberId < 0)
            {
                var bHasSameNumberCards = false;
                for (var Idx2 = (Idx1 + 1); Idx2 < cards.Count; Idx2++)
                {
                    var card2 = cards[Idx2];
                    if (card2 != null && card2.SameNumberId < 0)
                    {
                        var bHasSameNumbers =
                            (card1.Sides[0] == card2.Sides[0]) &&
                            (card1.Sides[1] == card2.Sides[1]) &&
                            (card1.Sides[2] == card2.Sides[2]) &&
                            (card1.Sides[3] == card2.Sides[3]);

                        bHasSameNumberCards = bHasSameNumberCards || bHasSameNumbers;
                        if (bHasSameNumbers)
                        {
                            if (!sameNumberMap.ContainsKey(sameNumberId))
                            {
                                sameNumberMap.Add(sameNumberId, []);
                                sameNumberMap[sameNumberId].Add(card1);
                                card1.SameNumberId = sameNumberId;
                            }

                            sameNumberMap[sameNumberId].Add(card2);
                            card2.SameNumberId = sameNumberId;
                        }
                    }
                }

                if (bHasSameNumberCards)
                {
                    sameNumberId++;
                }
            }
        }
    }
}
