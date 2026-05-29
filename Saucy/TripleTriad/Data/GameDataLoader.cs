
using Lumina.Excel.Sheets;
using TriadNpcSheet = Lumina.Excel.Sheets.TripleTriad;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.Data;

public class GameDataLoader
{
    // hardcoded maps between game enums and my own. having own ones was bad idea :<
    private static readonly ETriadCardType[] cardTypeMap = [ETriadCardType.None, ETriadCardType.Primal, ETriadCardType.Scion, ETriadCardType.Beastman, ETriadCardType.Garlean];
    private static readonly ETriadCardRarity[] cardRarityMap = [ETriadCardRarity.Common, ETriadCardRarity.Common, ETriadCardRarity.Uncommon, ETriadCardRarity.Rare, ETriadCardRarity.Epic, ETriadCardRarity.Legendary];
    private static readonly uint[] ruleLogicToLuminaMap = [0, 1, 2, 3, 5, 10, 11, 4, 6, 12, 13, 8, 9, 14, 7, 15];
    private readonly Dictionary<uint, ENpcCachedData> mapENpcCache = [];
    private readonly Dictionary<uint, int> mapNpcAchievementId = [];
    private readonly Dictionary<uint, uint> mapNpcUnlockQuestId = [];
    public bool IsDataReady { get; private set; }

    public void StartAsyncWork() =>
        Task.Run(async () =>
        {
            // there are some rare and weird concurrency issues reported on plugin reinstall
            //      at Lumina.Excel.ExcelSheet`1.GetEnumerator()+MoveNext()
            //      at TriadBuddyPlugin.GameDataLoader.ParseNpcLocations(DataManager dataManager) in 
            //
            // add wait & retry mechanic, maybe it can work around whatever happened?
            // lumina doesn't expose any sync/locking so can't really solve the issue

            for (var retryIdx = 3; retryIdx >= 0; retryIdx--)
            {
                var needsRetry = false;
                try
                {
                    ParseGameData();
                }
                catch (Exception ex)
                {
                    needsRetry = retryIdx > 1;
                    Svc.Log.Warning(ex, "exception while parsing! retry:{0}", needsRetry);
                }

                if (needsRetry)
                {
                    await Task.Delay(2000);
                    Svc.Log.Info("retrying game data parsers...");
                }
                else
                {
                    break;
                }
            }
        });

    private void ParseGameData()
    {
        var cardInfoDB = GameCardDB.Get();
        var cardDB = TriadCardDB.Get();
        var npcDB = TriadNpcDB.Get();

        cardInfoDB.mapCards.Clear();
        cardDB.cards.Clear();
        npcDB.npcs.Clear();
        mapENpcCache.Clear();
        mapNpcAchievementId.Clear();

        var result = true;
        result = result && ParseRules();
        result = result && ParseCardTypes();
        result = result && ParseCards();
        result = result && ParseNpcs();
        result = result && ParseNpcUnlockQuests();
        result = result && ParseNpcAchievements();
        result = result && ParseNpcLocations();
        result = result && ParseCardRewards();

        if (result)
        {
            FinalizeNpcList();
            FixLocalizedNameCasing();
            cardInfoDB.OnLoaded();

            var cardCount = cardDB.cards.Count;
            var npcCount = npcDB.npcs.Count;
            Svc.Framework.Run(() =>
            {
                IsDataReady = true;
                Svc.Log.Info($"Loaded game data for cards:{cardCount}, npcs:{npcCount}");
            });
        }
        else
        {
            // welp. can't do anything at this point, clear all DBs
            // UI scraping will fail when data is missing there

            cardInfoDB.mapCards.Clear();
            cardDB.cards.Clear();
            npcDB.npcs.Clear();
        }

        mapENpcCache.Clear();
        mapNpcAchievementId.Clear();
    }

