using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.Data;

public partial class GameDataLoader
{
    private static readonly ETriadCardType[] cardTypeMap = [ETriadCardType.None, ETriadCardType.Primal, ETriadCardType.Scion, ETriadCardType.Beastman, ETriadCardType.Garlean];
    private static readonly ETriadCardRarity[] cardRarityMap = [ETriadCardRarity.Common, ETriadCardRarity.Common, ETriadCardRarity.Uncommon, ETriadCardRarity.Rare, ETriadCardRarity.Epic, ETriadCardRarity.Legendary];
    private static readonly uint[] ruleLogicToLuminaMap = [0, 1, 2, 3, 5, 10, 11, 4, 6, 12, 13, 8, 9, 14, 7, 15];
    private readonly Dictionary<uint, ENpcCachedData> mapENpcCache = [];
    private readonly Dictionary<uint, int> mapNpcAchievementId = [];
    private readonly Dictionary<uint, uint> mapNpcUnlockQuestId = [];
    public bool IsDataReady { get; private set; }

    public async Task StartAsyncWork(CancellationToken cancellationToken)
    {
        for (var retryIdx = 3; retryIdx >= 0; retryIdx--)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            if (!needsRetry)
                break;

            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            Svc.Log.Info("retrying game data parsers...");
        }
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

            IsDataReady = true;
            Svc.Log.Info($"Loaded game data for cards:{cardDB.cards.Count}, npcs:{npcDB.npcs.Count}");
        }
        else
        {
            cardInfoDB.mapCards.Clear();
            cardDB.cards.Clear();
            npcDB.npcs.Clear();
        }

        mapENpcCache.Clear();
        mapNpcAchievementId.Clear();
    }
}
