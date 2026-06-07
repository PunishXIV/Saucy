using System;
using System.Threading;
namespace Saucy.TripleTriad.GameLogic;

internal static class SaucyParallelism
{
    private static readonly object EvalSync = new();
    private static int cachedEvalLimit = -1;

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
            var cached = field;
            if (cached != null && cachedEvalLimit == limit)
            {
                return cached;
            }

            lock (EvalSync)
            {
                if (field != null && cachedEvalLimit == limit)
                {
                    return field;
                }

                field = new(limit, limit);
                cachedEvalLimit = limit;
                return field;
            }
        }
    }
}
