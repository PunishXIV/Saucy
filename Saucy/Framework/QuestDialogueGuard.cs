using Dalamud.Game.ClientState.Conditions;
namespace Saucy.Framework;

public static class QuestDialogueGuard
{
    public static bool ShouldBlockTalk(bool isAtTrackedNpc) =>
        Svc.Condition[ConditionFlag.OccupiedInQuestEvent] && !isAtTrackedNpc;

    public static bool ShouldBlockYesno(bool isAtTrackedNpc) =>
        Svc.Condition[ConditionFlag.OccupiedInQuestEvent] && !isAtTrackedNpc;
}
