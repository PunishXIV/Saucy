using System.Collections.Generic;

namespace FFTriadBuddy;

internal static class GameDataText
{
    public static readonly Dictionary<ETriadCardType, string> CardTypeNames = [];

    public static string GetCardTypeName(ETriadCardType type) =>
        CardTypeNames.TryGetValue(type, out var name) ? name : type.ToString();
}
