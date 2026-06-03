using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using ECommons.Logging;
using ECommons.Throttlers;
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
    private const string SanityCheckThrottleKey = "Saucy.CuffACur.SanityCheck";
    private const uint GoldSaucerCuffBaseId = 2005029;
    private const uint HousingCuffBaseId = 197370;

    public static bool ModuleEnabled = false;

    public delegate nint UnknownFunction(nint a1, ushort a2, int a3, void* a4);
    public static Hook<UnknownFunction>? FuncHook;

    public static nint FuncDetour(nint a1, ushort a2, int a3, void* a4)
    {
        return FuncHook!.Original(a1, a2, a3, a4);
    }

    public static unsafe void RunModule()
    {
        var prizeMenu = Svc.GameGui.GetAddonByName("GoldSaucerReward", 1);
        var addon = Svc.GameGui.GetAddonByName("PunchingMachine", 1);
        try
        {
            if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent])
            {
                if (ECommons.GenericHelpers.TryGetAddonByName<AddonSelectString>("SelectString", out var startMenu) && startMenu->AtkUnitBase.IsVisible)
                {
                    try
                    {
                        new AddonMaster.SelectString(startMenu).Entries[0].Select();
                        return;
                    }
                    catch
                    {

                    }
                }

                if (addon != IntPtr.Zero)
                {
                    var ui = (AtkUnitBase*)addon.Address;

                    if (ui->IsVisible)
                    {
                        var slidingNode = ui->UldManager.NodeList[18];
                        var btn = ui->UldManager.NodeList[10];
                        var img = btn->GetComponent()->UldManager.NodeList[2];

                        //if (img->Y == -2)
                        //{
                        //    slidingNode->SetWidth(225);
                        //    var values = stackalloc AtkValue[3];
                        //    values[0] = new()
                        //    {
                        //        Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int,
                        //        Int = 11,
                        //    };
                        //    values[1] = new()
                        //    {
                        //        Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int,
                        //        Int = 3,
                        //    };
                        //    values[2] = new()
                        //    {
                        //        Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int,
                        //        Int = 1500,
                        //    };

                        //    if (!ShotFired)
                        //    {
                        //        Dalamud.Logging.PluginLog.Debug($"FIRE");
                        //        ui->FireCallback(3, values);
                        //        ShotFired = true;

                        //        return;
                        //    }
                        //}

                        if (slidingNode->Width is >= 210 and <= 240)
                        {
                            var evt = stackalloc AtkEvent[]
                            {
                                new()
                                {
                                    Node = btn,
                                    Target = (AtkEventTarget*)btn,
                                    Param = 0,
                                    NextEvent = null,
                                   // Type = (AtkEventType)0x17,
                               //    Unk29 = 0,
                                //   Flags = 0xDD
                                }
                            };

                            FuncHook ??= Svc.Hook.HookFromAddress<UnknownFunction>(Svc.SigScanner.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 0F B7 FA"), FuncDetour);
                            FuncHook.Original((nint)addon, 0x17, 0, evt);
                            uiReaderGamesResults.SetIsResultsUI(true);

                        }
                    }
                }
            }

            if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent] && !uiReaderGamesResults.HasResultsUI)
            {
                var cuf = FindNearestCuffMachine();
                if (cuf == null)
                {
                    if (EzThrottler.Throttle(SanityCheckThrottleKey, 1000))
                    {
                        DuoLog.Warning("No Cuff-a-Cur machine nearby (maybe get closer if in front of one).");
                        DisableModule();
                    }

                    return;
                }

                var tg = TargetSystem.Instance();
                tg->InteractWithObject((GameObject*)cuf.Address);
            }
        }
        catch (Exception)
        {

        }
    }

    private static void DisableModule()
    {
        ModuleEnabled = false;
        C.EnableCuffModule = false;
        C.Save();
    }

    private static IGameObject? FindNearestCuffMachine()
    {
        IGameObject? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var obj in Svc.Objects)
        {
            var maxDistance = obj.BaseId switch
            {
                GoldSaucerCuffBaseId => 1f,
                HousingCuffBaseId => 4f,
                _ => 0f
            };
            if (maxDistance <= 0f)
            {
                continue;
            }

            var distance = GetTargetDistance(obj);
            if (distance > maxDistance || distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearest = obj;
        }

        return nearest;
    }

    public static float GetTargetDistance(IGameObject target)
    {
        var LocalPlayer = Svc.Objects.LocalPlayer;

        if (LocalPlayer is null)
            return 0;

        if (target.GameObjectId == LocalPlayer.GameObjectId)
            return 0;

        Vector2 position = new(target.Position.X, target.Position.Z);
        Vector2 selfPosition = new(LocalPlayer.Position.X, LocalPlayer.Position.Z);

        return Math.Max(0, Vector2.Distance(position, selfPosition) - target.HitboxRadius - LocalPlayer.HitboxRadius);
    }
}
