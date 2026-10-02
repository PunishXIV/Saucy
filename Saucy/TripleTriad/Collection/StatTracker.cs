using System.Collections.Generic;
namespace Saucy.TripleTriad;

public class StatTracker
{
    private static readonly TriadNpcStatRecord EmptyStats = new();

    private static TriadCollectionSettings Settings => C.TriadCollection;

    public void OnMatchFinished(int npcLogicId, UIStateTriadResults uiState)
    {
        if (!GameNpcDB.Get().mapNpcs.TryGetValue(npcLogicId, out var npcInfo))
        {
            return;
        }

        var savedStats = GetNpcStats(npcInfo);
        if (savedStats == null)
        {
            savedStats = new();
            Settings.NpcStats.Add(npcInfo.triadId, savedStats);
        }

        savedStats.NumCoins += uiState.numMGP;
        savedStats.NumWins += uiState.isWin ? 1 : 0;
        savedStats.NumDraws += uiState.isDraw ? 1 : 0;
        savedStats.NumLosses += uiState.isLose ? 1 : 0;

        if (uiState.isWin && TryResolveRewardCard(npcInfo, uiState.cardItemId, out var cardId))
        {
            savedStats.Cards[cardId] = savedStats.Cards.GetValueOrDefault(cardId) + 1;
        }

        C.Save();
    }

    // The result addon's reward card also covers duplicates; the ownership-diff tracker only sees first-time drops.
    private static bool TryResolveRewardCard(GameNpcInfo npcInfo, uint resultCardItemId, out int cardId)
    {
        var resultCard = resultCardItemId > 0 ? GameCardDB.Get().FindByItemId(resultCardItemId) : null;
        if (resultCard != null && npcInfo.rewardCards.Contains(resultCard.CardId))
        {
            cardId = resultCard.CardId;
            return cardId > 0;
        }

        if (TriadRewardDropTracker.TryGetVerifiedNpcCardDrop(out var droppedCard, resultCardItemId) &&
            droppedCard != null && npcInfo.rewardCards.Contains(droppedCard.CardId))
        {
            cardId = droppedCard.CardId;
            return cardId > 0;
        }

        cardId = -1;
        return false;
    }

    public TriadNpcStatRecord? GetNpcStats(GameNpcInfo npcInfo)
    {
        if (Settings.NpcStats.TryGetValue(npcInfo.triadId, out var savedStats))
        {
            return savedStats;
        }
        return null;
    }

    public TriadNpcStatRecord GetNpcStatsOrDefault(GameNpcInfo npcInfo) => GetNpcStats(npcInfo) ?? EmptyStats;

    public void RemoveNpcStats(GameNpcInfo npcInfo)
    {
        Settings.NpcStats.Remove(npcInfo.triadId);
        C.Save();
    }

    public static bool GetAverageRewardPerMatchDesc(TriadCollectionSettings settings, GameNpcInfo npcInfo, out float avgMGP)
    {
        if (settings.NpcStats.TryGetValue(npcInfo.triadId, out var savedStats))
        {
            var numMatches = savedStats.GetNumMatches();
            if (numMatches > 0)
            {
                var cardDB = TriadCardDB.Get();
                var gameCardDB = GameCardDB.Get();
                var sumNetGain = savedStats.NumCoins - (numMatches * npcInfo.matchFee);

                foreach (var kvp in savedStats.Cards)
                {
                    if (kvp.Key >= 0 && kvp.Key < cardDB.cards.Count && kvp.Value > 0)
                    {
                        var cardOb = cardDB.FindById(kvp.Key);
                        if (cardOb != null && cardOb.IsValid() && gameCardDB.mapCards.TryGetValue(kvp.Key, out var cardInfo))
                        {
                            sumNetGain += kvp.Value * cardInfo.SaleValue;
                        }
                    }
                }

                avgMGP = 1.0f * sumNetGain / numMatches;
                return true;
            }
        }

        avgMGP = 0;
        return false;
    }
}
