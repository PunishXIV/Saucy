using System;
using System.Threading;
namespace Saucy.TripleTriad.GameLogic;

internal static class SaucyParallelism
{
    private static readonly object EvalSync = new();
    private static int cachedEvalLimit = -1;
    private static SemaphoreSlim? evalConcurrency;

    public static int LogicalProcessorCount => Environment.ProcessorCount;

    public static int DeckOptimizerConfiguredThreads
        => Configuration.ClampDeckOptimizerMaxThreads(C.DeckOptimizerMaxThreads);

    public static int DeckOptimizerThreads
    {
        get
        {
            var configured = DeckOptimizerConfiguredThreads;
            var threads = configured <= 0 ? Math.Max(1, LogicalProcessorCount) : configured;
            if (TriadUiState.IsBoardVisible())
            {
                threads = Math.Min(threads, Math.Max(1, LogicalProcessorCount / 4));
            }

            return threads;
        }
    }

    public static int EvalConcurrencyLimit
        => TriadUiState.IsBoardVisible()
            ? 1
            : Math.Max(1, LogicalProcessorCount / 4);

    public static SemaphoreSlim EvalConcurrency
    {
        get
        {
            var limit = EvalConcurrencyLimit;
            var cached = evalConcurrency;
            if (cached != null && cachedEvalLimit == limit)
            {
                return cached;
            }

            lock (EvalSync)
            {
                if (evalConcurrency != null && cachedEvalLimit == limit)
                {
                    return evalConcurrency;
                }

                evalConcurrency = new(limit, limit);
                cachedEvalLimit = limit;
                return evalConcurrency;
            }
        }
    }

    public static void ResetEvalConcurrency()
    {
        lock (EvalSync)
        {
            evalConcurrency?.Dispose();
            evalConcurrency = null;
            cachedEvalLimit = -1;
        }
    }
}
