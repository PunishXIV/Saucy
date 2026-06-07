using Saucy.AirForce;
using Saucy.Framework;
using System;
namespace Saucy;

public sealed partial class Saucy
{
    private void CheckLimbResults(UIStateLimbResults results)
    {
        if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Limb))
        {
            C.UpdateStats(stats =>
            {
                stats.LimbMGP += StatsBonusHelper.ApplyMgpBonus(results.numMGP);
                stats.LimbGamesPlayed++;
            });

            uiReaderGamesResults.SetIsResultsUI(false);
            C.Save();
            TryDismissGoldSaucerReward();
        }
    }

    private void CheckAirForceResults(UIStateAirForceResults results)
    {
        if (!C.IsModuleEnabled(ModuleNames.AirForceOne) || !AirForceAutomation.ShouldTrackReward)
        {
            return;
        }

        C.UpdateStats(stats =>
        {
            stats.AirForceMGP += StatsBonusHelper.ApplyMgpBonus(results.numMGP);
            stats.AirForceGamesPlayed++;
        });

        AirForceAutomation.ConsumeRewardTracking();
        uiReaderGamesResults.SetIsResultsUI(false);
        C.Save();
    }

    private void CheckCuffResults(UIStateCuffResults obj)
    {
        try
        {
            if (GoldSaucerArcadeMachineHelper.IsEnabled(GoldSaucerArcadeMachine.Cuff))
            {
                C.UpdateStats(stats =>
                {
                    stats.CuffMGP += StatsBonusHelper.ApplyMgpBonus(obj.numMGP);
                    if (obj.isPunishing)
                    {
                        stats.CuffPunishings += 1;
                    }
                    if (obj.isBrutal)
                    {
                        stats.CuffBrutals += 1;
                    }
                    if (obj.isBruising)
                    {
                        stats.CuffBruisings += 1;
                    }

                    stats.CuffGamesPlayed += 1;
                });

                uiReaderGamesResults.SetIsResultsUI(false);
                C.Save();
                TryDismissGoldSaucerReward();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "cuff results handling failed");
        }
    }

    private static void TryDismissGoldSaucerReward() => GoldSaucerRewardHelper.TryDismiss();

    internal void DisableArcadeModule(string moduleName)
    {
        if (moduleName == ModuleNames.CuffACur)
        {
            C.SetModuleEnabled(ModuleNames.CuffACur, false);
            GoldSaucerArcadeRunSession.ClearStopForDutyFinder(GoldSaucerArcadeMachine.Cuff);
            GoldSaucerArcadeFakeBreak.Clear(GoldSaucerArcadeMachine.Cuff);
        }
        else if (moduleName == ModuleNames.OutOnALimb)
        {
            C.SetModuleEnabled(ModuleNames.OutOnALimb, false);
            GoldSaucerArcadeRunSession.ClearStopForDutyFinder(GoldSaucerArcadeMachine.Limb);
            GoldSaucerArcadeFakeBreak.Clear(GoldSaucerArcadeMachine.Limb);
        }

        C.Save();

        if (C.PlaySound)
        {
            PlaySound();
        }

        if (C.LogOutAfterTriadRun)
        {
            ScheduleLogout();
        }
    }

    private void CheckResults(UIStateTriadResults obj)
    {
        if (TriadRunSession.ModuleEnabled)
        {
            C.UpdateStats(stats =>
            {
                stats.GamesPlayedWithSaucy++;
                stats.MGPWon += StatsBonusHelper.ApplyMgpBonus(obj.numMGP);

                var npcName = TriadRun.lastGameNpc?.Name ?? "Unknown";
                if (stats.NPCsPlayed.TryGetValue(npcName, out var plays))
                {
                    stats.NPCsPlayed[npcName] += 1;
                }
                else
                {
                    stats.NPCsPlayed.TryAdd(npcName, 1);
                }

                if (obj.isLose)
                {
                    stats.GamesLostWithSaucy++;
                }
                if (obj.isDraw)
                {
                    stats.GamesDrawnWithSaucy++;
                }
            });

            if (obj.isWin)
            {
                C.UpdateStats(stats => stats.GamesWonWithSaucy++);

                if (TriadCardFarmSession.IsModeActive())
                {
                    TriadCardFarmSession.DetectAndProcessDrops(obj.cardItemId);
                    if (!TriadCardFarmSession.IsComplete() &&
                        TriadCardFarmSession.ShouldScheduleDropVerification(obj.cardItemId))
                    {
                        TriadCardFarmSession.ScheduleDropVerification(obj.cardItemId);
                    }
                }
                else if (TriadRunSession.PlayUntilCardDrops && obj.cardItemId > 0)
                {
                    var droppedCard = GameCardDB.Get().FindByItemId(obj.cardItemId);
                    if (droppedCard != null)
                    {
                        TriadRewardDropTracker.ProcessVerifiedCardDrop(droppedCard);
                    }
                    else
                    {
                        TriadRewardDropTracker.NotifyPlayUntilAnyCardDropped();
                    }

                    if (!TriadRunSession.ShouldContinue())
                    {
                        TriadRematchAutomation.RequestSessionEndDismiss();
                    }
                }
                else if (TriadRewardDropTracker.TryGetVerifiedNpcCardDrop(out var droppedCard, obj.cardItemId) &&
                         droppedCard != null)
                {
                    TriadRewardDropTracker.ProcessVerifiedCardDrop(droppedCard);
                }
            }

            TriadRematchAutomation.RecordMatchResultIfNeeded();

            C.Save();
        }
    }
}
