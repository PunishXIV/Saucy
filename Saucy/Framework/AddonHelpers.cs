using ECommons.Automation.UIInput;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using AgentId = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId;
namespace Saucy.Framework;

public static unsafe class AgentHelper
{
    public static bool IsActive(AgentId agentId)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
        return agent is not null && agent->IsAgentActive();
    }

    public static bool IsAddonOwnedBy(AtkUnitBase* addon, AgentId agentId)
    {
        if (addon is null ||
            !RaptureAtkModule.Instance()->AddonCallbackMapping.TryGetValue(addon->Id, out var callbackEntry, false))
        {
            return false;
        }

        var agent = AgentModule.Instance()->GetAgentByInternalId(agentId);
        return agent == callbackEntry.AgentInterface;
    }
}

public static unsafe class AddonButton
{
    public static bool TryClick(AtkUnitBase* addon, uint nodeId)
    {
        if (addon is null)
        {
            return false;
        }

        return TryClick(addon, addon->GetComponentButtonById(nodeId));
    }

    public static bool TryClick(AtkUnitBase* addon, AtkComponentButton* button, bool requireEnabled = true)
    {
        if (addon is null ||
            button is null ||
            button->AtkResNode is null ||
            !button->AtkResNode->IsVisible())
        {
            return false;
        }

        if (requireEnabled && !button->IsEnabled)
        {
            return false;
        }

        try
        {
            button->ClickAddonButton(addon);
            addon->Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose(ex, "[AddonButton] click failed");
            return false;
        }
    }
}
