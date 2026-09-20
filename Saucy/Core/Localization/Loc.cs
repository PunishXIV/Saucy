using System;
using System.Collections.Generic;
using System.Globalization;

namespace Saucy.Localization;

/// <summary>
/// Serialized by value into <c>Configuration.UiLanguage</c>. Numbers are pinned: never
/// reorder or reuse one, or saved configs silently switch language.
/// </summary>
public enum UiLanguage
{
    English = 0,
    ChineseSimplified = 1
}

/// <summary>
/// Lightweight localization. Keys are the original English strings; missing
/// entries fall back to the key so untranslated text always renders.
/// </summary>
public static class Loc
{
    private static readonly Lazy<Dictionary<string, string>> ZhLazy = new(BuildZh);

    // volatile + local snapshot: Apply() runs on the UI thread while T() is called from the
    // framework thread, so the swap must be published promptly and read exactly once.
    private static volatile Dictionary<string, string>? _active;

    public static string T(string? text)
    {
        var active = _active;
        return text != null && active != null && active.TryGetValue(text, out var translated)
            ? translated
            : text ?? string.Empty;
    }

    // InvariantCulture, matching LocText.English: the plugin does not control the framework
    // thread's culture, and a chat line must not disagree with the log line about "1.5" vs "1,5".
    public static string T(string format, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(format), args);

    /// <summary>
    /// Translates <paramref name="text"/> for use inside an ImGui <c>display_format</c>, which
    /// native printf parses. Doubles every '%' so a translation can never introduce a conversion
    /// specifier and misread a vararg; callers keep their own specifiers (like <c>%d</c>) outside
    /// the translated text.
    ///
    /// <remarks>
    /// Needed only by the sinks that reach native printf: the <c>format</c> parameter of
    /// <c>SliderInt</c> / <c>SliderFloat</c> / <c>DragInt</c> / <c>DragFloat</c> and friends,
    /// which bottom out in <c>ImFormatString</c>. Every Dalamud text sink - <c>ImGui.Text</c>,
    /// <c>TextWrapped</c>, <c>TextColored</c>, <c>TextDisabled</c>, <c>SetTooltip</c>,
    /// <c>BulletText</c>, <c>LabelText</c>, <c>ImGuiComponents.HelpMarker</c>, ECommons'
    /// <c>ImGuiEx.Text</c> - routes to <c>igTextUnformatted</c>, so a bare '%' there is literal
    /// and calling this would render a visible doubled '%%'. Use plain <see cref="T(string?)"/>.
    /// </remarks>
    /// </summary>
    public static string TPrintf(string text) => T(text).Replace("%", "%%");

    /// <summary>Applies the configured language; null follows the Dalamud UI language.</summary>
    public static void Apply(UiLanguage? configured)
    {
        var language = configured ?? DetectClientLanguage();
        _active = language == UiLanguage.ChineseSimplified ? ZhLazy.Value : null;
    }

    private static UiLanguage DetectClientLanguage()
    {
        try
        {
            var code = Svc.PluginInterface.UiLanguage;
            return code != null && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.ChineseSimplified
                : UiLanguage.English;
        }
        catch (Exception ex)
        {
            // Never let language detection break loading, but leave a trail: the realistic
            // failure is reading UiLanguage before the interface is ready.
            Svc.Log.Warning(ex, "Could not read the Dalamud UI language; falling back to English.");
            return UiLanguage.English;
        }
    }

    private static Dictionary<string, string> BuildZh()
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in ZhSources)
        {
            foreach (var (key, value) in source)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Merge order for the Simplified Chinese tables, last one wins on a duplicate key.
    /// <c>tools/loc_audit.py</c> reads this list so its view of the dictionary matches runtime.
    /// </summary>
    private static IReadOnlyDictionary<string, string>[] ZhSources =>
    [
        CoreZh.Entries,
        GamesZh.Entries,
        StatsZh.Entries,
        DependenciesZh.Entries,
        TriadZh.Entries,
        OptimizerZh.Entries,
        ChatZh.Entries
    ];
}
