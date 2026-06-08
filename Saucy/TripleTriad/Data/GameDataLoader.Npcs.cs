using Dalamud.Utility;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using TriadNpcSheet = Lumina.Excel.Sheets.TripleTriad;
namespace Saucy.TripleTriad.Data;

public partial class GameDataLoader
{
    private bool ParseNpcs()
    {
        var npcDB = TriadNpcDB.Get();

        var cardDB = TriadCardDB.Get();
        var modDB = TriadGameModifierDB.Get();

        var listTriadIds = new List<uint>();

        var npcDataSheet = Svc.Data.GetExcelSheet<TriadNpcSheet>();
        if (npcDataSheet != null)
        {
            foreach (var rowData in npcDataSheet)
            {
                listTriadIds.Add(rowData.RowId);
            }
        }

        listTriadIds.Remove(0);
        if (listTriadIds.Count == 0 || npcDataSheet == null)
        {
            Svc.Log.Fatal("Failed to parse npc data (missing ids)");
            return false;
        }

        var mapTriadNpcData = new Dictionary<uint, NpcIds>();
        var sheetNpcNames = Svc.Data.GetExcelSheet<ENpcResident>();
        var sheetENpcBase = Svc.Data.GetExcelSheet<ENpcBase>();
        if (sheetNpcNames != null && sheetENpcBase != null)
        {
            foreach (var rowData in sheetENpcBase)
            {
                var triadId = rowData.ENpcData.FirstOrDefault(x => listTriadIds.Contains(x.RowId));
                if (triadId.RowId != 0 && !mapTriadNpcData.ContainsKey(triadId.RowId))
                {
                    var rowName = sheetNpcNames.GetRowOrDefault(rowData.RowId);
                    if (rowName != null)
                    {
                        mapTriadNpcData.Add(triadId.RowId, new()
                        {
                            ENpcId = rowData.RowId, TriadNpcId = triadId.RowId, Name = rowName.Value.Singular.ToString()
                        });
                    }
                }
            }
        }
        else
        {
            Svc.Log.Fatal($"Failed to parse npc data (NN:{sheetNpcNames?.Count ?? 0}, NB:{sheetENpcBase?.Count ?? 0})");
            return false;
        }

        var ruleLuminaToLogicMap = new int[ruleLogicToLuminaMap.Length];
        for (var idx = 0; idx < ruleLogicToLuminaMap.Length; idx++)
        {
            ruleLuminaToLogicMap[ruleLogicToLuminaMap[idx]] = idx;
        }

        var nameLocId = 0;
        foreach (var rowData in npcDataSheet)
        {
            if (!mapTriadNpcData.ContainsKey(rowData.RowId))
            {
                continue;
            }

            var listRules = new List<TriadGameModifier>();
            foreach (var ruleRow in rowData.TripleTriadRule)
            {
                if (ruleRow.RowId != 0 && ruleRow.IsValid)
                {
                    if (ruleRow.RowId >= modDB.mods.Count)
                    {
                        Svc.Log.Fatal($"Failed to parse npc data (rule.id:{ruleRow.RowId})");
                        return false;
                    }

                    var logicRule = modDB.mods[ruleLuminaToLogicMap[(int)ruleRow.RowId]];
                    listRules.Add(logicRule);

                    var ruleValueOb = ruleRow.Value;
                    if (ruleValueOb.Name.ToString() != logicRule.GetLocalizedName())
                    {
                        Svc.Log.Fatal($"Failed to match npc rules! (rule.id:{ruleRow.RowId})");
                        return false;
                    }
                }
            }

            var numCardsFixed = 0;
            var cardsFixed = new int[5];
            {
                if (rowData.TripleTriadCardFixed.Count != 5)
                {
                    Svc.Log.Fatal($"Failed to parse npc data (num CF:{rowData.TripleTriadCardFixed.Count})");
                    return false;
                }

                for (var cardIdx = 0; cardIdx < rowData.TripleTriadCardFixed.Count; cardIdx++)
                {
                    var cardRowIdx = rowData.TripleTriadCardFixed[cardIdx].RowId;
                    if (cardRowIdx != 0)
                    {
                        if (cardRowIdx >= cardDB.cards.Count)
                        {
                            Svc.Log.Fatal($"Failed to parse npc data (card.id:{cardRowIdx})");
                            return false;
                        }

                        cardsFixed[cardIdx] = (int)cardRowIdx;
                        numCardsFixed++;
                    }
                }
            }

            var numCardsVar = 0;
            var cardsVariable = new int[5];
            {
                if (rowData.TripleTriadCardVariable.Count != 5)
                {
                    Svc.Log.Fatal($"Failed to parse npc data (num CV:{rowData.TripleTriadCardVariable.Count})");
                    return false;
                }

                for (var cardIdx = 0; cardIdx < rowData.TripleTriadCardVariable.Count; cardIdx++)
                {
                    var cardRowIdx = rowData.TripleTriadCardVariable[cardIdx].RowId;
                    if (cardRowIdx != 0)
                    {
                        if (cardRowIdx >= cardDB.cards.Count)
                        {
                            Svc.Log.Fatal($"Failed to parse npc data (card.id:{cardRowIdx})");
                            return false;
                        }

                        cardsVariable[cardIdx] = (int)cardRowIdx;
                        numCardsVar++;
                    }
                }
            }

            if (numCardsFixed == 0 && numCardsVar == 0)
            {
                continue;
            }

            var npcIdData = mapTriadNpcData[rowData.RowId];
            var npcOb = new TriadNpc(nameLocId, listRules, cardsFixed, cardsVariable)
            {
                Name = npcIdData.Name
            };
            npcOb.OnNameUpdated();
            nameLocId++;

            var newCachedData = new ENpcCachedData
            {
                triadId = npcIdData.TriadNpcId, gameLogicOb = npcOb, matchFee = rowData.Fee
            };
            if (rowData.ItemPossibleReward.Count > 0)
            {
                newCachedData.rewardItems = new uint[rowData.ItemPossibleReward.Count];
                for (var rewardIdx = 0; rewardIdx < rowData.ItemPossibleReward.Count; rewardIdx++)
                {
                    newCachedData.rewardItems[rewardIdx] = rowData.ItemPossibleReward[rewardIdx].RowId;
                }
            }

            mapENpcCache.Add(npcIdData.ENpcId, newCachedData);
        }

        return true;
    }

