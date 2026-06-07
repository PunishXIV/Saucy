#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierAllOpen : TriadGameModifier
{
    public TriadGameModifierAllOpen()
    {
        RuleName = "All Open";
        RuleIndex = 2;
        SpecialMod = ETriadGameSpecialMod.SelectVisible5;
    }

    public static void StaticMakeKnown(TriadGameSimulationState gameData, List<int> redIndices)
    {
        const int deckSize = 5;

        if (gameData.deckRed is TriadDeckInstanceManual deckRedEx && redIndices.Count <= deckSize)
        {
            if (gameData.bDebugRules)
            {
                Logger.WriteLine(">> Open:{0}! red indices:{1}", redIndices.Count, string.Join(", ", redIndices));
            }

            var redDeckVisible = new TriadDeck(deckRedEx.deck.knownCards, deckRedEx.deck.unknownCardPool);
            for (var idx = 0; idx < redIndices.Count; idx++)
            {
                var cardIdx = redIndices[idx];
                if (cardIdx < deckRedEx.deck.knownCards.Count)
                {
                }
                else
                {
                    var idxU = cardIdx - deckRedEx.deck.knownCards.Count;
                    var cardOb = deckRedEx.deck.unknownCardPool[idxU];
                    redDeckVisible.knownCards.Add(cardOb);
                    redDeckVisible.unknownCardPool.Remove(cardOb);
                }
            }

            for (var idx = 0; (idx < redDeckVisible.knownCards.Count) && (redDeckVisible.knownCards.Count > deckSize); idx++)
            {
                var cardOb = redDeckVisible.knownCards[idx];
                var orgIdx = deckRedEx.GetCardIndex(cardOb);
                if (!redIndices.Contains(orgIdx))
                {
                    redDeckVisible.knownCards.RemoveAt(idx);
                    idx--;
                }
            }

            gameData.deckRed = new TriadDeckInstanceManual(redDeckVisible);
        }
    }
}
