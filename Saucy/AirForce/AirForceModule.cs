using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using ECommons.Automation;
using ECommons.CSExtensions;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Saucy.AirForce;

public static unsafe class AirForceModule
{
    public static void OnUpdate()
    {
        if (Svc.Condition[ConditionFlag.BoundByDuty95] && GenericHelpers.TryGetAddonByName<AtkUnitBase>("RideShooting", out var addon) && addon->IsReady())
        {
            foreach (var x in Svc.Objects.OfType<IEventObj>().Where(x => x.BaseId.EqualsAny<uint>(
                2009678,
                2009676,
                2009677,
                2009679,
                2015180,
                2015179,
                2015178,
                2015183
                )).Where(x => x.AnimationId == 1).OrderBy(Player.DistanceTo))
            {
                if (x.BaseId.EqualsAny<uint>(
                    2015183,
                    2009679
                    ))
                {
                    continue;
                }
                if (Svc.GameGui.WorldToScreen(x.Position, out var screen))
                {
                    if (EzThrottler.Throttle("Shoot", 250))
                    {
                        var a = *(nint*)((nint)(AgentModule.Instance()->GetAgentByInternalId(AgentId.RideShooting)) + 48);
                        *(float*)(a + 3184) = screen.X;
                        *(float*)(a + 3188) = screen.Y;
                        Svc.Framework.RunOnTick(() =>
                        {
                            _ = WindowsKeypress.SendKeypress(ECommons.WindowsFormsReflector.Keys.Space);
                        }
                        , delayTicks: 1);
                        break;
                    }
                }
            }
        }
    }
}
