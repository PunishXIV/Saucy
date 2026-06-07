#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierPlus : TriadGameModifier
{
    public TriadGameModifierPlus()
    {
        RuleName = "Plus";
        RuleIndex = 8;
        bAllowCombo = true;
        Features = EFeature.CaptureNei | EFeature.CardPlaced;
    }

    public override void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList)
    {
        var checkCard = gameData.board[boardPos];
        for (var sideIdx = 0; sideIdx < 4; sideIdx++)
        {
            var testNeiPos = neiPos[sideIdx];
            if (testNeiPos >= 0 && gameData.board[testNeiPos] != null)
            {
                var neiCard = gameData.board[testNeiPos];
                if (checkCard.owner != neiCard.owner)
                {
                    var numPosPattern = checkCard.GetNumber((ETriadGameSide)sideIdx);
                    var numOtherPattern = neiCard.GetOppositeNumber((ETriadGameSide)sideIdx);
                    var sumPattern = numPosPattern + numOtherPattern;
                    var bIsCaptured = false;

                    for (var vsSideIdx = 0; vsSideIdx < 4; vsSideIdx++)
                    {
                        var vsNeiPos = neiPos[vsSideIdx];
                        if (vsNeiPos >= 0 && sideIdx != vsSideIdx && gameData.board[vsNeiPos] != null)
                        {
                            var vsCard = gameData.board[vsNeiPos];

                            var numPosVs = checkCard.GetNumber((ETriadGameSide)vsSideIdx);
                            var numOtherVs = vsCard.GetOppositeNumber((ETriadGameSide)vsSideIdx);
                            var sumVs = numPosVs + numOtherVs;

                            if (sumPattern == sumVs)
                            {
                                bIsCaptured = true;

                                if (vsCard.owner != checkCard.owner)
                                {
                                    vsCard.owner = checkCard.owner;
                                    captureList.Add(vsNeiPos);

                                    if (gameData.bDebugRules)
                                    {
                                        Logger.WriteLine(">> " + RuleName + "! [" + vsNeiPos + "] " + vsCard.card.Name + " => " + vsCard.owner);
                                    }
                                }
                            }
                        }
                    }

                    if (bIsCaptured)
                    {
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
}
