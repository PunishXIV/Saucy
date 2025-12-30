using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Utility;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using static ECommons.GenericHelpers;
using static TriadBuddyPlugin.UIReaderTriadGame;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Saucy.TripleTriad;

internal static unsafe class TriadAutomater
{
    public delegate int PlaceCardDelegate(nint addon);

    public static Hook<PlaceCardDelegate> PlaceCardHook;
    public static bool ModuleEnabled = false;
    public static Dictionary<uint, int> TempCardsWonList = [];

    public static bool PlayXTimes = false;
    public static bool PlayUntilCardDrops = false;
    public static int NumberOfTimes = 1;
    public static bool LogOutAfterCompletion = false;
    public static bool PlayUntilAllCardsDropOnce = false;

    public static int PlaceCardDetour(nint a1) => PlaceCardHook.Original(a1);
    public static void PlaceCard(int which, int slot)
    {
        try
        {
            if (TryGetAddonByName("TripleTriad", out AddonTripleTriad* addon))
            {
                Callback.Fire(&addon->AtkUnitBase, true, 14, (uint)slot + ((uint)which << 16));
                addon->AtkUnitBase.Update(0);
                addon->TurnState = 0;
            }
        }
        catch { }
    }

    public static void RunModule()
    {
        if (TTSolver.preGameDecks.Count > 0)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !TTSolver.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (TTSolver.hasMove)
        {
            PlaceCard(TTSolver.moveCardIdx, TTSolver.moveBoardIdx);
            return;
        }

        if (uiReaderPrep.HasMatchRequestUI)
        {
            AcceptTriadMatch();
        }

        if (uiReaderPrep.HasDeckSelectionUI)
        {
            DeckSelect();
        }
    }

    private static void DeckSelect()
    {
        try
        {
            if (TryGetAddonByName("TripleTriadSelDeck", out AtkUnitBase* addon) && addon->IsVisible && !TryGetAddonByName<AtkUnitBase>("TripleTriad", out var _))
            {
                if (C.SelectedDeckIndex == -1 && !C.UseRecommendedDeck)
                {
                    var button = addon->UldManager.NodeList[3]->GetAsAtkComponentButton();
                    button->ClickAddonButton(addon);
                }
                else
                {
                    var deck = C.UseRecommendedDeck ? TTSolver.preGameBestId : C.SelectedDeckIndex;
                    var values = stackalloc AtkValue[1];
                    //Deck Index
                    values[0] = new()
                    {
                        Type = ValueType.Int, Int = deck
                    };
                    addon->FireCallback(1, values);
                    addon->Close(true);
                }
            }
        }
        catch { }
    }

    private static void AcceptTriadMatch()
    {
        try
        {
            if (TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) && IsAddonReady(addon))
            {
                var button = addon->GetComponentButtonById(41);
                if (button->IsEnabled && button->AtkResNode->IsVisible())
                {
                    button->ClickAddonButton(addon);
                }
            }
        }
        catch { }
    }

    public static bool Logout()
    {
        var isLoggedIn = Svc.Condition.Any();
        if (!isLoggedIn)
        {
            return true;
        }

        Chat.SendMessage("/logout");
        return true;
    }

    public static bool SelectYesLogout()
    {
        var addon = GetSpecificYesno(Svc.Data.GetExcelSheet<Addon>().GetRow(115).Text.ToDalamudString().GetText());
        if (addon == null)
        {
            return false;
        }
        new AddonMaster.SelectYesno(addon).Yes();
        return true;
    }

    internal static AtkUnitBase* GetSpecificYesno(params string[] s)
    {
        for (var i = 1; i < 100; i++)
        {
            try
            {
                var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno", i).Address;
                if (addon == null)
                {
                    return null;
                }
                if (IsAddonReady(addon))
                {
                    var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                    var text = MemoryHelper.ReadSeString(&textNode->NodeText).GetText();
                    if (text.EqualsAny(s))
                    {
                        Svc.Log.Verbose($"SelectYesno {s} addon {i}");
                        return addon;
                    }
                }
            }
            catch (Exception e)
            {
                e.Log();
                return null;
            }
        }
        return null;
    }
}
