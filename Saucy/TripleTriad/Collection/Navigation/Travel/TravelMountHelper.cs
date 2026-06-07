using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
namespace Saucy.TripleTriad;

internal static unsafe class TravelMountHelper
{
    private const uint GeneralActionMountRoulette = 9;

    public static bool CanMountInCurrentTerritory() =>
        CanMountInTerritory(Svc.ClientState.TerritoryType);

    public static bool CanMountInTerritory(uint territoryId)
    {
        var row = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return row != null && row.Value.Mount;
    }

    public static bool IsFlyingUnlocked()
    {
        var uiState = UIState.Instance();
        return uiState != null && uiState->PlayerState.CanFly;
    }

    public static bool IsMountUnlocked(uint mountId) =>
        mountId != 0 && UIState.Instance()->PlayerState.IsMountUnlocked(mountId);

    public static bool ResolveUseFlying(bool requestedFly, uint? territoryId = null)
    {
        if (!requestedFly)
        {
            return false;
        }

        var resolvedTerritoryId = territoryId ?? Svc.ClientState.TerritoryType;
        if (!CanMountInTerritory(resolvedTerritoryId))
        {
            return false;
        }

        return IsFlyingUnlocked();
    }

    public static bool TryMountUp()
    {
        if (!CanMountInCurrentTerritory())
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.MountOrOrnamentTransition] || Svc.Condition[ConditionFlag.Casting])
        {
            EzThrottler.Throttle("SaucyTravelMountWait", 2000, true);
        }

        if (Svc.Condition[ConditionFlag.Jumping])
        {
            return false;
        }

        if (!EzThrottler.Check("SaucyTravelMountWait"))
        {
            return false;
        }

        var mountId = C.TriadCollection.TravelMountId;
        var actionManager = ActionManager.Instance();
        if (mountId == 0 || !IsMountUnlocked(mountId))
        {
            if (actionManager->GetActionStatus(ActionType.GeneralAction, GeneralActionMountRoulette) != 0)
            {
                return true;
            }

            if (Player.IsAnimationLocked || !EzThrottler.Throttle("SaucyTravelMount"))
            {
                return false;
            }

            actionManager->UseAction(ActionType.GeneralAction, GeneralActionMountRoulette);
            return false;
        }

        if (actionManager->GetActionStatus(ActionType.Mount, mountId) != 0)
        {
            return true;
        }

        if (Player.IsAnimationLocked || !EzThrottler.Throttle("SaucyTravelMount"))
        {
            return false;
        }

        actionManager->UseAction(ActionType.Mount, mountId);
        return false;
    }

    public static bool TryDismount()
    {
        if (!Svc.Condition[ConditionFlag.Mounted])
        {
            return true;
        }

        if (Svc.Condition[ConditionFlag.Jumping] ||
            Svc.Condition[ConditionFlag.MountOrOrnamentTransition] ||
            Player.IsAnimationLocked ||
            !EzThrottler.Throttle("SaucyTravelDismount"))
        {
            return false;
        }

        Chat.ExecuteGeneralAction(23);
        return false;
    }
}
