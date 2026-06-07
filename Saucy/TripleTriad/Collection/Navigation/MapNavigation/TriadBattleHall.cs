using Dalamud.Game.Text.SeStringHandling.Payloads;
namespace Saucy.TripleTriad;

internal static class TriadBattleHall
{
    public const uint TerritoryId = 579;

    public const string NavigationBlockedMessage =
        "[Saucy] The Battlehall is a Duty Finder instance.\nSaucy cannot path there — enter via Duty Finder.";

    public static bool IsBattleHallTerritory(uint territoryId) => territoryId == TerritoryId;

    public static bool IsBattleHallLocation(MapLinkPayload location) =>
        IsBattleHallTerritory(location.TerritoryType.RowId);

    public static bool TryGetNpcInfo(TriadNpc npc, out GameNpcInfo? info)
    {
        info = null;
        if (npc == null)
        {
            return false;
        }

        return GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out info);
    }

    public static bool IsBattleHallNpc(TriadNpc npc) =>
        TryGetNpcInfo(npc, out var info) && IsBattleHallNpc(info!);

    public static bool IsBattleHallNpc(GameNpcInfo info) =>
        info?.Location != null && IsBattleHallLocation(info.Location);

    public static bool IsBattleHallCard(GameCardInfo cardInfo) =>
        cardInfo != null &&
        cardInfo.RewardNpcs.Count > 0 &&
        cardInfo.RewardNpcs.TrueForAll(IsBattleHallRewardNpc);

    private static bool IsBattleHallRewardNpc(int npcId) =>
        GameNpcDB.Get().mapNpcs.TryGetValue(npcId, out var info) && IsBattleHallNpc(info);

    public static bool ShouldBlockMapNavigation(TriadNpc? npc, MapLinkPayload? location) =>
        (npc != null && IsBattleHallNpc(npc)) ||
        (location != null && IsBattleHallLocation(location));

    public static void PrintNavigationBlocked() => Svc.Chat.PrintError(NavigationBlockedMessage);
}
