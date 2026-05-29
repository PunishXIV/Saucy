using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Linq;
using System.Numerics;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Saucy.CuffACur;

public unsafe class CufModule
{
    public delegate nint UnknownFunction(nint a1, ushort a2, int a3, void* a4);
    public static bool ModuleEnabled = false;
    public static Hook<UnknownFunction>? FuncHook;

    public static nint FuncDetour(nint a1, ushort a2, int a3, void* a4) => FuncHook!.Original(a1, a2, a3, a4);

    public static void RunModule()
    {
        var addon = Svc.GameGui.GetAddonByName("PunchingMachine");
        try
        {
            if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
            {
                if (GenericHelpers.TryGetAddonByName<AddonSelectString>("SelectString", out var startMenu) &&
                    startMenu->AtkUnitBase.IsVisible &&
                    GenericHelpers.IsAddonReady(&startMenu->AtkUnitBase))
                {
                    var entries = new AddonMaster.SelectString(startMenu).Entries;
                    if (entries == null || !entries.Any())
                    {
                        return;
                    }

                    try
                    {
                        entries[0].Select();
                    }
                    catch (Exception ex)
                    {
                        Svc.Log.Verbose(ex, "[CufModule] Failed to select start menu entry");
                    }

                    return;
                }

                if (addon != nint.Zero)
                {
                    var ui = (AtkUnitBase*)addon.Address;

                    if (ui->IsVisible && ui->UldManager.NodeListCount > 18)
                    {
                        var slidingNode = ui->UldManager.NodeList[18];
                        var btn = ui->UldManager.NodeList[10];
                        if (slidingNode->Width is >= 210 and <= 240)
                        {
                            var evt = stackalloc AtkEvent[]
                            {
                                new()
                                {
                                    Node = btn, Target = (AtkEventTarget*)btn, Param = 0, NextEvent = null
                                }
                            };

                            FuncHook ??= Svc.Hook.HookFromAddress<UnknownFunction>(Svc.SigScanner.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 0F B7 FA"), FuncDetour);
                            FuncHook.Original(addon, 0x17, 0, evt);
                            uiReaderGamesResults.SetIsResultsUI(true);
                        }
                    }
                }
            }

            if (!Svc.Condition[ConditionFlag.OccupiedInQuestEvent] && !uiReaderGamesResults.HasResultsUI)
            {
                var cuf = FindNearestCuffMachine();
                if ((nint)cuf == nint.Zero)
                {
                    return;
                }

                var tg = TargetSystem.Instance();
                tg->InteractWithObject(cuf);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[CufModule] RunModule failed");
        }
    }

    private static GameObject* FindNearestCuffMachine()
    {
        GameObject* nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            if (obj.BaseId is not (2005029 or 197370))
            {
                continue;
            }

            var maxDistance = obj.BaseId == 2005029 ? 1f : 4f;
            var distance = GetTargetDistance(obj);
            if (distance > maxDistance || distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearest = (GameObject*)obj.Address;
        }

        return nearest;
    }

    public static float GetTargetDistance(IGameObject target)
    {
        var LocalPlayer = Svc.Objects.LocalPlayer;

        if (LocalPlayer is null)
        {
            return 0;
        }

        if (target.GameObjectId == LocalPlayer.GameObjectId)
        {
            return 0;
        }

        Vector2 position = new(target.Position.X, target.Position.Z);
        Vector2 selfPosition = new(LocalPlayer.Position.X, LocalPlayer.Position.Z);

        return Math.Max(0, Vector2.Distance(position, selfPosition) - target.HitboxRadius - LocalPlayer.HitboxRadius);
    }
}
