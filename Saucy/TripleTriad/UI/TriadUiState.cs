namespace Saucy.TripleTriad;

internal static unsafe class TriadUiState
{
    public static bool IsBoardVisible() =>
        TriadLocalClientStructs.TryGetBoard(out var _);

    public static bool IsResultVisible() =>
        TriadLocalClientStructs.TryGetResult(out var _);

    public static bool IsMatchRegistrationVisible() =>
        TriadLocalClientStructs.TryGetRequest(out var _);

    public static bool IsPrepDeckSelectVisible() =>
        IsDeckSelectOverlayVisible() &&
        !IsBoardVisible() &&
        !IsResultVisible();

    public static bool IsAutomationFlowActive() =>
        IsBoardVisible() ||
        IsResultVisible() ||
        IsMatchRegistrationVisible() ||
        IsPrepDeckSelectVisible();

    internal static bool IsDeckSelectOverlayVisible() =>
        TriadLocalClientStructs.TryGetSelDeck(out var _);
}
