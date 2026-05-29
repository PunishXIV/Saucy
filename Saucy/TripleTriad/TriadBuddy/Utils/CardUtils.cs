using FFTriadBuddy;
namespace TriadBuddy;

internal static class CardUtils
{
    public static string GetUIDesc(TriadCard card) => card.Name.GetLocalized();

    public static string GetOrderDesc(TriadCard card)
    {
        if (card.SortOrder > 1000)
        {
            return $"Ex.{card.SortOrder - 1000}";
        }

        return $"No.{card.SortOrder}";
    }

    public static string GetRarityDesc(TriadCard card) => $"{(int)card.Rarity + 1}★";
}
