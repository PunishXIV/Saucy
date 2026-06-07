using System;
namespace Saucy.TripleTriad;

internal static class TriadDeckNameHelper
{
    public static bool NamesMatch(string a, string b)
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

    public static string StripSuffix(string name)
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
