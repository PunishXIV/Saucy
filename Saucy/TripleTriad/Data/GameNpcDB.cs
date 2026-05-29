using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Collections.Generic;
namespace Saucy.TripleTriad.Data;

public class GameNpcInfo
{
    public int achievementId;

    // call GameNpcDB.Refresh() before reading fields below
    public bool IsBeatenOnce;
    public bool IsCompleted;

    public MapLinkPayload? Location;

    public int matchFee;
    public int npcId;
    public List<int> rewardCards = [];
    public int triadId;

    public uint UnlockQuestId;

    public string? UnlockQuestName;

    public bool IsExcludedFromAchievementTracker => (achievementId == 0xffff);
}

public class GameNpcDB
{
    private static readonly GameNpcDB instance = new();
    public Dictionary<int, GameNpcInfo> mapNpcs = [];

    public UnsafeReaderTriadCards? memReader;

    public static GameNpcDB Get() => instance;

    public void Refresh() => RefreshCompleted();

    public void Refresh(bool completion, bool beatenOnce)
    {
        if (completion)
        {
            RefreshCompleted();
        }
    }

    public void RefreshCompleted()
    {
        var cardInfoDB = GameCardDB.Get();

        foreach (var kvp in mapNpcs)
        {
            var isCompleted = true;

            foreach (var rewardId in kvp.Value.rewardCards)
            {
                if (!cardInfoDB.ownedCardIds.Contains(rewardId))
                {
                    isCompleted = false;
                    break;
                }
            }

            kvp.Value.IsCompleted = isCompleted;
        }
    }

    public void RefreshBeatenOnce()
    {
        foreach (var kvp in mapNpcs)
        {
            kvp.Value.IsBeatenOnce = TriadMemoryReads.IsAvailable &&
                                     TriadMemoryReads.TryIsNpcBeatenOnce(kvp.Value.triadId);
        }
    }
}
