namespace Saucy;

/// <summary>
/// Human-readable module names. These English strings double as localization keys for
/// <see cref="Localization.Loc"/>.
/// </summary>
/// <remarks>
/// Not to be confused with <see cref="ModuleNames"/>, which holds persisted config keys
/// ("CuffACurModule") that must never be localized or renamed. The sidebar, the window
/// banner and the arcade conflict toast each used to carry their own copy of these
/// literals, so a typo in any one of them fell back to English with no compile error and
/// no audit signal. One definition means a missing translation surfaces once.
/// </remarks>
public static class ModuleDisplayNames
{
    public const string OutOnALimb = "Out on a Limb";
    public const string CuffACur = "Cuff-a-Cur";
    public const string SliceIsRight = "Slice is Right";

    /// <summary>Short form for the narrow sidebar; the banner uses <see cref="AnyWayTheWindBlows"/>.</summary>
    public const string WindBlows = "Wind Blows";

    public const string AnyWayTheWindBlows = "Any Way the Wind Blows";
    public const string AirForceOne = "Air Force One";
    public const string TripleTriad = "Triple Triad";
    public const string MiniCactpot = "Mini-Cactpot";
    public const string JumboCactpot = "Jumbo Cactpot";

    /// <summary>Banner status shown when no module is running.</summary>
    public const string Idle = "Idle";
}
