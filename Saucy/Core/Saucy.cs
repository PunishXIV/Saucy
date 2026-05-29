using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.SimpleGui;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NAudio.Wave;
using PunishLib;
using Saucy.AirForce;
using Saucy.CuffACur;
using Saucy.Framework;
using Saucy.IPC;
using Saucy.OutOnALimb;
using Saucy.TripleTriad;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static ECommons.GenericHelpers;
using Module = ECommons.Module;

namespace Saucy;

public sealed class Saucy : IDalamudPlugin
{
    private const string commandName = "/saucy";
    public static Saucy P = null!;

    public static Solver TTSolver = new();

    public static UIReaderTriadGame uiReaderGame = null!;
    public static UIReaderTriadPrep uiReaderPrep = null!;
    public static UIReaderScheduler uiReaderScheduler = null!;
    public static UIReaderGamesResults uiReaderGamesResults = null!;
    public static GameDataLoader dataLoader = null!;
    public static ModuleManager ModuleManager = null!;

    private static Dictionary<uint, int>? cardIdByItemId;

    private readonly object _lockObj = new();
    private Mp3FileReader? _currentReader;
    private WaveOutEvent? _currentWaveOut;

    private TriadCollectionHost? _triadCollectionHost;
    private readonly PluginUI _pluginUi = new();
    private bool _autoOpenedForTriadFlow;

    public LimbManager LimbManager = null!;
    public Saucy(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.All);
        PunishLibMain.Init(pluginInterface, "Saucy", new AboutPlugin
        {
            Sponsor = "https://ko-fi.com/taurenkey"
        });
        EzConfig.Migrate<Configuration>();
        C = EzConfig.Init<Configuration>();
        C.MigrateModuleSettings();
        P = this;

        EzConfigGui.Init(_pluginUi);
        Svc.PluginInterface.UiBuilder.OpenMainUi += EzConfigGui.Open;

        Svc.Commands.AddHandler(commandName, new(OnCommand)
        {
            HelpMessage = "Opens the Saucy menu."
        });

        dataLoader = new();
        dataLoader.StartAsyncWork();

        TTSolver.profileGS = new();

        uiReaderGame = new();
#pragma warning disable CS8622 // Nullability mismatch in vendored delegate
        uiReaderGame.OnUIStateChanged += TTSolver.UpdateGame;
#pragma warning restore CS8622

        uiReaderPrep = new()
        {
            shouldScanDeckData = (TTSolver.profileGS == null) || TTSolver.profileGS.HasErrors
        };
        uiReaderPrep.OnUIStateChanged += TTSolver.UpdateDecks;
        uiReaderPrep.OnMatchRequestChanged += OnTriadPrepUiChanged;
        uiReaderPrep.OnDeckSelectionChanged += OnTriadPrepUiChanged;

        var uiReaderMatchResults = new UIReaderTriadResults();
        uiReaderMatchResults.OnUpdated += CheckResults;

        uiReaderGamesResults = new(Svc.GameGui);
        uiReaderGamesResults.OnCuffUpdated += CheckCuffResults;
        uiReaderGamesResults.OnLimbUpdated += CheckLimbResults;