    private bool ParseNpcUnlockQuests()
    {
        var npcSheet = Svc.Data.GetExcelSheet<TriadNpcSheet>();
        if (npcSheet == null)
        {
            return true;
        }

        foreach (var row in npcSheet)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            foreach (var questRef in row.PreviousQuest)
            {
                if (questRef.RowId == 0)
                {
                    continue;
                }

                mapNpcUnlockQuestId[row.RowId] = questRef.RowId;
                break;
            }
        }

        return true;
    }

    private bool ParseNpcAchievements()
    {
        var npcDataSheet = Svc.Data.GetExcelSheet<TripleTriadResident>();
        if (npcDataSheet != null)
        {
            foreach (var rowData in npcDataSheet)
            {
                mapNpcAchievementId.Add(rowData.RowId, rowData.Order);
            }
        }

        return true;
    }

    private bool ParseNpcLocations()
    {
        var sheetLevel = Svc.Data.GetExcelSheet<Level>();
        if (sheetLevel != null)
        {
            const byte TypeNpc = 8;
            foreach (var row in sheetLevel)
            {
                if (row.Type == TypeNpc)
                {
                    if (mapENpcCache.TryGetValue(row.Object.RowId, out var npcCache))
                    {
                        npcCache.mapRawCoords = [row.X, row.Y, row.Z];
                        npcCache.mapId = row.Map.RowId;
                        npcCache.territoryId = row.Territory.RowId;
                    }
                }
            }
        }

        var sheetMap = Svc.Data.GetExcelSheet<Map>();
        if (sheetMap != null)
        {
            foreach (var kvp in mapENpcCache)
            {
                var mapRow = sheetMap.GetRowOrDefault(kvp.Value.mapId);
                if (mapRow == null)
                {
                    continue;
                }

                var map = mapRow.Value;
                var mapTerritoryId = map.TerritoryType.RowId;
                if (mapTerritoryId != 0)
                {
                    kvp.Value.territoryId = mapTerritoryId;
                }

                if (kvp.Value.mapRawCoords != null)
                {
                    kvp.Value.mapCoords =
                    [
                        MapUtil.ConvertWorldCoordXZToMapCoord(
                            kvp.Value.mapRawCoords[0], map.SizeFactor, map.OffsetX),
                        MapUtil.ConvertWorldCoordXZToMapCoord(
                            kvp.Value.mapRawCoords[2], map.SizeFactor, map.OffsetY)
                    ];
                }
            }
        }

        return true;
    }

    private bool ParseCardRewards()
    {
        var sheetItems = Svc.Data.GetExcelSheet<Item>();
        if (sheetItems != null)
        {
            var cardsDB = TriadCardDB.Get();
            var gameCardDB = GameCardDB.Get();

            foreach (var kvp in mapENpcCache)
            {
                if (kvp.Value.rewardItems == null)
                {
                    continue;
                }

                foreach (var itemId in kvp.Value.rewardItems)
                {
                    var itemRow = itemId == 0 ? null : sheetItems.GetRowOrDefault(itemId);
                    if (itemRow != null)
                    {
                        var cardOb = cardsDB.FindById((int)itemRow.Value.AdditionalData.RowId);
                        if (cardOb == null)
                        {
                            var cardName = NormalizeCardItemName(itemRow.Value.Name.ToString());
                            cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.Equals(cardName, StringComparison.InvariantCultureIgnoreCase));

                            if (cardOb == null)
                            {
                                cardName = NormalizeCardItemName(itemRow.Value.Singular.ToString());
                                cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.Equals(cardName, StringComparison.InvariantCultureIgnoreCase));

                                if (cardOb == null)
                                {
                                    cardName = NormalizeCardItemName(itemRow.Value.Plural.ToString());
                                    cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.Equals(cardName, StringComparison.InvariantCultureIgnoreCase));
                                }
                            }
                        }

                        if (cardOb != null)
                        {
                            var cardInfo = gameCardDB.FindById(cardOb.Id);
                            cardInfo?.ItemId = itemId;

                            kvp.Value.rewardCardIds.Add(cardOb.Id);
                        }
                        else
                        {
                            var npcName = (kvp.Value.gameLogicOb != null) ? kvp.Value.gameLogicOb.Name : "??";
                            Svc.Log.Error($"Failed to parse npc reward data! npc:{kvp.Value.triadId} ({npcName}), rewardId:{itemId} ({itemRow.Value.Name} | {itemRow.Value.Singular})");
                        }
                    }
                }
            }
        }

        return true;
    }

    private void FinalizeNpcList()
    {
        var gameCardDB = GameCardDB.Get();
        var gameNpcDB = GameNpcDB.Get();
        var npcDB = TriadNpcDB.Get();

        foreach (var kvp in mapENpcCache)
        {
            var cacheOb = kvp.Value;
            if (cacheOb != null && cacheOb.gameLogicOb != null)
            {
                if (cacheOb.mapId != 0)
                {
                    cacheOb.gameLogicIdx = npcDB.npcs.Count;
                    npcDB.npcs.Add(cacheOb.gameLogicOb);
                }

                cacheOb.gameLogicOb.Id = (cacheOb.mapId != 0) ? cacheOb.gameLogicIdx : -1;
            }
        }

        gameNpcDB.mapNpcs.Clear();
        if (npcDB.npcs.Count > 1)
        {
            npcDB.npcs.Sort((x, y) => x.Id.CompareTo(y.Id));
        }

        foreach (var kvp in mapENpcCache)
        {
            if (kvp.Value.gameLogicIdx < 0)
            {
                continue;
            }

            var gameNpcOb = new GameNpcInfo
            {
                npcId = kvp.Value.gameLogicIdx,
                triadId = (int)kvp.Value.triadId,
                ENpcBaseId = kvp.Key
            };
            if (mapNpcUnlockQuestId.TryGetValue(kvp.Value.triadId, out var unlockQuestId))
            {
                gameNpcOb.UnlockQuestId = unlockQuestId;
                gameNpcOb.UnlockQuestName = Svc.Data.GetExcelSheet<Quest>()?.GetRowOrDefault(unlockQuestId)?.Name.ToString();
            }
            if (!mapNpcAchievementId.TryGetValue(kvp.Value.triadId, out gameNpcOb.achievementId))
            {
                Svc.Log.Info($"Failed to find achievId for triadId:{kvp.Value.triadId}");
            }

            gameNpcOb.matchFee = kvp.Value.matchFee;
            gameNpcOb.TerritoryId = kvp.Value.territoryId;
            if (kvp.Value.mapCoords != null)
            {
                gameNpcOb.Location = new(kvp.Value.territoryId, kvp.Value.mapId, kvp.Value.mapCoords[0], kvp.Value.mapCoords[1]);
            }

            if (kvp.Value.mapRawCoords is { Length: 3 } rawCoords)
            {
                gameNpcOb.WorldPosition = new(rawCoords[0], rawCoords[1], rawCoords[2]);
            }

            foreach (var cardId in kvp.Value.rewardCardIds)
            {
                gameNpcOb.rewardCards.Add(cardId);

                var cardInfo = gameCardDB.FindById(cardId);
                if (cardInfo == null)
                {
                    Svc.Log.Error($"Failed to match npc reward data! npc:{gameNpcOb.npcId}, key:{kvp.Key}, card:{cardId}");
                    continue;
                }

                var npcOb = (gameNpcOb.npcId < npcDB.npcs.Count) ? npcDB.npcs[gameNpcOb.npcId] : null;
                if (npcOb != null)
                {
                    cardInfo.RewardNpcs.Add(gameNpcOb.npcId);
                }
                else
                {
                    Svc.Log.Error($"Failed to match npc reward data! npc:{gameNpcOb.npcId}, key:{kvp.Key}");
                }
            }

            gameNpcDB.mapNpcs.Add(gameNpcOb.npcId, gameNpcOb);
        }
    }

    private void FixLocalizedNameCasing()
    {
        var excludedList = new[]
        {
            "the", "goe", "van", "des", "sas", "yae", "tol", "der", "rem"
        };

        foreach (var card in TriadCardDB.Get().cards)
        {
            card?.Name = FixNameCasing(card.Name, excludedList);
        }

        foreach (var npc in TriadNpcDB.Get().npcs)
        {
            npc?.Name = FixNameCasing(npc.Name, excludedList);
        }
    }

    private static string NormalizeCardItemName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return string.Empty;
        }

        var normalized = itemName.Trim();
        normalized = normalized.Replace("-Karten", "", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("-Karte", "", StringComparison.OrdinalIgnoreCase);

        foreach (var suffix in new[]
        {
            " card", " cards", " carte", " cartes", " karte", " karten"
        })
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^suffix.Length].TrimEnd();
                break;
            }
        }

        const string cartePrefix = "carte ";
        if (normalized.StartsWith(cartePrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[cartePrefix.Length..].TrimStart();
        }

        const string kartePrefix = "karte ";
        if (normalized.StartsWith(kartePrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[kartePrefix.Length..].TrimStart();
        }

        return normalized.Trim();
    }

    private static string FixNameCasing(string text, string[] excludedList)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var tokens = text.Split(' ');
        var numChangedTokens = 0;

        for (var idx = 0; idx < tokens.Length; idx++)
        {
            if (tokens[idx].Length > 2 && Array.IndexOf(excludedList, tokens[idx]) < 0)
            {
                if (tokens[idx][1] == '\'')
                {
                    continue;
                }

                if (char.IsLower(tokens[idx], 0))
                {
                    tokens[idx] = tokens[idx][..1].ToUpper() + tokens[idx][1..];
                    numChangedTokens++;
                }
            }
        }

        return numChangedTokens > 0 ? string.Join(' ', tokens) : text;
    }

    public static ETriadCardType ConvertToTriadType(byte rawType) => (rawType < cardTypeMap.Length) ? cardTypeMap[rawType] : ETriadCardType.None;

    public static ETriadCardRarity ConvertToTriadRarity(byte rawRarity) => (rawRarity < cardRarityMap.Length) ? cardRarityMap[rawRarity] : ETriadCardRarity.Common;

    private class ENpcCachedData
    {
        public int gameLogicIdx = -1;
        public TriadNpc? gameLogicOb;
        public float[]? mapCoords;
        public uint mapId;

        public float[]? mapRawCoords;

        public int matchFee;
        public List<int> rewardCardIds = [];

        public uint[]? rewardItems;
        public uint territoryId;
        public uint triadId;
    }

    private struct NpcIds
    {
        public uint TriadNpcId;
        public uint ENpcId;
        public string Name;
    }
}
