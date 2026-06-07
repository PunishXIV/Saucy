using FFXIVClientStructs.FFXIV.Component.GUI;
namespace Saucy.OutOnALimb;

internal static unsafe class LimbArcadeTimer
{
    private const int SecondsRemainingIndex = 2;
    public static int? TryGetSecondsRemaining()
    {
        var data = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.GoldSaucerArcadeMachine);
        if (data == null)
        {
            return null;
        }

        return data->IntArray[SecondsRemainingIndex];
    }
}