        uiReaderScheduler = new(Svc.GameGui);
        uiReaderScheduler.AddObservedAddon(uiReaderGame);
        uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
        uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
        uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);
        uiReaderScheduler.AddObservedAddon(uiReaderGamesResults);

        LimbManager = new(C.LimbConfig);
        ModuleManager = new();
        C.EnabledModules.CollectionChanged += OnChange;

        _triadCollectionHost = new(pluginInterface);

        SubscriptionManager.Prepare();
        SubscriptionManager.Subscribe();
        Svc.Framework.Update += RunBot;
    }
    public string Name => "Saucy";
    public static Configuration C { get; private set; } = null!;

    public void Dispose()
    {
        Svc.Commands.RemoveHandler(commandName);
        Svc.PluginInterface.UiBuilder.OpenMainUi -= EzConfigGui.Open;
        Svc.Framework.Update -= RunBot;
        _triadCollectionHost?.Dispose();
        SubscriptionManager.DisposeAll();
        _triadCollectionHost = null;
        lock (_lockObj) { DisposeAudio(); }
        CufModule.FuncHook?.Dispose();
        ModuleManager.Dispose();
        ECommonsMain.Dispose(); //Don't forget!
        P = null!; //necessary to free the reference for GC
    }

    private void OnChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var m in ModuleManager.Modules)
        {
            if (C.EnabledModules.Contains(m.InternalName) && !m.IsEnabled)
            {
                m.EnableInternal();
            }

            if (!C.EnabledModules.Contains(m.InternalName) && m.IsEnabled)
            {
                m.DisableInternal();
            }
        }
    }

    private void CheckLimbResults(UIStateLimbResults results)
    {
        if (LimbManager.Cfg.EnableLimb)
        {
            C.UpdateStats(stats =>
            {
                stats.LimbMGP += GetBonusMGP(results.numMGP);
                stats.LimbGamesPlayed++;
            });

            uiReaderGamesResults.SetIsResultsUI(false);
            C.Save();
        }
    }

    private void CheckCuffResults(UIStateCuffResults obj)
    {
        try
        {
            if (CufModule.ModuleEnabled)
            {
                C.UpdateStats(stats =>
                {
                    stats.CuffMGP += GetBonusMGP(obj.numMGP);
                    if (obj.isPunishing)
                    {
                        stats.CuffPunishings += 1;
                    }
                    if (obj.isBrutal)
                    {
                        stats.CuffBrutals += 1;
                    }
                    if (obj.isBruising)
                    {
                        stats.CuffBruisings += 1;
                    }

                    stats.CuffGamesPlayed += 1;
                });

                if (TriadAutomater.PlayXTimes)
                {
                    TriadAutomater.NumberOfTimes--;
                }

                if (TriadAutomater.NumberOfTimes == 0 && TriadAutomater.PlayXTimes)
                {
                    TriadAutomater.NumberOfTimes = 1;
                    DisableCuffModule();
                    if (C.PlaySound)
                    {
                        PlaySound();
                    }

                    if (TriadAutomater.LogOutAfterCompletion)
                    {
                        ScheduleLogout();
                    }
                }

                uiReaderGamesResults.SetIsResultsUI(false);
                C.Save();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "cuff results handling failed");
        }
    }

    private void CheckResults(UIStateTriadResults obj)
    {
        if (TriadAutomater.ModuleEnabled)
        {
            C.UpdateStats(stats =>
            {
                stats.GamesPlayedWithSaucy++;
                stats.MGPWon += GetBonusMGP(obj.numMGP);

                var npcName = TTSolver.lastGameNpc?.Name ?? "Unknown";
                if (stats.NPCsPlayed.TryGetValue(npcName, out var plays))
                {
                    stats.NPCsPlayed[npcName] += 1;
                }
                else
                {
                    stats.NPCsPlayed.TryAdd(npcName, 1);
                }

                if (obj.isLose)
                {
                    stats.GamesLostWithSaucy++;
                }
                if (obj.isDraw)
                {
                    stats.GamesDrawnWithSaucy++;
                }
            });

            if (TriadAutomater.PlayXTimes)
            {
                TriadAutomater.MatchesCompletedThisSession++;
            }

            if (obj.isWin)
            {
                C.UpdateStats(stats => stats.GamesWonWithSaucy++);
                if (obj.cardItemId > 0)
                {
                    if (TriadAutomater.PlayUntilCardDrops)
                    {
                        TriadAutomater.NumberOfTimes--;
                    }

                    C.UpdateStats(stats => stats.CardsDroppedWithSaucy++);

                    var cardInfo = FindCardByItemId(obj.cardItemId);
                    if (cardInfo is not null)
                    {
                        C.UpdateStats(stats =>
                        {
                            if (stats.CardsWon.ContainsKey((uint)cardInfo.CardId))
                            {
                                stats.CardsWon[(uint)cardInfo.CardId] += 1;
                            }
                            else
                            {
                                stats.CardsWon[(uint)cardInfo.CardId] = 1;
                            }
                        });

                        if (TriadAutomater.TempCardsWonList.ContainsKey((uint)cardInfo.CardId))
                        {
                            TriadAutomater.TempCardsWonList[(uint)cardInfo.CardId] += 1;
                        }
                    }
                }
            }

            if (TriadAutomater.ShouldContinueTriadSession())
            {
                TriadAutomater.RequestRematch();
            }
            else
            {
                TriadAutomater.ClearRematchPending();
            }

            C.Save();
        }
    }

    private int GetBonusMGP(int numMGP)
    {
        double multiplier = 1;
        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer == null)
        {
            return numMGP;
        }

        //Jackpot
        var jackpot = localPlayer.StatusList.FirstOrDefault(x => x.StatusId == 902);
        if (jackpot != null)
        {
            multiplier += (double)jackpot.Param / 100;
        }

        //MGP Card
        if (localPlayer.StatusList.Any(x => x.StatusId == 1079))
        {
            multiplier += 0.15;
        }

        var bonusMGP = Math.Round(Math.Ceiling(numMGP * multiplier), 0);
        return (int)bonusMGP;
    }

    private unsafe void Rematch()
    {
        TriadAutomater.RequestRematch();
    }

    private static GameCardInfo? FindCardByItemId(uint itemId)
    {
        cardIdByItemId ??= BuildCardIdByItemIdLookup();
        return cardIdByItemId.TryGetValue(itemId, out var cardId) ? GameCardDB.Get().FindById(cardId) : null;
    }

    private static Dictionary<uint, int> BuildCardIdByItemIdLookup()
    {
        var lookup = new Dictionary<uint, int>();
        foreach ((var _, var cardInfo) in GameCardDB.Get().mapCards)
        {
            if (cardInfo.ItemId != 0)
            {
                lookup[cardInfo.ItemId] = cardInfo.CardId;
            }
        }

        return lookup;
    }

    private static void DisableCuffModule()
    {
        C.EnableCuffModule = false;
        if (C.EnabledModules.Contains("CuffACurModule"))
        {
            C.EnabledModules.Remove("CuffACurModule");
        }
    }

    private static void ScheduleLogout() =>
        _ = PerformLogout().ContinueWith(
            task =>
            {
                if (task.Exception is not null)
                {
                    Svc.Log.Error(task.Exception, "[Saucy] Logout after completion failed");
                }
            },
            TaskScheduler.Default);

    private void RunBot(IFramework framework)
    {
        try
        {
            SubscriptionManager.Subscribe();
            TriadMapNavigation.Tick();

            var deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
            uiReaderScheduler.Update(deltaSeconds);
            if (TriadAutomater.IsAutomationFlowActive() || uiReaderGame.IsVisible)
            {
                TTSolver.EnsureRunTargetNpcSynced();
            }

            UpdateTriadAutoOpen();

            if (C.AirForceEnabled)
            {
                AirForceModule.OnUpdate();
            }

            if (CufModule.ModuleEnabled)
            {
                CufModule.RunModule();
                return;
            }

            if (TriadAutomater.ModuleEnabled)
            {
                if (!TriadAutomater.ShouldContinueTriadSession() && !TriadAutomater.IsAutomationFlowActive())
                {
                    if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
                        Svc.Condition[ConditionFlag.Occupied33] ||
                        Svc.Condition[ConditionFlag.OccupiedInEvent] ||
                        Svc.Condition[ConditionFlag.Occupied30] ||
                        Svc.Condition[ConditionFlag.Occupied38] ||
                        Svc.Condition[ConditionFlag.Occupied39] ||
                        Svc.Condition[ConditionFlag.OccupiedSummoningBell] ||
                        Svc.Condition[ConditionFlag.WatchingCutscene] ||
                        Svc.Condition[ConditionFlag.Mounting71] ||
                        Svc.Condition[ConditionFlag.CarryingObject])
                    {
                        SkipDialogue();
                    }
                    else
                    {
                        if (C.PlaySound)
                        {
                            PlaySound();
                        }

                        TriadAutomater.PlayXTimes = false;
                        TriadAutomater.PlayUntilCardDrops = false;
                        TriadAutomater.PlayUntilAllCardsDropOnce = false;
                        TriadAutomater.ModuleEnabled = false;
                        TriadAutomater.TempCardsWonList.Clear();
                        TriadAutomater.MatchesCompletedThisSession = 0;
                        TriadAutomater.ClearRematchPending();

                        if (TriadAutomater.LogOutAfterCompletion)
                        {
                            ScheduleLogout();
                        }
                    }

                    return;
                }

                TriadAutomater.RunModule();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "bot update failed");
        }
    }

    private void OnTriadPrepUiChanged(bool isActive)
    {
        if (isActive)
        {
            UpdateTriadAutoOpen();
        }
    }

    private void UpdateTriadAutoOpen()
    {
        if (!C.OpenAutomatically || TriadAutomater.ModuleEnabled)
        {
            _autoOpenedForTriadFlow = false;
            return;
        }

        if (!IsTriadFlowActive())
        {
            _autoOpenedForTriadFlow = false;
            return;
        }

        if (_autoOpenedForTriadFlow)
        {
            return;
        }

        _pluginUi.OpenForTriad();
        _autoOpenedForTriadFlow = true;
    }

    private static bool IsTriadFlowActive()
    {
        if (uiReaderPrep.HasMatchRequestUI ||
            uiReaderPrep.HasDeckSelectionUI ||
            uiReaderGame.IsVisible)
        {
            return true;
        }

        return IsTriadAddonVisible("TripleTriadRequest") ||
               IsTriadAddonVisible("TripleTriadSelDeck") ||
               IsTriadAddonVisible("TripleTriad");
    }

    private static unsafe bool IsTriadAddonVisible(string addonName)
    {
        if (!TryGetAddonByName<AtkUnitBase>(addonName, out var addon))
        {
            return false;
        }

        return addon->IsVisible;
    }

    private unsafe void SkipDialogue()
    {
        try
        {
            var talk = Svc.GameGui.GetAddonByName("Talk");
            if (talk == nint.Zero)
            {
                return;
            }
            var talkAddon = (AtkUnitBase*)talk.Address;
            if (!IsAddonReady(talkAddon))
            {
                return;
            }
            new AddonMaster.Talk(talk).Click();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[Saucy] SkipDialogue failed");
        }
    }

    private static async Task PerformLogout()
    {
        await Svc.Framework.RunOnTick(TriadAutomater.Logout, TimeSpan.FromMilliseconds(2000));
        await Svc.Framework.RunOnTick(TriadAutomater.SelectYesLogout, TimeSpan.FromMilliseconds(3500));
    }

    private void PlaySound()
    {
        lock (_lockObj)
        {
            DisposeAudio();

            var sound = C.SelectedSound;
            var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds", $"{sound}.mp3");
            if (!File.Exists(path))
            {
                return;
            }

            _currentReader = new(path);
            _currentWaveOut = new();
            _currentWaveOut.PlaybackStopped += OnPlaybackStopped;
            _currentWaveOut.Init(_currentReader);
            _currentWaveOut.Play();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lockObj)
        {
            DisposeAudio();
        }
    }

    private void DisposeAudio()
    {
        if (_currentWaveOut != null)
        {
            _currentWaveOut.PlaybackStopped -= OnPlaybackStopped;
            _currentWaveOut.Dispose();
            _currentWaveOut = null;
        }
        if (_currentReader != null)
        {
            _currentReader.Dispose();
            _currentReader = null;
        }
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Length == 0)
        {
            EzConfigGui.Toggle();
            return;
        }

        var args = arguments.Split();
        if (args.Length < 2 || args[0].ToLower() != "tt")
        {
            return;
        }

        var subCommand = args[1].ToLower();

        if (subCommand == "go")
        {
            TriadAutomater.BeginAutomationSession();
            TriadAutomater.ModuleEnabled = true;
            Svc.Chat.Print("[Saucy] Triad Module Enabled!");
            return;
        }

        if (subCommand == "stop")
        {
            TriadAutomater.ModuleEnabled = false;
            Svc.Chat.Print("[Saucy] Triad Module Disabled!");
            return;
        }

        if (subCommand == "play" && args.Length >= 3)
        {
            if (int.TryParse(args[2], out var val))
            {
                TriadAutomater.PlayXTimes = true;
                Svc.Chat.Print("[Saucy] Play X Amount of Times Enabled!");
                TriadAutomater.NumberOfTimes = val;
            }
            else
            {
                Svc.Chat.Print($"[Saucy] Incorrect value specified: {args[2]}");
            }
            return;
        }

        if (subCommand == "cards" && args.Length >= 3)
        {
            if (args[2].ToLower() == "any")
            {
                TriadAutomater.PlayUntilCardDrops = true;
                Svc.Chat.Print("[Saucy] Play Until Any Cards Drop Enabled!");
            }
            if (args[2].ToLower() == "all")
            {
                TriadAutomater.PlayUntilAllCardsDropOnce = true;
                Svc.Chat.Print("[Saucy] Play Until All Cards Drop from NPC at Least X Times Enabled!");
            }

            if (args.Length >= 4 && int.TryParse(args[3], out var val))
            {
                TriadAutomater.NumberOfTimes = val;
            }
        }
    }
}
