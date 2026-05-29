using Dalamud.Utility;
using ECommons.Automation;
using ECommons.Automation.UIInput;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal static unsafe class TriadAutomater
{
    public delegate int PlaceCardDelegate(nint addon);

    private const int MaxDeckSelectAttemptsPerScreen = 12;
    private const int DeckSelectRetryCooldownFrames = 15;

    public static int DeckSelectFramesOpen { get; private set; }

    public static bool ModuleEnabled = false;
    public static Dictionary<uint, int> TempCardsWonList = [];

    public static bool PlayXTimes = false;
    public static bool PlayUntilCardDrops = false;
    public static int NumberOfTimes = 1;
    public static bool LogOutAfterCompletion = false;
    public static bool PlayUntilAllCardsDropOnce = false;

    public static int MatchesCompletedThisSession = 0;

    private static readonly HashSet<int> attemptedDeckIndices = [];
    private static bool deckSelectScreenActive;
    private static int deckSelectAttemptCount;
    private static int framesSinceRematchAttempt;
    private static int framesSinceMatchAcceptAttempt;
    private static int framesSinceDeckSelectAttempt;
    private static bool rematchPending;

    private const int RematchRetryCooldownFrames = 15;
    private const int MatchAcceptRetryCooldownFrames = 15;

    /// <summary>When true, posts brief /echo lines for triad move debugging.</summary>
    public const bool DebugTriadAutomation = false;

    public static bool PlaceCard(int which, int slot)
    {
        try
        {
            if (!TryGetAddonByName("TripleTriad", out AddonTripleTriad* addon))
            {
                return false;
            }

            Callback.Fire(&addon->AtkUnitBase, true, 14, (uint)slot + ((uint)which << 16));
            addon->AtkUnitBase.Update(0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] PlaceCard failed");
            return false;
        }
    }

    public static void RunModule()
    {
        if (PlayUntilAllCardsDropOnce)
        {
            EnsureRunTargetCards(ResolveRunTargetNpc());
        }

        if (TTSolver.preGameDecks.Count > 0)
        {
            var selectedDeck = C.SelectedDeckIndex;
            if (selectedDeck >= 0 && !TTSolver.preGameDecks.ContainsKey(selectedDeck))
            {
                C.SelectedDeckIndex = -1;
            }
        }

        if (TryRematch())
        {
            return;
        }

        if (IsTriadRequestVisible())
        {
            AcceptTriadMatch();
            return;
        }

        if (IsDeckSelectVisible())
        {
            DeckSelect();
            return;
        }

        if (TryGetAddonByName("TripleTriad", out AddonTripleTriad* triadAddon) &&
            triadAddon->AtkUnitBase.IsVisible)
        {
            uiReaderGame.SyncCurrentFromAddon((nint)triadAddon);

            // Any non-waiting turn state (includes forced-card / masked moves from special rules).
            var canPlace = triadAddon->TurnState != TurnState.Waiting;

            if (canPlace)
            {
                TTSolver.TickPlaceRetryCooldown();
                TTSolver.UpdateGame(uiReaderGame.currentState, automationTick: true);
            }

            if (canPlace && TTSolver.ShouldAttemptPlace())
            {
                if (PlaceCard(TTSolver.moveCardIdx, TTSolver.moveBoardIdx))
                {
                    TTSolver.RecordPlaceAttempt();
                }

                return;
            }

            if (canPlace)
            {
                return;
            }
        }
    }

    private static bool IsTriadRequestVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) && addon->IsVisible;

    private static bool IsDeckSelectVisible() =>
        TryGetAddonByName<AtkUnitBase>("TripleTriadSelDeck", out var addon) && addon->IsVisible;

    public static void RequestRematch() => rematchPending = true;

    public static void ClearRematchPending()
    {
        rematchPending = false;
        framesSinceRematchAttempt = 0;
    }

    public static void BeginAutomationSession()
    {
        MatchesCompletedThisSession = 0;
        ClearRematchPending();
        ResetDeckSelectSession();
        TTSolver.ResetRunTargetNpcSession();
    }

    public static bool IsAutomationFlowActive()
    {
        if (IsTriadRequestVisible() || IsDeckSelectVisible())
        {
            return true;
        }

        if (TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var resultAddon) && resultAddon->IsVisible)
        {
            return true;
        }

        return TryGetAddonByName("TripleTriad", out AddonTripleTriad* triadAddon) &&
               triadAddon->AtkUnitBase.IsVisible;
    }

    private static int lastTargetNpcId = -1;

    public static GameNpcInfo? ResolveRunTargetNpc()
    {
        TTSolver.EnsureRunTargetNpcSynced();

        var npcId = TTSolver.preGameNpc?.Id ?? TTSolver.currentNpc?.Id ?? TTSolver.lastGameNpc?.Id ?? -1;
        if (npcId >= 0 && GameNpcDB.Get().mapNpcs.TryGetValue(npcId, out var npcInfo))
        {
            if (PlayUntilAllCardsDropOnce)
            {
                EnsureRunTargetCards(npcInfo);
            }

            return npcInfo;
        }

        return null;
    }

    public static bool ShouldContinueTriadSession()
    {
        if (PlayXTimes && MatchesCompletedThisSession >= NumberOfTimes)
        {
            return false;
        }

        if (PlayUntilCardDrops && NumberOfTimes <= 0)
        {
            return false;
        }

        if (PlayUntilAllCardsDropOnce)
        {
            var runTargetNpc = ResolveRunTargetNpc();
            EnsureRunTargetCards(runTargetNpc);

            if (runTargetNpc != null && TempCardsWonList.Count == 0)
            {
                return false;
            }

            if (TempCardsWonList.Count > 0 && TempCardsWonList.All(x => x.Value >= NumberOfTimes))
            {
                return false;
            }
        }

        return true;
    }

    public static void EnsureRunTargetCards(GameNpcInfo? npcInfo)
    {
        if (!PlayUntilAllCardsDropOnce || npcInfo == null)
        {
            return;
        }

        if (lastTargetNpcId != npcInfo.npcId)
        {
            TempCardsWonList.Clear();
            lastTargetNpcId = npcInfo.npcId;
        }

        GameCardDB.Get().Refresh();
        foreach (var cardId in npcInfo.rewardCards)
        {
            var cardInfo = GameCardDB.Get().FindById(cardId);
            if (cardInfo == null)
            {
                continue;
            }

            if (!C.OnlyUnobtainedCards || !cardInfo.IsOwned)
            {
                TempCardsWonList.TryAdd((uint)cardId, 0);
            }
        }
    }

    private static bool TryRematch()
    {
        if (!ModuleEnabled ||
            !TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon) ||
            !addon->IsVisible)
        {
            rematchPending = false;
            framesSinceRematchAttempt = 0;
            return false;
        }

        if (!ShouldContinueTriadSession())
        {
            ClearRematchPending();
            addon->Close(true);
            return true;
        }

        if (!rematchPending)
        {
            return false;
        }

        if (framesSinceRematchAttempt > 0)
        {
            framesSinceRematchAttempt--;
            return true;
        }

        try
        {
            var values = stackalloc AtkValue[2];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = 0
            };
            values[1] = new()
            {
                Type = AtkValueType.UInt, UInt = 1
            };
            addon->FireCallback(2, values);

            var rematchButton = addon->GetComponentButtonById(2);
            if (rematchButton == null)
            {
                rematchButton = addon->GetComponentButtonById(3);
            }

            if (rematchButton != null && rematchButton->IsEnabled &&
                rematchButton->AtkResNode != null && rematchButton->AtkResNode->IsVisible())
            {
                try
                {
                    rematchButton->ClickAddonButton(addon);
                }
                catch (Exception clickEx)
                {
                    Svc.Log.Verbose(clickEx, "[TriadAutomater] Rematch button click fallback failed");
                }
            }

            framesSinceRematchAttempt = RematchRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] TryRematch failed");
        }

        return true;
    }

    private static void DeckSelect()
    {
        try
        {
            var onDeckScreen = TryGetAddonByName("TripleTriadSelDeck", out AtkUnitBase* addon) &&
                               addon->IsVisible;

            if (!onDeckScreen)
            {
                ResetDeckSelectSession();
                return;
            }

            DeckSelectFramesOpen++;

            if (!deckSelectScreenActive)
            {
                ResetDeckSelectSession();
                deckSelectScreenActive = true;
            }

            if (framesSinceDeckSelectAttempt > 0)
            {
                framesSinceDeckSelectAttempt--;
                return;
            }

            if (deckSelectAttemptCount >= MaxDeckSelectAttemptsPerScreen)
            {
                return;
            }

            if (!IsAddonReady(addon))
            {
                return;
            }

            if (!TTSolver.TryGetDeckSelectCandidate(C.UseRecommendedDeck, C.SelectedDeckIndex, attemptedDeckIndices,
                    out var deck))
            {
                return;
            }

            if (deck < 0)
            {
                TryRandomDeckButton(addon);
                framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;
                return;
            }

            if (attemptedDeckIndices.Contains(deck))
            {
                return;
            }

            attemptedDeckIndices.Add(deck);
            deckSelectAttemptCount++;
            framesSinceDeckSelectAttempt = DeckSelectRetryCooldownFrames;

            if (deckSelectAttemptCount > 1)
            {
                Svc.Chat.Print($"[Saucy] Retrying with deck {deck + 1}...");
            }
            else
            {
                Svc.Chat.Print($"[Saucy] Selecting deck {deck + 1}...");
            }

            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = deck
            };
            addon->FireCallback(1, values);
            addon->Update(0);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] DeckSelect failed");
        }
    }

    private static void TryRandomDeckButton(AtkUnitBase* addon)
    {
        if (deckSelectAttemptCount >= MaxDeckSelectAttemptsPerScreen)
        {
            return;
        }

        var button = GetRandomDeckButton(addon);
        if (button != null && button->IsEnabled && button->AtkResNode != null && button->AtkResNode->IsVisible())
        {
            deckSelectAttemptCount++;
            Svc.Chat.Print("[Saucy] Using random deck...");
            try
            {
                button->ClickAddonButton(addon);
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "[TriadAutomater] Random deck button click failed");
            }
        }
    }

    private static void ResetDeckSelectSession()
    {
        deckSelectScreenActive = false;
        attemptedDeckIndices.Clear();
        deckSelectAttemptCount = 0;
        framesSinceDeckSelectAttempt = 0;
        DeckSelectFramesOpen = 0;
    }

    private static AtkComponentButton* GetRandomDeckButton(AtkUnitBase* addon)
    {
        var button = addon->GetComponentButtonById(3);
        if (button != null)
        {
            return button;
        }

        if (addon->UldManager.NodeListCount > 3)
        {
            return addon->UldManager.NodeList[3]->GetAsAtkComponentButton();
        }

        return null;
    }

    private static void AcceptTriadMatch()
    {
        try
        {
            if (!TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) || !addon->IsVisible)
            {
                framesSinceMatchAcceptAttempt = 0;
                return;
            }

            if (framesSinceMatchAcceptAttempt > 0)
            {
                framesSinceMatchAcceptAttempt--;
                return;
            }

            if (!IsAddonReady(addon))
            {
                return;
            }

            var values = stackalloc AtkValue[1];
            values[0] = new()
            {
                Type = AtkValueType.Int, Int = 0
            };
            addon->FireCallback(0, values);
            addon->Update(0);

            var challengeButton = addon->GetComponentButtonById(41);
            if (challengeButton != null && challengeButton->IsEnabled &&
                challengeButton->AtkResNode != null && challengeButton->AtkResNode->IsVisible())
            {
                try
                {
                    challengeButton->ClickAddonButton(addon);
                }
                catch (Exception clickEx)
                {
                    Svc.Log.Verbose(clickEx, "[TriadAutomater] Challenge button click fallback failed");
                }
            }

            framesSinceMatchAcceptAttempt = MatchAcceptRetryCooldownFrames;
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[TriadAutomater] AcceptTriadMatch failed");
        }
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
                    var text = textNode->NodeText.GetText();
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
