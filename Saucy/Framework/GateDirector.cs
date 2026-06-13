using FFXIVClientStructs.FFXIV.Client.Game.GoldSaucer;

namespace Saucy.Framework;

internal static unsafe class GateDirector
{
    public static bool InSaucer => Svc.ClientState.TerritoryType is 144;

    public static bool TryGetDirector(out GFateDirector* director)
    {
        director = null;
        var mgr = GoldSaucerManager.Instance();
        if (mgr is null)
        {
            return false;
        }

        director = mgr->CurrentGFateDirector;
        return director is not null;
    }

    public static bool IsPlayerOnStage()
    {
        if (!TryGetDirector(out var director))
        {
            return false;
        }

        return director->Flags.HasFlag(GFateDirectorFlag.IsJoined) &&
               !director->Flags.HasFlag(GFateDirectorFlag.IsFinished);
    }

    public static Module.GateType GetCurrentGate()
    {
        if (!TryGetDirector(out var director))
        {
            return Module.GateType.None;
        }

        return (Module.GateType)director->GateType;
    }

    public static bool IsInGate(Module.GateType gate) =>
        InSaucer && IsPlayerOnStage() && GetCurrentGate() == gate;
}
