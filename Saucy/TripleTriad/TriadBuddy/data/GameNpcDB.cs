using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Collections.Generic;

namespace TriadBuddyPlugin
{
    public class GameNpcInfo
    {
        public int npcId;
        public int triadId;
        public int achievementId;

        public int matchFee;

        public MapLinkPayload? Location;
        public List<int> rewardCards = new();

        // call GameNpcDB.Refresh() before reading fields below
        public bool IsBeatenOnce;
        public bool IsCompleted;
        public bool IsExcludedFromAchievementTracker => (achievementId == 0xffff);
    }

    public class GameNpcDB
    {
        private static GameNpcDB instance = new();

        public UnsafeReaderTriadCards? memReader;
        public Dictionary<int, GameNpcInfo> mapNpcs = new();

        public static GameNpcDB Get() { return instance; }

        public void Refresh()
        {
            if (memReader != null && !memReader.HasErrors)
            {
                foreach (var kvp in mapNpcs)
                {
                    kvp.Value.IsBeatenOnce = memReader.IsNpcBeaten(kvp.Value.triadId);
                }
            }

            // card search window is already doing GameCardDB refresh before this
            var cardInfoDB = GameCardDB.Get();
            foreach (var kvp in mapNpcs)
            {
                bool isCompleted = true;

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
    }
}
