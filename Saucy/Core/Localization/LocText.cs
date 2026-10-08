using System;
using System.Globalization;

namespace Saucy.Localization;

/// <summary>
/// A user-facing message carried as its English source string plus arguments, so the same
/// message can be logged in English and printed to chat in the current UI language.
/// Needed because <see cref="Loc"/> keys on the English literal: a message that is already
/// interpolated has no key left to look up.
///
/// <remarks>
/// There is deliberately no conversion from <see cref="string"/>. Any such operator - even an
/// explicit one - lets a caller pass an interpolated string, which compiles into a permanently
/// untranslatable message that neither the compiler nor <c>tools/loc_audit.py</c> can see.
/// <see cref="Of"/> is the only way in, so the literal always stays a key.
/// </remarks>
/// </summary>
public readonly struct LocText
{
    private readonly object?[] _args;

    private LocText(string format, object?[] args)
    {
        Format = format ?? throw new ArgumentNullException(nameof(format));
        _args = args;
    }

    public string Format { get; }

    public static LocText Of(string format, params object?[] args) => new(format, args);

    /// <summary>The message in English, for logs.</summary>
    public string English =>
        _args is not { Length: > 0 }
            ? Format ?? string.Empty
            : string.Format(CultureInfo.InvariantCulture, Format, _args);

    /// <summary>The message in the current UI language, for chat.</summary>
    public string Localized => _args is not { Length: > 0 } ? Loc.T(Format) : Loc.T(Format, _args);

    public override string ToString() => English;
}
