using System;
namespace Saucy.Framework;

public static class NpcDialogueGate
{
    public static bool CanAutomateYesno(string scope, bool inTimedFlow) =>
        NpcHelper.HasInitiatedDialogue(scope) ||
        (inTimedFlow && NpcHelper.IsTargeting(scope));

    public static void RefreshTimedFlow(
        string scope,
        bool inTimedFlow,
        Action markFlow,
        Func<bool> hasModuleUi)
    {
        if (!NpcHelper.IsTargeting(scope))
        {
            return;
        }

        if (!inTimedFlow && !NpcHelper.HasInitiatedDialogue(scope))
        {
            return;
        }

        if (hasModuleUi())
        {
            markFlow();
        }
    }
}
