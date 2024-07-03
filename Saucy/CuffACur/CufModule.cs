using ClickLib.Clicks;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Linq;
using System.Numerics;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Saucy.CuffACur
{
    public unsafe class CufModule
    {
        public static bool ModuleEnabled = false;

        public delegate nint UnknownFunction(nint a1, ushort a2, int a3, void* a4);
        public static Hook<UnknownFunction> FuncHook;
        private static bool ShotFired = false;

        public static nint FuncDetour(nint a1, ushort a2, int a3, void* a4)
        {
            return FuncHook.Original(a1, a2, a3, a4);
        }

        public unsafe static void RunModule()
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
                            ClickSelectString.Using((IntPtr)startMenu).SelectItem1();
                            return;
                        }
                        catch
                        {

                        }
                    }

                    if (addon != IntPtr.Zero)
                    {
                        var ui = (AtkUnitBase*)addon;

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

                            if (slidingNode->Width >= 210 && slidingNode->Width <= 240)
                            {
                                var evt = stackalloc AtkEvent[]
                                {
                                    new()
                                    {
                                        Node = btn,
                                        Target = (AtkEventTarget*)btn,
                                        Param = 0,
                                        NextEvent = null,
                                        Type = (AtkEventType)0x17,
                                        Unk29 = 0,
                                        Flags = 0xDD
                                    }
                                };

                                FuncHook ??= Svc.Hook.HookFromAddress<UnknownFunction>(Svc.SigScanner.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 0F B7 FA"), FuncDetour);
                                FuncHook.Original((nint)addon, 0x17, 0, evt);
                                Saucy.uiReaderGamesResults.SetIsResultsUI(true);

                            }
                        }
                    }
                }

                if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent] && !Saucy.uiReaderGamesResults.HasResultsUI)
                {
                    GameObject* cuf = (GameObject*)Svc.Objects.Where(x => (x.DataId == 2005029 && GetTargetDistance(x) <= 1f) || (x.DataId == 197370 && GetTargetDistance(x) <= 4f)).OrderByDescending(x => GetTargetDistance(x)).FirstOrDefault()?.Address;
                    if ((IntPtr)cuf == IntPtr.Zero)
                        return;

                    ShotFired = false;
                    TargetSystem* tg = TargetSystem.Instance();
                    tg->InteractWithObject(cuf);
                }
            }
            catch (Exception)
            {

            }
        }

        public static float GetTargetDistance(IGameObject target)
        {

            var LocalPlayer = Svc.ClientState.LocalPlayer;

            if (LocalPlayer is null)
                return 0;

            if (target.EntityId == LocalPlayer.EntityId)
                return 0;

            Vector2 position = new(target.Position.X, target.Position.Z);
            Vector2 selfPosition = new(LocalPlayer.Position.X, LocalPlayer.Position.Z);

            return Math.Max(0, Vector2.Distance(position, selfPosition) - target.HitboxRadius - LocalPlayer.HitboxRadius);

        }
    }
}
