using FFTriadBuddy;

namespace TriadBuddyPlugin
{
    public class StatTracker
    {
        private readonly static Configuration.NpcStatInfo EmptyStats = new();
        private readonly Configuration config;

        public StatTracker(Configuration config)
        {
            this.config = config;
        }

        public void OnMatchFinished(Solver solver, UIStateTriadResults uiState)
        {
            if (!GameNpcDB.Get().mapNpcs.TryGetValue(solver.lastGameNpc?.Id ?? -1, out var npcInfo))
            {
                return;
            }

            var savedStats = GetNpcStats(npcInfo);
            if (savedStats == null)
            {
                savedStats = new();
                config.NpcStats.Add(npcInfo.triadId, savedStats);
            }

            savedStats.NumCoins += uiState.numMGP;
            savedStats.NumWins += uiState.isWin ? 1 : 0;
            savedStats.NumDraws += uiState.isDraw ? 1 : 0;
            savedStats.NumLosses += uiState.isLose ? 1 : 0;

            if (uiState.cardItemId != 0)
            {
                int cardId = -1;

                var gameCardDB = GameCardDB.Get();
                foreach (var kvp in gameCardDB.mapCards)
                {
                    if (kvp.Value.ItemId == uiState.cardItemId)
                    {
                        cardId = kvp.Value.CardId;
                        break;
                    }
                }

                if (cardId > 0)
                {
                    if (savedStats.Cards.TryGetValue(cardId, out var _))
                    {
                        savedStats.Cards[cardId] += 1;
                    }
                    else
                    {
                        savedStats.Cards.Add(cardId, 1);
                    }
                }
            }

            config.Save();

            // consume value to avoid counting if next match is against player
            solver.lastGameNpc = null;
        }

        public Configuration.NpcStatInfo GetNpcStats(GameNpcInfo npcInfo)
        {
            if (config.NpcStats.TryGetValue(npcInfo.triadId, out var savedStats))
            {
                return savedStats;
            }

            return null;
        }

        public Configuration.NpcStatInfo GetNpcStatsOrDefault(GameNpcInfo npcInfo) => GetNpcStats(npcInfo) ?? EmptyStats;

        public void RemoveNpcStats(GameNpcInfo npcInfo)
        {
            config.NpcStats.Remove(npcInfo.triadId);
            config.Save();
        }

        public static bool GetAverageRewardPerMatchDesc(Configuration config, GameNpcInfo npcInfo, out float avgMGP)
        {
            if (config.NpcStats.TryGetValue(npcInfo.triadId, out var savedStats))
            {
                int numMatches = savedStats.GetNumMatches();
                if (numMatches > 0)
                {
                    var cardDB = TriadCardDB.Get();
                    var gameCardDB = GameCardDB.Get();
                    int sumNetGain = savedStats.NumCoins - (numMatches * npcInfo.matchFee);

                    foreach (var kvp in savedStats.Cards)
                    {
                        if (kvp.Key >= 0 && kvp.Key < cardDB.cards.Count && kvp.Value > 0)
                        {
                            var cardOb = cardDB.FindById(kvp.Key);
                            if (cardOb.IsValid() && gameCardDB.mapCards.TryGetValue(kvp.Key, out var cardInfo))
                            {
                                sumNetGain += kvp.Value * cardInfo.SaleValue;
                            }
                        }
                    }

                    avgMGP = (1.0f * sumNetGain / numMatches);
                    return true;
                }
            }

            avgMGP = 0;
            return false;
        }
    }
}
