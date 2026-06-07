using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AgentId = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId;

namespace Saucy.Framework;

public static unsafe class AgentHelper
{
    public static bool IsActive(AgentId agentId)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
        return agent != null && agent->IsAgentActive();
    }

    public static bool IsAddonOwnedBy(AtkUnitBase* addon, AgentId agentId)
    {
        if (addon == null ||
            !RaptureAtkModule.Instance()->AddonCallbackMapping.TryGetValue(addon->Id, out var callbackEntry, false))
        {
            return false;
        }

        var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
        return agent == callbackEntry.AgentInterface;
    }

    /// <summary>
    ///     True when the agent's managed addon id matches the visible yes/no (AgentInterface.AddonId).
    /// </summary>
    public static bool IsYesnoOwnedByAgent(AgentId agentId, AddonSelectYesno* yesno)
    {
        if (yesno == null)
        {
            return false;
        }

        var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
        if (agent == null)
        {
            return false;
        }

        var managedAddonId = agent->AddonId;
        return managedAddonId != 0 && managedAddonId == yesno->AtkUnitBase.Id;
    }

    public static bool SharesCallbackAgentWith(AtkUnitBase* addon, AtkUnitBase* other)
    {
        if (addon == null || other == null)
        {
            return false;
        }

        var rap = RaptureAtkModule.Instance();
        if (!rap->AddonCallbackMapping.TryGetValue(addon->Id, out var left, false) ||
            !rap->AddonCallbackMapping.TryGetValue(other->Id, out var right, false))
        {
            return false;
        }

        return left.AgentInterface == right.AgentInterface;
    }
}
