using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.OutOnALimb.ECEmbedded;
using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Automation.UIInput;

namespace Saucy.OtherGames;
public unsafe class MiniCactpotManager : IDisposable
{
    private int[] ClickedState = [-1, -1, -1, -1, -1, -1, -1, -1, -1,];
    private string LastState = "";
    private bool RadioButtonClicked = false;

    public MiniCactpotManager()
    {
        Svc.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
        ClientState_TerritoryChanged(Svc.ClientState.TerritoryType);
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
        Svc.Framework.Update -= Framework_Update;
    }

    private void Log(string message) => PluginLog.Debug($"[MiniCactpotManager] {message}");

    private void ClientState_TerritoryChanged(ushort obj)
    {
        if (obj == 144)
        {
            Svc.Framework.Update += Framework_Update;
            //Log("Subscribed to framework update");
        }
        else
        {
            Svc.Framework.Update -= Framework_Update;
            //Log("Unsubscribed from framework update");
        }
    }

    private void Framework_Update(object framework)
    {
        if (!Saucy.Config.EnableAutoMiniCactpot) return;
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("LotteryDaily", out var addon) && GenericHelpers.IsAddonReady(addon))
        {
            var reader = new Reader(addon);
            if (reader.Numbers.Contains(-1)) return;
            if (reader.Stage == 0 || reader.Stage == 1)
            {
                for (int i = 0; i < 9; i++)
                {
                    var nodeId = 30 + i;
                    int number = reader.Numbers.ElementAt(i);
                    if (number == 0)
                    {
                        var element = addon->GetNodeById((uint)nodeId);
                        //Log($"Checking {i} {element->MultiplyRed} {element->MultiplyGreen} {element->MultiplyBlue}");
                        if (element->MultiplyRed == 33 && element->MultiplyBlue == 33 && element->MultiplyGreen == 33)
                        {
                            var component = (AtkComponentNode*)element;
                            var button = (AtkComponentCheckBox*)component->Component;
                            var enabled = button->AtkComponentButton.IsEnabled;
                            if (enabled && (LastState != reader.State || ClickedState[i] != -1))
                            {
                                if (EzThrottler.Throttle("ClickMiniCactpot", 100))
                                {
                                    Log($"Clicking {i}");
                                    LastState = reader.State;
                                    ClickedState[i] = reader.Numbers.ElementAt(i);
                                    Callback.Fire(addon, true, 1, i);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            else if (reader.Stage == 2)
            {
                if (!RadioButtonClicked)
                {
                    for (int i = 21; i <= 28; i++)
                    {
                        var btn = addon->GetNodeById((uint)i);
                        if (btn->MultiplyBlue == 33 && btn->MultiplyRed == 33 && btn->MultiplyGreen == 33)
                        {
                            if (EzThrottler.Throttle("ClickMiniCactpot", 100))
                            {
                                btn->GetAsAtkComponentRadioButton()->ClickRadioButton(addon);
                                RadioButtonClicked = true;
                                return;
                            }
                        }
                    }
                }
                else
                {
                    CloseConfirm();
                }
            }
            else if (reader.Stage == 5)
            {
                CloseConfirm();
            }

            void CloseConfirm()
            {
                var confirm = addon->GetButtonNodeById(67);
                if (confirm->IsEnabled)
                {
                    if (EzThrottler.Throttle("ClickMiniCactpot", 100))
                    {
                        confirm->ClickAddonButton(addon);
                    }
                }
            }
        }
        else
        {
            ClickedState = [-1, -1, -1, -1, -1, -1, -1, -1, -1,];
            LastState = "";
            RadioButtonClicked = false;
            EzThrottler.Throttle("ClickMiniCactpot", 1500, true);
        }
    }

    public class Reader(AtkUnitBase* UnitBase, int BeginOffset = 0) : AtkReader(UnitBase, BeginOffset)
    {
        public int Stage => ReadInt(0) ?? -1;
        public string State => ReadString(3);
        public IEnumerable<int> Numbers
        {
            get
            {
                for (int i = 6; i <= 14; i++)
                {
                    yield return ReadInt(i) ?? -1;
                }
            }
        }
    }
}
