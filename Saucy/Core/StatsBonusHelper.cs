using System;
using System.Linq;
namespace Saucy;

public static class StatsBonusHelper
{
    public static int ApplyMgpBonus(int numMgp)
    {
        double multiplier = 1;
        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer == null)
        {
            return numMgp;
        }

        var jackpot = localPlayer.StatusList.FirstOrDefault(x => x.StatusId == 902);
        if (jackpot != null)
        {
            multiplier += (double)jackpot.Param / 100;
        }

        if (localPlayer.StatusList.Any(x => x.StatusId == 1079))
        {
            multiplier += 0.15;
        }

        return (int)Math.Round(Math.Ceiling(numMgp * multiplier), 0);
    }
}
