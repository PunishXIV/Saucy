using ClickLib.Enums;
using ClickLib.Structures;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Runtime.InteropServices;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad
{
    internal static unsafe class TriadAutomater
    {

        public delegate int PlaceCardDelegate(IntPtr addon);
        public static Hook<PlaceCardDelegate> PlaceCardHook;
        public static bool ModuleEnabled = false;

        public static bool PlayXTimes = false;
        public static bool PlayUntilCardDrops = false;
        public static int NumberOfTimes = 1;

        public static int PlaceCardDetour(IntPtr a1)
        {
            return PlaceCardHook.Original(a1);
        }
        public static void PlaceCard(int which, int slot)
        {
            if (TryGetAddonByName<AtkUnitBase>("TripleTriad", out var addon))
            {
                var values = stackalloc AtkValue[2];
                values[0] = new()
                {
                    Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int,
                    Int = 14,
                };
                values[1] = new()
                {
                    Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt,
                    UInt = (uint)slot + ((uint)which << 16),
                };
                addon->FireCallback(2, values);

                PlaceCardHook ??= Hook<PlaceCardDelegate>.FromAddress(Svc.SigScanner.ScanText("40 56 48 83 EC 20 48 8B F1 E8 ?? ?? ?? ?? 83 BE"), PlaceCardDetour);
                PlaceCardHook.Original((IntPtr)addon);
            }
        }

        public static unsafe void ClickButton(AtkUnitBase* window, AtkComponentButton* target, uint which, EventType type = EventType.CHANGE)
    => ClickAddonComponent(window, target->AtkComponentBase.OwnerNode, which, type);

        public static unsafe void ClickAddonCheckBox(AtkUnitBase* window, AtkComponentCheckBox* target, uint which, EventType type = EventType.CHANGE)
             => ClickAddonComponent(window, target->AtkComponentButton.AtkComponentBase.OwnerNode, which, type);


        public static unsafe void ClickAddonComponent(AtkUnitBase* UnitBase, AtkComponentNode* target, uint which, EventType type, EventData? eventData = null, InputData? inputData = null)
        {
            eventData ??= EventData.ForNormalTarget(target, UnitBase);
            inputData ??= InputData.Empty();

            InvokeReceiveEvent(&UnitBase->AtkEventListener, type, which, eventData, inputData);
        }

        /// <summary>
        /// AtkUnitBase receive event delegate.
        /// </summary>
        /// <param name="eventListener">Type receiving the event.</param>
        /// <param name="evt">Event type.</param>
        /// <param name="which">Internal routing number.</param>
        /// <param name="eventData">Event data.</param>
        /// <param name="inputData">Keyboard and mouse data.</param>
        /// <returns>The addon address.</returns>
        internal unsafe delegate IntPtr ReceiveEventDelegate(AtkEventListener* eventListener, EventType evt, uint which, void* eventData, void* inputData);


        /// <summary>
        /// Invoke the receive event delegate.
        /// </summary>
        /// <param name="eventListener">Type receiving the event.</param>
        /// <param name="type">Event type.</param>
        /// <param name="which">Internal routing number.</param>
        /// <param name="eventData">Event data.</param>
        /// <param name="inputData">Keyboard and mouse data.</param>
        private static unsafe void InvokeReceiveEvent(AtkEventListener* eventListener, EventType type, uint which, EventData eventData, InputData inputData)
        {
            var receiveEvent = GetReceiveEvent(eventListener);
            receiveEvent(eventListener, type, which, eventData.Data, inputData.Data);
        }

        private static unsafe ReceiveEventDelegate GetReceiveEvent(AtkEventListener* listener)
        {
            var receiveEventAddress = new IntPtr(listener->vfunc[2]);
            return Marshal.GetDelegateForFunctionPointer<ReceiveEventDelegate>(receiveEventAddress)!;
        }

        private static unsafe ReceiveEventDelegate GetReceiveEvent(AtkComponentBase* listener)
            => GetReceiveEvent(&listener->AtkEventListener);

        private static unsafe ReceiveEventDelegate GetReceiveEvent(AtkUnitBase* listener)
            => GetReceiveEvent(&listener->AtkEventListener);

        public static void RunModule()
        {

            if (Saucy.TTSolver.preGameDecks.Count > 0)
            {
                var selectedDeck = Saucy.Configuration.SelectedDeckIndex;
                if (selectedDeck >= 0 && !Saucy.TTSolver.preGameDecks.ContainsKey(selectedDeck))
                {
                    Saucy.Configuration.SelectedDeckIndex = -1;
                }
            }

            if (Saucy.TTSolver.hasMove)
            {
                PlaceCard(Saucy.TTSolver.moveCardIdx, Saucy.TTSolver.moveBoardIdx);
            }

            //Challenge Screen
            {
                if (TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon))
                {
                    var button = (AtkComponentButton*)addon->UldManager.NodeList[4];
                    ClickButton(addon, button, 1);
                }
            }

            //Deck Select
            {
                if (TryGetAddonByName<AtkUnitBase>("TripleTriadSelDeck", out var addon) && addon->IsVisible && !TryGetAddonByName<AtkUnitBase>("TripleTriad", out var _))
                {

                    if (Saucy.Configuration.UseRecommendedDeck || Saucy.Configuration.SelectedDeckIndex == -1)
                    {
                        var button = (AtkComponentButton*)addon->UldManager.NodeList[3];
                        ClickButton(addon, button, 2);
                    }
                    else
                    {
                        var values = stackalloc AtkValue[1];
                        //Deck Index
                        values[0] = new()
                        {
                            Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int,
                            Int = Saucy.Configuration.SelectedDeckIndex,
                        };
                        addon->FireCallback(1, values);
                        addon->Hide(true);
                    }
                }
            }
        }
    }
}
