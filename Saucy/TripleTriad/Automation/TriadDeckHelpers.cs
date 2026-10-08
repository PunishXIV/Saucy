using System;
using Saucy.Localization;

namespace Saucy.TripleTriad;

internal static class TriadDeckLog
{
    /// <summary>
    /// Prints an optimizer chat line in the current UI language. Pass a <see cref="LocText"/>
    /// (English format plus arguments) rather than an interpolated string so the text keeps a
    /// translation key.
    /// </summary>
    public static void Print(LocText message, bool force = false)
    {
        if (!force && !C.ShowOptimizerChatSpam)
        {
            return;
        }

        Svc.Chat.Print(message.Localized);
    }
}

internal static class TriadDeckNameHelper
{
    private static bool NamesMatch(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool RowMatchesNpc(string deckRowName, string expectedName, string npcName)
    {
        if (string.IsNullOrWhiteSpace(deckRowName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedName) && NamesMatch(deckRowName, expectedName))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(npcName) &&
            deckRowName.StartsWith(npcName, StringComparison.OrdinalIgnoreCase) &&
            deckRowName.Contains("(Sa", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var deckBase = StripSuffix(deckRowName);
        return deckBase.Length >= 4 &&
               !string.IsNullOrEmpty(npcName) &&
               npcName.StartsWith(deckBase, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripSuffix(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var tagIdx = name.IndexOf("(Sa", StringComparison.OrdinalIgnoreCase);
        var stripped = tagIdx > 0 ? name[..tagIdx] : name;
        return stripped.TrimEnd(' ', '.', '…');
    }
}
