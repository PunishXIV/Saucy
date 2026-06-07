using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.Data;

public partial class GameDataLoader
{
    private bool ParseRules()
    {
        var modDB = TriadGameModifierDB.Get();

        var rulesSheet = Svc.Data.GetExcelSheet<TripleTriadRule>();
        if (rulesSheet == null || rulesSheet.Count != modDB.mods.Count)
        {
            Svc.Log.Fatal($"Failed to parse rules (got:{rulesSheet?.Count ?? 0}, expected:{modDB.mods.Count})");
            return false;
        }

        for (var idx = 0; idx < modDB.mods.Count; idx++)
        {
            var mod = modDB.mods[idx];
            mod.DisplayName = rulesSheet.GetRowOrDefault(ruleLogicToLuminaMap[idx])?.Name.ToString() ?? "";
        }

        return true;
    }

    private bool ParseCardTypes()
    {
        var typesSheet = Svc.Data.GetExcelSheet<TripleTriadCardType>();
        if (typesSheet == null || typesSheet.Count != Enum.GetValues<ETriadCardType>().Length)
        {
            Svc.Log.Fatal($"Failed to parse card types (got:{typesSheet?.Count ?? 0}, expected:{Enum.GetValues<ETriadCardType>().Length})");
            return false;
        }

        foreach (var row in typesSheet)
        {
            var cardType = ConvertToTriadType((byte)row.RowId);
            GameDataText.CardTypeNames[cardType] = row.Name.ToString();
        }

        return true;
    }

    private bool ParseCards()
    {
        var cardDB = TriadCardDB.Get();
        var cardInfoDB = GameCardDB.Get();

        var cardDataSheet = Svc.Data.GetExcelSheet<TripleTriadCardResident>();
        var cardNameSheet = Svc.Data.GetExcelSheet<TripleTriadCard>();

        if (cardDataSheet != null && cardNameSheet != null && cardDataSheet.Count == cardNameSheet.Count)
        {
            var cardTypesSheet = Svc.Data.GetExcelSheet<TripleTriadCardType>();
            var cardRaritySheet = Svc.Data.GetExcelSheet<TripleTriadCardRarity>();
            if (cardTypesSheet == null || cardTypesSheet.Count != cardTypeMap.Length)
            {
                Svc.Log.Fatal($"Failed to parse card types (got:{cardTypesSheet?.Count ?? 0}, expected:{cardTypeMap.Length})");
                return false;
            }
            if (cardRaritySheet == null || cardRaritySheet.Count != cardRarityMap.Length)
            {
                Svc.Log.Fatal($"Failed to parse card rarities (got:{cardRaritySheet?.Count ?? 0}, expected:{cardRarityMap.Length})");
                return false;
            }

            for (uint idx = 0; idx < cardDataSheet.Count; idx++)
            {
                var rowData = cardDataSheet.GetRowOrDefault(idx);
                var rowName = cardNameSheet.GetRowOrDefault(idx);

                if (rowData != null && rowName != null && rowData.Value.Top > 0)
                {
                    var rowTypeId = rowData.Value.TripleTriadCardType.RowId;
                    var rowRarityId = rowData.Value.TripleTriadCardRarity.RowId;
                    var cardType = (rowTypeId < cardTypeMap.Length) ? cardTypeMap[rowTypeId] : ETriadCardType.None;
                    var cardRarity = (rowRarityId < cardRarityMap.Length) ? cardRarityMap[rowRarityId] : ETriadCardRarity.Common;

                    var cardOb = new TriadCard((int)idx, cardRarity, cardType, rowData.Value.Top, rowData.Value.Bottom, rowData.Value.Right, rowData.Value.Left, rowData.Value.Order, rowData.Value.UIPriority)
                    {
                        Name = rowName.Value.Name.ToString()
                    };

                    var absDiff = (int)Math.Abs(cardDB.cards.Count - idx);
                    if (absDiff > 10)
                    {
                        Svc.Log.Fatal($"Failed to assign card data (got:{cardDB.cards.Count}, expected:{idx})");
                        return false;
                    }

                    while (cardDB.cards.Count < idx)
                    {
                        cardDB.cards.Add(null);
                    }
                    cardDB.cards.Add(cardOb);

                    var cardInfo = new GameCardInfo
                    {
                        CardId = cardOb.Id, SortKey = rowData.Value.SortKey, SaleValue = rowData.Value.SaleValue
                    };
                    cardInfoDB.mapCards.Add(cardOb.Id, cardInfo);
                }
            }
        }
        else
        {
            Svc.Log.Fatal($"Failed to parse card data (D:{cardDataSheet?.Count ?? 0}, N:{cardNameSheet?.Count ?? 0})");
            return false;
        }

        cardDB.ProcessSameSideLists();
        return true;
    }
}

internal static class GameDataText
{
    public static readonly Dictionary<ETriadCardType, string> CardTypeNames = [];

    public static string GetCardTypeName(ETriadCardType type) =>
        CardTypeNames.TryGetValue(type, out var name) ? name : type.ToString();
}
