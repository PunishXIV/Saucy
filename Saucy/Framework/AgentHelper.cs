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
}
