using FFTriadBuddy;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TriadBuddyPlugin
{
    public class GameDataLoader
    {
        public bool IsDataReady { get; private set; } = false;

        // hardcoded maps between game enums and my own. having own ones was bad idea :<
        private static readonly ETriadCardType[] cardTypeMap = { ETriadCardType.None, ETriadCardType.Primal, ETriadCardType.Scion, ETriadCardType.Beastman, ETriadCardType.Garlean };
        private static readonly ETriadCardRarity[] cardRarityMap = { ETriadCardRarity.Common, ETriadCardRarity.Common, ETriadCardRarity.Uncommon, ETriadCardRarity.Rare, ETriadCardRarity.Epic, ETriadCardRarity.Legendary };
        private static readonly uint[] ruleLogicToLuminaMap = { 0, 1, 2, 3, 5, 10, 11, 4, 6, 12, 13, 8, 9, 14, 7, 15 };

        private class ENpcCachedData
        {
            public uint triadId;                // TripleTriad sheet
            public int gameLogicIdx = -1;       // TriadNpcDB
            public TriadNpc? gameLogicOb;

            public float[]? mapRawCoords;
            public float[]? mapCoords;
            public uint mapId;
            public uint territoryId;

            public uint[]? rewardItems;
            public List<int> rewardCardIds = new();

            public int matchFee;
        }
        private Dictionary<uint, ENpcCachedData> mapENpcCache = new();
        private Dictionary<uint, int> mapNpcAchievementId = new();

        public void StartAsyncWork()
        {
            Task.Run(async () =>
            {
                // there are some rare and weird concurrency issues reported on plugin reinstall
                //      at Lumina.Excel.ExcelSheet`1.GetEnumerator()+MoveNext()
                //      at TriadBuddyPlugin.GameDataLoader.ParseNpcLocations(DataManager dataManager) in 
                //
                // add wait & retry mechanic, maybe it can work around whatever happened?
                // lumina doesn't expose any sync/locking so can't really solve the issue

                for (int retryIdx = 3; retryIdx >= 0; retryIdx--)
                {
                    bool needsRetry = false;
                    try
                    {
                        ParseGameData();
                    }
                    catch (Exception ex)
                    {
                        needsRetry = retryIdx > 1;
                        Service.logger.Warning(ex, "exception while parsing! retry:{0}", needsRetry);
                    }

                    if (needsRetry)
                    {
                        await Task.Delay(2000);
                        Service.logger.Info("retrying game data parsers...");
                    }
                    else
                    {
                        break;
                    }
                }
            });
        }

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

            bool result = true;
            result = result && ParseRules();
            result = result && ParseCardTypes();
            result = result && ParseCards();
            result = result && ParseNpcs();
            result = result && ParseNpcAchievements();
            result = result && ParseNpcLocations();
            result = result && ParseCardRewards();

            if (result)
            {
                FinalizeNpcList();
                FixLocalizedNameCasing();
                cardInfoDB.OnLoaded();

                Service.logger.Info($"Loaded game data for cards:{cardDB.cards.Count}, npcs:{npcDB.npcs.Count}");
                IsDataReady = true;
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
            var locDB = LocalizationDB.Get();

            var rulesSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadRule>();
            if (rulesSheet == null || rulesSheet.RowCount != modDB.mods.Count)
            {
                Service.logger.Fatal($"Failed to parse rules (got:{rulesSheet?.RowCount ?? 0}, expected:{modDB.mods.Count})");
                return false;
            }

            for (int idx = 0; idx < modDB.mods.Count; idx++)
            {
                var mod = modDB.mods[idx];
                var locStr = locDB.LocRuleNames[mod.GetLocalizationId()];

                locStr.Text = rulesSheet.GetRow(ruleLogicToLuminaMap[idx])?.Name ?? "";
            }

            return true;
        }

        private bool ParseCardTypes()
        {
            var locDB = LocalizationDB.Get();

            var typesSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadCardType>();
            if (typesSheet == null || typesSheet.RowCount != locDB.LocCardTypes.Count)
            {
                Service.logger.Fatal($"Failed to parse rules (got:{typesSheet?.RowCount ?? 0}, expected:{locDB.LocCardTypes.Count})");
                return false;
            }

            foreach (var row in typesSheet)
            {
                var cardType = ConvertToTriadType((byte)row.RowId);
                locDB.mapCardTypes[cardType].Text = row.Name.RawString;
            }

            return true;
        }

        private bool ParseCards()
        {
            var cardDB = TriadCardDB.Get();
            var cardInfoDB = GameCardDB.Get();

            var cardDataSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadCardResident>();
            var cardNameSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadCard>();

            if (cardDataSheet != null && cardNameSheet != null && cardDataSheet.RowCount == cardNameSheet.RowCount)
            {
                var cardTypesSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadCardType>();
                var cardRaritySheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadCardRarity>();
                if (cardTypesSheet == null || cardTypesSheet.RowCount != cardTypeMap.Length)
                {
                    Service.logger.Fatal($"Failed to parse card types (got:{cardTypesSheet?.RowCount ?? 0}, expected:{cardTypeMap.Length})");
                    return false;
                }
                if (cardRaritySheet == null || cardRaritySheet.RowCount != cardRarityMap.Length)
                {
                    Service.logger.Fatal($"Failed to parse card rarities (got:{cardRaritySheet?.RowCount ?? 0}, expected:{cardRarityMap.Length})");
                    return false;
                }

                for (uint idx = 0; idx < cardDataSheet.RowCount; idx++)
                {
                    var rowData = cardDataSheet.GetRow(idx);
                    var rowName = cardNameSheet.GetRow(idx);

                    if (rowData != null && rowName != null && rowData.Top > 0)
                    {
                        var rowTypeId = rowData.TripleTriadCardType.Row;
                        var rowRarityId = rowData.TripleTriadCardRarity.Row;
                        var cardType = (rowTypeId < cardTypeMap.Length) ? cardTypeMap[rowTypeId] : ETriadCardType.None;
                        var cardRarity = (rowRarityId < cardRarityMap.Length) ? cardRarityMap[rowRarityId] : ETriadCardRarity.Common;

                        // i got left & right mixed up at some point...
                        var cardOb = new TriadCard((int)idx, cardRarity, cardType, rowData.Top, rowData.Bottom, rowData.Right, rowData.Left, rowData.Order, rowData.UIPriority);
                        cardOb.Name.Text = rowName.Name.RawString;

                        // shared logic code maps card by their ids directly: cards[id]=card
                        // should be linear and offset by 1 ([0] = empty)
                        int absDiff = (int)Math.Abs(cardDB.cards.Count - idx);
                        if (absDiff > 10)
                        {
                            Service.logger.Fatal($"Failed to assign card data (got:{cardDB.cards.Count}, expected:{idx})");
                            return false;
                        }

                        while (cardDB.cards.Count < idx)
                        {
                            cardDB.cards.Add(null);
                        }
                        cardDB.cards.Add(cardOb);

                        // create matching entry in extended card info db
                        var cardInfo = new GameCardInfo() { CardId = cardOb.Id, SortKey = rowData.SortKey, SaleValue = rowData.SaleValue };
                        cardInfoDB.mapCards.Add(cardOb.Id, cardInfo);
                    }
                }
            }
            else
            {
                Service.logger.Fatal($"Failed to parse card data (D:{cardDataSheet?.RowCount ?? 0}, N:{cardNameSheet?.RowCount ?? 0})");
                return false;
            }

            cardDB.ProcessSameSideLists();
            return true;
        }

        private struct NpcIds
        {
            public uint TriadNpcId;
            public uint ENpcId;
            public string Name;
        }

        private bool ParseNpcs()
        {
            var npcDB = TriadNpcDB.Get();

            // cards & rules can be mapped directly from their respective DBs
            var cardDB = TriadCardDB.Get();
            var modDB = TriadGameModifierDB.Get();

            // name is a bit more annoying to get
            var listTriadIds = new List<uint>();

            var npcDataSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriad>();
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
                Service.logger.Fatal("Failed to parse npc data (missing ids)");
                return false;
            }

            var mapTriadNpcData = new Dictionary<uint, NpcIds>();
            var sheetNpcNames = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.ENpcResident>();
            var sheetENpcBase = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.ENpcBase>();
            if (sheetNpcNames != null && sheetENpcBase != null)
            {
                foreach (var rowData in sheetENpcBase)
                {
                    var triadId = Array.Find(rowData.ENpcData, id => listTriadIds.Contains(id));
                    if (triadId != 0 && !mapTriadNpcData.ContainsKey(triadId))
                    {
                        var rowName = sheetNpcNames.GetRow(rowData.RowId);
                        if (rowName != null)
                        {
                            mapTriadNpcData.Add(triadId, new NpcIds() { ENpcId = rowData.RowId, TriadNpcId = triadId, Name = rowName.Singular.RawString });
                        }
                    }
                }
            }
            else
            {
                Service.logger.Fatal($"Failed to parse npc data (NN:{sheetNpcNames?.RowCount ?? 0}, NB:{sheetENpcBase?.RowCount ?? 0})");
                return false;
            }

            // prep rule id mapping :/
            var ruleLuminaToLogicMap = new int[ruleLogicToLuminaMap.Length];
            for (int idx = 0; idx < ruleLogicToLuminaMap.Length; idx++)
            {
                ruleLuminaToLogicMap[ruleLogicToLuminaMap[idx]] = idx;
            }

            int nameLocId = 0;
            foreach (var rowData in npcDataSheet)
            {
                if (rowData == null)
                {
                    continue;
                }

                if (!mapTriadNpcData.ContainsKey(rowData.RowId))
                {
                    // no name = no npc entry, disabled? skip it
                    continue;
                }

                var listRules = new List<TriadGameModifier>();
                if (rowData.TripleTriadRule != null)
                {
                    foreach (var ruleRow in rowData.TripleTriadRule)
                    {
                        if (ruleRow != null && ruleRow.Row != 0)
                        {
                            if (ruleRow.Row >= modDB.mods.Count)
                            {
                                Service.logger.Fatal($"Failed to parse npc data (rule.id:{ruleRow.Row})");
                                return false;
                            }

                            var logicRule = modDB.mods[ruleLuminaToLogicMap[(int)ruleRow.Row]];
                            listRules.Add(logicRule);

                            var ruleValueOb = ruleRow.Value;
                            if (ruleValueOb == null || ruleValueOb.Name.RawString != logicRule.GetLocalizedName())
                            {
                                Service.logger.Fatal($"Failed to match npc rules! (rule.id:{ruleRow.Row})");
                                return false;
                            }
                        }
                    }
                }

                int numCardsFixed = 0;
                int[] cardsFixed = new int[5];
                if (rowData.TripleTriadCardFixed != null)
                {
                    if (rowData.TripleTriadCardFixed.Length != 5)
                    {
                        Service.logger.Fatal($"Failed to parse npc data (num CF:{rowData.TripleTriadCardFixed.Length})");
                        return false;
                    }

                    for (int cardIdx = 0; cardIdx < rowData.TripleTriadCardFixed.Length; cardIdx++)
                    {
                        var cardRowIdx = rowData.TripleTriadCardFixed[cardIdx].Row;
                        if (cardRowIdx != 0)
                        {
                            if (cardRowIdx >= cardDB.cards.Count)
                            {
                                Service.logger.Fatal($"Failed to parse npc data (card.id:{cardRowIdx})");
                                return false;
                            }

                            cardsFixed[cardIdx] = (int)cardRowIdx;
                            numCardsFixed++;
                        }
                    }
                }

                int numCardsVar = 0;
                int[] cardsVariable = new int[5];
                if (rowData.TripleTriadCardVariable != null)
                {
                    if (rowData.TripleTriadCardVariable.Length != 5)
                    {
                        Service.logger.Fatal($"Failed to parse npc data (num CV:{rowData.TripleTriadCardVariable.Length})");
                        return false;
                    }

                    for (int cardIdx = 0; cardIdx < rowData.TripleTriadCardVariable.Length; cardIdx++)
                    {
                        var cardRowIdx = rowData.TripleTriadCardVariable[cardIdx].Row;
                        if (cardRowIdx != 0)
                        {
                            if (cardRowIdx >= cardDB.cards.Count)
                            {
                                Service.logger.Fatal($"Failed to parse npc data (card.id:{cardRowIdx})");
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
                npcOb.Name.Text = npcIdData.Name;
                npcOb.OnNameUpdated();
                nameLocId++;

                // don't add to noc lists just yet, there are some entries with missing locations that need to be filtered out first!

                var newCachedData = new ENpcCachedData() { triadId = npcIdData.TriadNpcId, gameLogicOb = npcOb, matchFee = rowData.Fee };
                if (rowData.ItemPossibleReward != null && rowData.ItemPossibleReward.Length > 0)
                {
                    newCachedData.rewardItems = new uint[rowData.ItemPossibleReward.Length];
                    for (int rewardIdx = 0; rewardIdx < rowData.ItemPossibleReward.Length; rewardIdx++)
                    {
                        newCachedData.rewardItems[rewardIdx] = rowData.ItemPossibleReward[rewardIdx].Row;
                    }
                }

                mapENpcCache.Add(npcIdData.ENpcId, newCachedData);
            }

            return true;
        }

        private bool ParseNpcAchievements()
        {
            var npcDataSheet = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.TripleTriadResident>();
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
            var sheetLevel = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.Level>();
            if (sheetLevel != null)
            {
                const byte TypeNpc = 8;
                foreach (var row in sheetLevel)
                {
                    if (row.Type == TypeNpc)
                    {
                        if (mapENpcCache.TryGetValue(row.Object, out var npcCache))
                        {
                            npcCache.mapRawCoords = new float[] { row.X, row.Y, row.Z };
                            npcCache.mapId = row.Map.Row;
                            npcCache.territoryId = row.Territory.Row;
                        }
                    }
                }
            }

            var sheetMap = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.Map>();
            if (sheetMap != null)
            {
                foreach (var kvp in mapENpcCache)
                {
                    var mapRow = sheetMap.GetRow(kvp.Value.mapId);
                    if (mapRow != null && kvp.Value.mapRawCoords != null)
                    {
                        kvp.Value.mapCoords = new float[2];
                        kvp.Value.mapCoords[0] = CovertCoordToHumanReadable(kvp.Value.mapRawCoords[0], mapRow.OffsetX, mapRow.SizeFactor);
                        kvp.Value.mapCoords[1] = CovertCoordToHumanReadable(kvp.Value.mapRawCoords[2], mapRow.OffsetY, mapRow.SizeFactor);
                    }
                }
            }

            float CovertCoordToHumanReadable(float Coord, float Offset, float Scale)
            {
                float useScale = Scale / 100.0f;
                float useValue = (Coord + Offset) * useScale;
                return ((41.0f / useScale) * ((useValue + 1024.0f) / 2048.0f)) + 1;
            }

            return true;
        }

        private bool ParseCardRewards()
        {
            var sheetItems = Service.dataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.Item>();
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
                        var itemRow = itemId == 0 ? null : sheetItems.GetRow(itemId);
                        if (itemRow != null)
                        {
                            var cardOb = cardsDB.FindById((int)itemRow.AdditionalData);
                            if (cardOb == null)
                            {
                                // try to match card by bname, remove known prefix/suffix
                                var cardName = itemRow.Name.ToString().Replace("-Karte", "");
                                cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.GetLocalized().Equals(cardName, StringComparison.InvariantCultureIgnoreCase));

                                if (cardOb == null)
                                {
                                    cardName = itemRow.Singular.ToString().Replace("-Karte", "");
                                    cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.GetLocalized().Equals(cardName, StringComparison.InvariantCultureIgnoreCase));

                                    if (cardOb == null)
                                    {
                                        cardName = itemRow.Plural.ToString().Replace("-Karten", "");
                                        cardOb = (cardName.Length < 2) ? null : cardsDB.cards.Find(x => (x != null) && x.Name.GetLocalized().Equals(cardName, StringComparison.InvariantCultureIgnoreCase));
                                    }
                                }
                            }

                            if (cardOb != null)
                            {
                                var cardInfo = gameCardDB.FindById(cardOb.Id);
                                if (cardInfo != null)
                                {
                                    cardInfo.ItemId = itemId;
                                }

                                kvp.Value.rewardCardIds.Add(cardOb.Id);
                            }
                            else
                            {
                                var npcName = (kvp.Value.gameLogicOb != null) ? kvp.Value.gameLogicOb.Name.GetLocalized() : "??";
                                Service.logger.Error($"Failed to parse npc reward data! npc:{kvp.Value.triadId} ({npcName}), rewardId:{itemId} ({itemRow.Name} | {itemRow.Singular})");
                            }
                        }
                    }
                }
            }

            return true;
        }

        private void FinalizeNpcList()
        {
            GameCardDB gameCardDB = GameCardDB.Get();
            GameNpcDB gameNpcDB = GameNpcDB.Get();
            TriadNpcDB npcDB = TriadNpcDB.Get();

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
                    else
                    {
                        // normal and annoying.
                        // Service.logger.Info($"Failed to add triad[{cacheOb.triadId}], enpc[{kvp.Key}], name:{cacheOb.gameLogicOb.Name.GetLocalized()} - no location found!");
                    }

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

                var gameNpcOb = new GameNpcInfo();
                gameNpcOb.npcId = kvp.Value.gameLogicIdx;
                gameNpcOb.triadId = (int)kvp.Value.triadId;
                if (!mapNpcAchievementId.TryGetValue(kvp.Value.triadId, out gameNpcOb.achievementId))
                {
                    Service.logger.Info($"Failed to find achievId for triadId:{kvp.Value.triadId}");
                }

                gameNpcOb.matchFee = kvp.Value.matchFee;
                if (kvp.Value.mapCoords != null)
                {
                    gameNpcOb.Location = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(kvp.Value.territoryId, kvp.Value.mapId, kvp.Value.mapCoords[0], kvp.Value.mapCoords[1]);
                }

                foreach (var cardId in kvp.Value.rewardCardIds)
                {
                    gameNpcOb.rewardCards.Add(cardId);

                    var cardInfo = gameCardDB.FindById(cardId);
                    if (cardInfo == null)
                    {
                        Service.logger.Error($"Failed to match npc reward data! npc:{gameNpcOb.npcId}, key:{kvp.Key}, card:{cardId}");
                        continue;
                    }

                    var npcOb = (gameNpcOb.npcId < npcDB.npcs.Count) ? npcDB.npcs[gameNpcOb.npcId] : null;
                    if (npcOb != null)
                    {
                        cardInfo.RewardNpcs.Add(gameNpcOb.npcId);
                    }
                    else
                    {
                        Service.logger.Error($"Failed to match npc reward data! npc:{gameNpcOb.npcId}, key:{kvp.Key}");
                    }
                }

                gameNpcDB.mapNpcs.Add(gameNpcOb.npcId, gameNpcOb);
            }
        }

        private void FixLocalizedNameCasing()
        {
            var locDB = LocalizationDB.Get();
            var excludedList = new string[] { "the", "goe", "van", "des", "sas", "yae", "tol", "der", "rem" };

            var fixLists = new List<LocString>[] { locDB.LocCardNames, locDB.LocNpcNames };
            foreach (var list in fixLists)
            {
                foreach (var entry in list)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Text))
                    {
                        continue;
                    }

                    string[] tokens = entry.Text.Split(' ');
                    int numChangedTokens = 0;

                    for (int idx = 0; idx < tokens.Length; idx++)
                    {
                        if (tokens[idx].Length > 2 && Array.IndexOf(excludedList, tokens[idx]) < 0)
                        {
                            if (tokens[idx][1] == '\'')
                            {
                                // don't touch, i have no idea how french capitalization work
                                continue;
                            }

                            bool hasLowerCase = char.IsLower(tokens[idx], 0);
                            if (hasLowerCase)
                            {
                                var newToken = tokens[idx].Substring(0, 1).ToUpper() + tokens[idx].Substring(1);
                                tokens[idx] = newToken;
                                numChangedTokens++;
                            }
                        }
                    }

                    if (numChangedTokens > 0)
                    {
                        var newText = string.Join(' ', tokens);
                        //Service.logger.Info($"fixed casing: '{entry.Text}' => '{newText}'");
                        entry.Text = newText;
                    }
                }
            }
        }

        public static ETriadCardType ConvertToTriadType(byte rawType)
        {
            return (rawType < cardTypeMap.Length) ? cardTypeMap[rawType] : ETriadCardType.None;
        }

        public static ETriadCardRarity ConvertToTriadRarity(byte rawRarity)
        {
            return (rawRarity < cardRarityMap.Length) ? cardRarityMap[rawRarity] : ETriadCardRarity.Common;
        }
    }
}
