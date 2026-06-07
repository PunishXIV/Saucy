#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierSame : TriadGameModifier
{
    public TriadGameModifierSame()
    {
        RuleName = "Same";
        RuleIndex = 7;
        bAllowCombo = true;
        Features = EFeature.CaptureNei | EFeature.CardPlaced;
    }

    public override void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList)
    {
        var checkCard = gameData.board[boardPos];
        var numSame = 0;
        var neiCaptureMask = 0;
        for (var sideIdx = 0; sideIdx < 4; sideIdx++)
        {
            var testNeiPos = neiPos[sideIdx];
            if (testNeiPos >= 0 && gameData.board[testNeiPos] != null)
            {
                var neiCard = gameData.board[testNeiPos];

                var numPos = checkCard.GetNumber((ETriadGameSide)sideIdx);
                var numOther = neiCard.GetOppositeNumber((ETriadGameSide)sideIdx);
                if (numPos == numOther)
                {
                    numSame++;

                    if (neiCard.owner != checkCard.owner)
                    {
                        neiCaptureMask |= (1 << sideIdx);
                    }
                }
            }
        }

        if (numSame >= 2)
        {
            for (var sideIdx = 0; sideIdx < 4; sideIdx++)
            {
                var testNeiPos = neiPos[sideIdx];
                if ((neiCaptureMask & (1 << sideIdx)) != 0)
                {
                    var neiCard = gameData.board[testNeiPos];
                    neiCard.owner = checkCard.owner;
                    captureList.Add(testNeiPos);

                    if (gameData.bDebugRules)
                    {
                        Logger.WriteLine(">> " + RuleName + "! [" + testNeiPos + "] " + neiCard.card.Name + " => " + neiCard.owner);
                    }
                }
            }
        }
    }
}
