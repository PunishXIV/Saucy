namespace Saucy.Cactpot;

/// <summary>
/// Per-frame YesAlready pause flags for Mini/Jumbo cactpot automation.
/// Modules compute <c>shouldPause</c> from visible UI / handoff state, then call <see cref="SyncMini"/> or <see cref="SyncJumbo"/>.
/// </summary>
internal static class CactpotSessionActivity
{
    internal static bool IsMiniActive { get; private set; }

    internal static bool IsJumboActive { get; private set; }

    internal static void SyncMini(bool inSaucer, bool shouldPause) =>
        IsMiniActive = inSaucer && shouldPause;

    internal static void SyncJumbo(bool inSaucer, bool shouldPause) =>
        IsJumboActive = inSaucer && shouldPause;

    internal static void ResetMini() => IsMiniActive = false;

    internal static void ResetJumbo() => IsJumboActive = false;
}
