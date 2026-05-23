using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons;
using ECommons.Automation;
using ECommons.CSExtensions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Saucy.AirForce;

#pragma warning disable IDE0078

public static unsafe class AirForceModule
{
    public static void OnUpdate()
    {
        if (Svc.Condition[ConditionFlag.BoundByDuty95] && GenericHelpers.TryGetAddonByName<AtkUnitBase>("RideShooting", out var addon) && addon->IsReady())
        {
            List<(Vector2 Pos, float radius)> bad = [];
            foreach (var x in Svc.Objects.OfType<IEventObj>().Where(x => x.BaseId == 2015180 || x.BaseId == 2015179 || x.BaseId == 2015178).Where(x => x.AnimationId == 1).OrderBy(Player.DistanceTo))
            {
                if (Svc.GameGui.WorldToScreen(x.Position, out var screen))
                {
                    if (x.BaseId == 2015183)
                    {
                        bad.Add((screen, 4f));
                    }
                    else
                    {
                        foreach (var b in bad)
                        {
                            if (Vector2.Distance(b.Pos, screen) < b.radius) continue;
                        }
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
}
