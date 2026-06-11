using Dalamud.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.GameLogic;

internal static class SaucyParallelism
{
    private const int LiveRolloutWorkers = 2000;
    private const int BackgroundRolloutWorkers = 2000;

    private static readonly object EvalSync = new();
    private static readonly Lazy<bool> isWineHost = new(Util.IsWine);
    private static int cachedEvalLimit = -1;
    private static SemaphoreSlim? evalConcurrency;

    public static bool IsWineHost => isWineHost.Value;

    public static int LogicalProcessorCount => Environment.ProcessorCount;

    public static int RolloutWorkerCount =>
        TriadUiState.IsBoardVisible() ? LiveRolloutWorkers : BackgroundRolloutWorkers;

    public static ParallelOptions RolloutParallelOptions => new()
    {
        MaxDegreeOfParallelism = CapAutoThreads(TriadUiState.IsBoardVisible()
            ? Math.Max(1, LogicalProcessorCount / 4)
            : Math.Max(1, LogicalProcessorCount))
    };

    public static int DeckOptimizerConfiguredThreads
        => Configuration.ClampDeckOptimizerMaxThreads(C.DeckOptimizerMaxThreads);

    public static int DeckOptimizerThreads
    {
        get
        {
            var configured = DeckOptimizerConfiguredThreads;
            var threads = configured <= 0 ? CapAutoThreads(Math.Max(1, LogicalProcessorCount)) : configured;
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
            : CapAutoThreads(Math.Max(1, LogicalProcessorCount / 4));

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

    // Wine routes thread suspension through the separate wineserver process; saturating
    // every host core with solver threads starves it mid-GC-suspend and crashes the game
    // (user-confirmed: "all cores" crashed where an explicit 24-thread run was stable).
    // Auto-selected parallelism leaves a quarter of the cores free under Wine; explicit
    // user thread settings are honored as-is.
    private static int CapAutoThreads(int threads) =>
        IsWineHost ? Math.Min(threads, Math.Max(1, (LogicalProcessorCount * 3) / 4)) : threads;

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
