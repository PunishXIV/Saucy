using Saucy.TripleTriad;
using Saucy.TripleTriad.GameLogic;
using Saucy.TripleTriad.UI;

namespace Saucy;

public sealed partial class Saucy
{
    private static void PrepareTriadSessionForPluginLoad()
    {
        TriadDeckOptimizerJobs.CancelActive(userCancelled: true);
        SaucyParallelism.ResetEvalConcurrency();
        TriadRun = new TriadSession();
    }

    private static void PrepareTriadSessionForPluginUnload()
    {
        TriadRunSession.StopAllAutomation(announce: false);
        DetachTriadUiReaders();
        TriadRun.InvalidatePendingMoveCalc();
        SaucyParallelism.ResetEvalConcurrency();
    }

    private static void DetachTriadUiReaders()
    {
        if (uiReaderGame != null)
        {
            uiReaderGame.OnUIStateChanged -= TriadRun.UpdateGame;
        }

        if (uiReaderPrep != null)
        {
            uiReaderPrep.OnUIStateChanged -= TriadRun.UpdateDecks;
        }
    }
}
