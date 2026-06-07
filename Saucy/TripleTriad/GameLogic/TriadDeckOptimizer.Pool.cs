#nullable disable
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public partial class TriadDeckOptimizer
{
    private void ApplyAscensionFilter(List<CardScoreData> commonScoredList, List<List<CardScoreData>> priScoredList)
    {
        static float FindCardAscValue(CardScoreData scoredEntry) => scoredEntry.score;

        var maxCardTypes = Enum.GetValues(typeof(ETriadCardType)).Length;
        var maxLists = priScoredList.Count + 1;
        var mapCardAscValues = new List<float>[maxCardTypes, maxLists];

        for (var idxL = 0; idxL < priScoredList.Count + 1; idxL++)
        {
            for (var idxT = 0; idxT < maxCardTypes; idxT++)
            {
                mapCardAscValues[idxT, idxL] = [];
            }

            if (idxL > 0)
            {
                foreach (var scoredEntry in priScoredList[idxL - 1])
                {
                    mapCardAscValues[(int)scoredEntry.card.Type, idxL].Add(FindCardAscValue(scoredEntry));
                }
            }
        }

        foreach (var scoredEntry in commonScoredList)
        {
            if (scoredEntry.card.Type != ETriadCardType.None)
            {
                mapCardAscValues[(int)scoredEntry.card.Type, 0].Add(FindCardAscValue(scoredEntry));
            }
        }

        var bestType = ETriadCardType.None;
        float bestScore = 0;

        if (debugMode) { Logger.WriteLine("Ascension filter..."); }
        for (var idxT = 0; idxT < maxCardTypes; idxT++)
        {
            if (idxT == (int)ETriadCardType.None)
            {
                continue;
            }

            var typePartialScores = new float[maxLists];
            float typeScore = 0;
            for (var idxL = 0; idxL < maxLists; idxL++)
            {
                for (var cardIdx = 0; cardIdx < mapCardAscValues[idxT, idxL].Count; cardIdx++)
                {
                    typePartialScores[idxL] += mapCardAscValues[idxT, idxL][cardIdx];
                }

                if (mapCardAscValues[idxT, idxL].Count == 0)
                {
                    typePartialScores[idxL] = 0;
                }
                else
                {
                    typePartialScores[idxL] /= mapCardAscValues[idxT, idxL].Count;
                }

                typeScore += typePartialScores[idxL];
            }
            typeScore /= maxLists;

            if (debugMode) { Logger.WriteLine("  [{0}]: score:{1} ({2})", (ETriadCardType)idxT, typeScore, string.Join(", ", typePartialScores)); }
            if (bestScore <= 0.0f || typeScore > bestScore)
            {
                bestScore = typeScore;
                bestType = (ETriadCardType)idxT;
            }
        }

        if (bestType != ETriadCardType.None)
        {
            if (debugMode) { Logger.WriteLine("  best: {0}", bestType); }
            static void IncreaseScoreForType(CardScoreData scoredEntry, ETriadCardType cardType)
            {
                if (scoredEntry.card.Type == cardType)
                {
                    scoredEntry.score += 1000.0f;
                }
            }

            foreach (var scoredEntry in commonScoredList)
            {
                IncreaseScoreForType(scoredEntry, bestType);
            }

            foreach (var priList in priScoredList)
            {
                foreach (var scoredEntry in priList)
                {
                    IncreaseScoreForType(scoredEntry, bestType);
                }
            }
        }
    }

    private bool FindCardPool(List<TriadCard> allCards, List<TriadGameModifier> modifiers, List<TriadCard> lockedCards)
    {
        currentPool = new();

        var maxRarityNum = Enum.GetValues(typeof(ETriadCardRarity)).Length;
        var priRarityNum = (int)commonRarity + 1;
        var mapAvailRarity = new int[maxRarityNum];

        var modifiersCopy = new List<TriadGameModifier>();
        modifiersCopy.AddRange(modifiers);

        var reverseModIdx = modifiersCopy.FindIndex(mod => mod.GetType() == typeof(TriadGameModifierReverse));
        var hasReverseMod = reverseModIdx >= 0;

        var ascensionModIdx = modifiersCopy.FindIndex(mod => mod.GetType() == typeof(TriadGameModifierAscension));
        var hasAscensionMod = ascensionModIdx >= 0;

        var descensionModIdx = modifiersCopy.FindIndex(mod => mod.GetType() == typeof(TriadGameModifierDescension));
        var hasDescensionMod = descensionModIdx >= 0;

        if (hasReverseMod && hasAscensionMod)
        {
            hasAscensionMod = false;
            modifiersCopy.RemoveAt(ascensionModIdx);
            modifiersCopy.Add(TriadGameModifierDB.Get().mods.Find(mod => mod.GetType() == typeof(TriadGameModifierDescension)));
        }
        else if (hasReverseMod && hasDescensionMod)
        {
            hasAscensionMod = true;
            modifiersCopy.RemoveAt(descensionModIdx);
            modifiersCopy.Add(TriadGameModifierDB.Get().mods.Find(mod => mod.GetType() == typeof(TriadGameModifierAscension)));
        }

        List<ETriadCardRarity> priRarityThr = [];
        for (var idxR = priRarityNum; idxR < maxRarityNum; idxR++)
        {
            var testRarity = (ETriadCardRarity)idxR;
            if (!hasReverseMod && maxSlotsPerRarity.ContainsKey(testRarity) && maxSlotsPerRarity[testRarity] > 0)
            {
                mapAvailRarity[idxR] = maxSlotsPerRarity[testRarity];
                mapAvailRarity[idxR - 1] -= maxSlotsPerRarity[testRarity];

                priRarityThr.Add(testRarity);
            }
        }

        if (debugMode)
        {
            Logger.WriteLine("FindCardPool> priRarityThr:{0}, maxAvail:[{1},{2},{3},{4},{5}], reverse:{6}, ascention:{7}", priRarityThr.Count,
                mapAvailRarity[0], mapAvailRarity[1], mapAvailRarity[2], mapAvailRarity[3], mapAvailRarity[4],
                hasReverseMod, hasAscensionMod);
        }

        currentPool.deckSlotTypes = new int[lockedCards.Count];
        var numLockedCards = 0;

        for (var idx = 0; idx < lockedCards.Count; idx++)
        {
            var card = lockedCards[idx];
            if (card != null)
            {
                if (card.Rarity > commonRarity)
                {
                    for (var testR = (int)card.Rarity; testR <= maxRarityNum; testR++)
                    {
                        if (mapAvailRarity[testR] > 0)
                        {
                            mapAvailRarity[testR]--;
                            break;
                        }
                    }
                }

                currentPool.deckSlotTypes[idx] = DeckSlotLocked;
                numLockedCards++;
            }
            else
            {
                currentPool.deckSlotTypes[idx] = DeckSlotCommon;
            }
        }

        if (debugMode) { Logger.WriteLine(">> adjusted for locking, numLocked:{0}, maxAvail:[{1},{2},{3},{4},{5}]", numLockedCards, mapAvailRarity[0], mapAvailRarity[1], mapAvailRarity[2], mapAvailRarity[3], mapAvailRarity[4]); }
        if (numLockedCards == lockedCards.Count)
        {
            return false;
        }

        List<CardScoreData> commonScoredList = [];
        List<List<CardScoreData>> priScoredList = [];
        for (var idxP = 0; idxP < priRarityThr.Count; idxP++)
        {
            priScoredList.Add([]);
        }

        priRarityThr.Reverse();

        foreach (var card in allCards)
        {
            if (card == null || !card.IsValid()) { continue; }

            var scoredCard = new CardScoreData
            {
                card = card, score = card.OptimizerScore
            };
            foreach (var mod in modifiersCopy)
            {
                mod.OnScoreCard(card, ref scoredCard.score);
            }

            for (var idxP = 0; idxP < priRarityThr.Count; idxP++)
            {
                if (card.Rarity <= priRarityThr[idxP])
                {
                    priScoredList[idxP].Add(scoredCard);
                }
            }

            if (card.Rarity <= commonRarity)
            {
                commonScoredList.Add(scoredCard);
            }
        }

        if (debugMode) { Logger.WriteLine(">> card lists sorted, common:{0}", commonScoredList.Count); }
        var isPoolValid = (commonScoredList.Count > 0);
        if (isPoolValid)
        {
            var numPriLists = 0;
            var deckSlotIdx = isOrderImportant ? 1 : 0;

            for (var idx = 0; idx < priScoredList.Count; idx++)
            {
                var numAvail = mapAvailRarity[(int)priRarityThr[idx]];
                if (debugMode) { Logger.WriteLine("  pri list[{0}]:{1}, rarity:{2}, avail:{3}", idx, priScoredList[idx].Count, priRarityThr[idx], numAvail); }
                if ((numAvail > 0) && (priScoredList[idx].Count > 0))
                {
                    for (var idxAvail = 0; idxAvail < numAvail; idxAvail++)
                    {
                        for (var idxD = 0; idxD < currentPool.deckSlotTypes.Length; idxD++)
                        {
                            if (currentPool.deckSlotTypes[deckSlotIdx] == DeckSlotCommon)
                            {
                                break;
                            }

                            deckSlotIdx++;
                        }

                        currentPool.deckSlotTypes[deckSlotIdx] = numPriLists;
                    }

                    numPriLists++;
                }
                else
                {
                    priScoredList[idx].Clear();
                }
            }

            if (hasAscensionMod)
            {
                ApplyAscensionFilter(commonScoredList, priScoredList);
            }

            if (numPriLists > 0)
            {
                currentPool.priorityLists = new TriadCard[numPriLists][];
                if (debugMode) { Logger.WriteLine(">> num priority lists:{0}", numPriLists); }

                var idxP = 0;
                for (var idxL = 0; idxL < priScoredList.Count; idxL++)
                {
                    var maxPriorityToUse = Math.Min(numPriorityToBuild, priScoredList[idxL].Count);
                    if (maxPriorityToUse > 0)
                    {
                        currentPool.priorityLists[idxP] = new TriadCard[maxPriorityToUse];
                        priScoredList[idxL].Sort();

                        for (var idxC = 0; idxC < maxPriorityToUse; idxC++)
                        {
                            currentPool.priorityLists[idxP][idxC] = priScoredList[idxL][idxC].card;
                        }

                        idxP++;
                    }
                }
            }

            var numPriSlots = 0;
            for (var idx = 0; idx < currentPool.deckSlotTypes.Length; idx++)
            {
                numPriSlots += (currentPool.deckSlotTypes[idx] >= 0) ? 1 : 0;
            }

            var maxCommonToUse = Math.Min(numCommonToBuild - (numCommonToBuild * numPriSlots * numCommonPctToDropPerPriSlot / 100), commonScoredList.Count);
            if (debugMode) { Logger.WriteLine(">> adjusting common pool based on priSlots:{0} and drop:{1}% => {2}", numPriSlots, numCommonPctToDropPerPriSlot, maxCommonToUse); }

            currentPool.commonList = new TriadCard[maxCommonToUse];
            commonScoredList.Sort();

            for (var idx = 0; idx < currentPool.commonList.Length; idx++)
            {
                currentPool.commonList[idx] = commonScoredList[idx].card;
            }
        }

        if (debugMode) { Logger.WriteLine(">> deck slot types:[{0}, {1}, {2}, {3}, {4}]", currentPool.deckSlotTypes[0], currentPool.deckSlotTypes[1], currentPool.deckSlotTypes[2], currentPool.deckSlotTypes[3], currentPool.deckSlotTypes[4]); }
        return isPoolValid;
    }
}
