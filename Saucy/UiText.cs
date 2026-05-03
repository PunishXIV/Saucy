using System;

namespace Saucy;

internal static class UiText
{
    private static bool UseChinese
    {
        get
        {
            var uiLang = Svc.PluginInterface?.UiLanguage;
            if (IsChineseCode(uiLang))
                return true;

            var clientLang = Svc.ClientState?.ClientLanguage.ToString();
            return IsChineseCode(clientLang);
        }
    }

    private static bool IsChineseCode(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return false;

        var value = lang.Trim();
        return value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("cn", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Chinese", StringComparison.OrdinalIgnoreCase)
            || value.Contains("CHS", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Hans", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Simplified", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Hant", StringComparison.OrdinalIgnoreCase);
    }

    internal static string T(string en, string zh)
        => UseChinese ? zh : en;

    internal static string F(string en, string zh, params object[] args)
        => string.Format(T(en, zh), args);

    internal static string GameCuff => T("Cuff-a-Cur", "痛击仙人掌");
    internal static string GameTriad => T("Triple Triad", "九宫幻卡");
    internal static string GameLimb => T("Out on a Limb", "挥斧成材");
    internal static string GameSlice => T("Slice is Right", "一刀两断");
    internal static string GameWind => T("Any Way the Wind Blows", "随风而行");
    internal static string GameMiniCactpot => T("Mini Cactpot", "仙人掌小乐透");
}
