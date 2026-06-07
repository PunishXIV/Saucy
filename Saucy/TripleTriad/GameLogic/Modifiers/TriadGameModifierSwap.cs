#nullable disable
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierSwap : TriadGameModifier
{
    public TriadGameModifierSwap()
    {
        RuleName = "Swap";
        RuleIndex = 13;
        SpecialMod = ETriadGameSpecialMod.SwapCards;
    }

    public static void StaticSwapCards(TriadGameSimulationState gameData, TriadCard swapFromBlue, int blueSlotIdx, TriadCard swapFromRed, int redSlotIdx)
    {
        if (gameData.deckBlue is TriadDeckInstanceManual deckBlueEx && gameData.deckRed is TriadDeckInstanceManual deckRedEx)
        {
            var bIsRedFromKnown = redSlotIdx < deckRedEx.deck.knownCards.Count;
            if (gameData.bDebugRules)
            {
                var DummyOb = new TriadGameModifierSwap();
                Logger.WriteLine(">> " + DummyOb.RuleName + "! blue[" + blueSlotIdx + "]:" + swapFromBlue.Name +
                                 " <-> red[" + redSlotIdx + (bIsRedFromKnown ? "" : ":Opt") + "]:" + swapFromRed.Name);
            }

            var blueDeckSwapped = new TriadDeck(deckBlueEx.deck.knownCards, deckBlueEx.deck.unknownCardPool);
            var redDeckSwapped = new TriadDeck(deckRedEx.deck.knownCards, deckRedEx.deck.unknownCardPool);

            redDeckSwapped.knownCards.Add(swapFromBlue);
            redDeckSwapped.knownCards.Remove(swapFromRed);
            redDeckSwapped.unknownCardPool.Remove(swapFromRed);

            blueDeckSwapped.knownCards[blueSlotIdx] = swapFromRed;

            gameData.deckBlue = new TriadDeckInstanceManual(blueDeckSwapped);
            gameData.deckRed = new TriadDeckInstanceManual(redDeckSwapped);
        }
    }
}
