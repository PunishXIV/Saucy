namespace Saucy;

public sealed partial class Saucy
{
    private static void PrepareTriadSessionForPluginLoad()
    {
        TriadDeckOptimizerJobs.CancelActive(userCancelled: true);
        SaucyParallelism.ResetEvalConcurrency();
        TriadRun = new();
        TriadRun.DebugScreenMemory.ResetSolver();
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
        uiReaderGame.OnUIStateChanged -= TriadRun.UpdateGame;

        uiReaderPrep.OnUIStateChanged -= TriadRun.UpdateDecks;
    }
}
