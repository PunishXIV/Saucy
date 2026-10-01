using System;
namespace Saucy.Framework;

internal static class MinigameInputPacing
{
    public const int ClickIntervalMs = 1000;
    public const int BoardWarmupMs = 1400;

    public static bool TryMarkWarmup(ref DateTime? readyUtc)
    {
        readyUtc ??= DateTime.UtcNow;
        return (DateTime.UtcNow - readyUtc.Value).TotalMilliseconds >= BoardWarmupMs;
    }

    public static void Reset(ref DateTime? readyUtc) => readyUtc = null;
}

internal sealed class TimedFlowWindow(TimeSpan duration)
{
    private DateTime? untilUtc;

    internal bool IsActive => untilUtc != null && DateTime.UtcNow <= untilUtc;

    internal void Mark() => untilUtc = DateTime.UtcNow + duration;

    internal void Clear() => untilUtc = null;
}