    private bool ParseRules()
    {
        // update rule names to match current client language
        // hardcoded mapping, good for now, it's almost never changes anyway

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

                    // i got left & right mixed up at some point...
                    var cardOb = new TriadCard((int)idx, cardRarity, cardType, rowData.Value.Top, rowData.Value.Bottom, rowData.Value.Right, rowData.Value.Left, rowData.Value.Order, rowData.Value.UIPriority);
                    cardOb.Name = rowName.Value.Name.ToString();

                    // shared logic code maps card by their ids directly: cards[id]=card
                    // should be linear and offset by 1 ([0] = empty)
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

                    // create matching entry in extended card info db
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

    private bool ParseNpcs()
    {
        var npcDB = TriadNpcDB.Get();

        // cards & rules can be mapped directly from their respective DBs
        var cardDB = TriadCardDB.Get();
        var modDB = TriadGameModifierDB.Get();

        // name is a bit more annoying to get
        var listTriadIds = new List<uint>();

        var npcDataSheet = Svc.Data.GetExcelSheet<TriadNpcSheet>();
        if (npcDataSheet != null)
        {
            // rowIds are not going from 0 here!
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

        // prep rule id mapping :/
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
                // no name = no npc entry, disabled? skip it
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
                // no cards = disabled, skip it
                continue;
            }

            var npcIdData = mapTriadNpcData[rowData.RowId];
            var npcOb = new TriadNpc(nameLocId, listRules, cardsFixed, cardsVariable);
            npcOb.Name = npcIdData.Name;
            npcOb.OnNameUpdated();
            nameLocId++;

            // don't add to noc lists just yet, there are some entries with missing locations that need to be filtered out first!

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
            return true;

        foreach (var row in npcSheet)
        {
            if (row.RowId == 0)
                continue;

            foreach (var questRef in row.PreviousQuest)
            {
                if (questRef.RowId == 0)
                    continue;

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
            // rowIds are not going from 0 here!
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
                if (mapRow != null && kvp.Value.mapRawCoords != null)
                {
                    kvp.Value.mapCoords = new float[2];
                    kvp.Value.mapCoords[0] = CovertCoordToHumanReadable(kvp.Value.mapRawCoords[0], mapRow.Value.OffsetX, mapRow.Value.SizeFactor);
                    kvp.Value.mapCoords[1] = CovertCoordToHumanReadable(kvp.Value.mapRawCoords[2], mapRow.Value.OffsetY, mapRow.Value.SizeFactor);
                }
            }
        }

        static float CovertCoordToHumanReadable(float Coord, float Offset, float Scale)
        {
            var useScale = Scale / 100.0f;
            var useValue = (Coord + Offset) * useScale;
            return ((41.0f / useScale) * ((useValue + 1024.0f) / 2048.0f)) + 1;
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
                            // try to match card by bname, remove known prefix/suffix
                            var cardName = itemRow.Value.Name.ToString().Replace("-Karte", "");
                            cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.Equals(cardName, StringComparison.InvariantCultureIgnoreCase));

                            if (cardOb == null)
                            {
                                cardName = itemRow.Value.Singular.ToString().Replace("-Karte", "");
                                cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.Equals(cardName, StringComparison.InvariantCultureIgnoreCase));

                                if (cardOb == null)
                                {
                                    cardName = itemRow.Value.Plural.ToString().Replace("-Karten", "");
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
                    // valid npc, add to lists
                    cacheOb.gameLogicIdx = npcDB.npcs.Count;
                    npcDB.npcs.Add(cacheOb.gameLogicOb);
                }

                // normal and annoying.
                // Svc.Log.Info($"Failed to add triad[{cacheOb.triadId}], enpc[{kvp.Key}], name:{cacheOb.gameLogicOb.Name} - no location found!");
                // npc.Id is their index in data array, refresh it
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
                npcId = kvp.Value.gameLogicIdx, triadId = (int)kvp.Value.triadId
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
            if (kvp.Value.mapCoords != null)
            {
                gameNpcOb.Location = new(kvp.Value.territoryId, kvp.Value.mapId, kvp.Value.mapCoords[0], kvp.Value.mapCoords[1]);
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
            if (card != null)
            {
                card.Name = FixNameCasing(card.Name, excludedList);
            }
        }

        foreach (var npc in TriadNpcDB.Get().npcs)
        {
            if (npc != null)
            {
                npc.Name = FixNameCasing(npc.Name, excludedList);
            }
        }
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
        public int gameLogicIdx = -1; // TriadNpcDB
        public TriadNpc? gameLogicOb;
        public float[]? mapCoords;
        public uint mapId;

        public float[]? mapRawCoords;

        public int matchFee;
        public List<int> rewardCardIds = [];

        public uint[]? rewardItems;
        public uint territoryId;
        public uint triadId; // TripleTriad sheet
    }

    private struct NpcIds
    {
        public uint TriadNpcId;
        public uint ENpcId;
        public string Name;
    }
}
