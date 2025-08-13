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

    public static int PlaceCardDetour(nint a1)
    {
        return PlaceCardHook.Original(a1);
    }
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
        catch
        {

        }
    }

    public static void RunModule()
    {
        if (Saucy.TTSolver.preGameDecks.Count > 0)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !Saucy.TTSolver.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (Saucy.TTSolver.hasMove)
        {
            PlaceCard(Saucy.TTSolver.moveCardIdx, Saucy.TTSolver.moveBoardIdx);
            return;
        }

        if (Saucy.uiReaderPrep.HasMatchRequestUI)
        {
            AcceptTriadMatch();
        }

        if (Saucy.uiReaderPrep.HasDeckSelectionUI)
        {
            DeckSelect();
        }
    }

    private static unsafe void DeckSelect()
    {
        try
        {
            if (TryGetAddonByName("TripleTriadSelDeck", out AtkUnitBase* addon) && addon->IsVisible && !TryGetAddonByName<AtkUnitBase>("TripleTriad", out var _))
            {

                if (C.SelectedDeckIndex == -1 && !C.UseRecommendedDeck)
                {
                    var button = addon->UldManager.NodeList[3]->GetAsAtkComponentButton();
                    button->ClickAddonButton(addon);
                    return;
                }
                else
                {
                    var deck = C.UseRecommendedDeck ? Saucy.TTSolver.preGameBestId : C.SelectedDeckIndex;
                    var values = stackalloc AtkValue[1];
                    //Deck Index
                    values[0] = new()
                    {
                        Type = ValueType.Int,
                        Int = deck,
                    };
                    addon->FireCallback(1, values);
                    addon->Close(true);
                    return;
                }
            }
        }
        catch
        {

        }
    }

    private static unsafe void AcceptTriadMatch()
    {
        try
        {
            if (TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) && addon->IsVisible && !TryGetAddonByName<AtkUnitBase>("TripleTriad", out var _))
            {
                var button = (AtkComponentButton*)addon->UldManager.NodeList[4];
                button->ClickAddonButton((AtkComponentBase*)addon, 1, EventType.CHANGE);
            }
        }
        catch { }
    }

    public static bool Logout()
    {
        var isLoggedIn = Svc.Condition.Any();
        if (!isLoggedIn) return true;

        Chat.SendMessage("/logout");
        return true;
    }

    public static bool SelectYesLogout()
    {
        var addon = GetSpecificYesno(Svc.Data.GetExcelSheet<Addon>().GetRow(115).Text.ToDalamudString().GetText());
        if (addon == null) return false;
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
                if (addon == null) return null;
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
