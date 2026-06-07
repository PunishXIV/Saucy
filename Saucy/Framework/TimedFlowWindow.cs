using System;
namespace Saucy.Framework;

internal sealed class TimedFlowWindow(TimeSpan duration)
{
    private DateTime? untilUtc;

    internal bool IsActive => untilUtc != null && DateTime.UtcNow <= untilUtc;

    internal void Mark() => untilUtc = DateTime.UtcNow + duration;

    internal void Clear() => untilUtc = null;
}
