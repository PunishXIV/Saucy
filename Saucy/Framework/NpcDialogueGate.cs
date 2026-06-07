using System;
namespace Saucy.Framework;

public static class NpcDialogueGate
{
    public static bool CanAutomateYesno(string scope, bool inTimedFlow) =>
        ObjectHelper.HasInitiatedDialogue(scope) ||
        (inTimedFlow && ObjectHelper.IsTargeting(scope));

    public static void RefreshTimedFlow(
        string scope,
        bool inTimedFlow,
        Action markFlow,
        Func<bool> hasModuleUi)
    {
        if (!ObjectHelper.IsTargeting(scope))
        {
            return;
        }

        if (!inTimedFlow && !ObjectHelper.HasInitiatedDialogue(scope))
        {
            return;
        }

        if (hasModuleUi())
        {
            markFlow();
        }
    }
}
