using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.SimpleGui;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using NAudio.Wave;
using PunishLib;
using Saucy.CuffACur;
using Saucy.Framework;
using Saucy.OtherGames;
using Saucy.OutOnALimb;
using Saucy.TripleTriad;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TriadBuddyPlugin;
using static ECommons.GenericHelpers;

namespace Saucy;

public sealed class Saucy : IDalamudPlugin
{
    public string Name => "Saucy";
    public static Configuration C { get; private set; } = null!;
    public static Saucy P = null!;

    private const string commandName = "/saucy";

    public static Solver TTSolver = new();

    public static UIReaderTriadGame uiReaderGame = null!;
    public static UIReaderTriadPrep uiReaderPrep = null!;
    public static UIReaderTriadCardList uiReaderCardList = null!;
    public static UIReaderTriadDeckEdit uiReaderDeckEdit = null!;
    public static UIReaderScheduler uiReaderScheduler = null!;
    public static UIReaderGamesResults uiReaderGamesResults = null!;
    public static StatTracker statTracker = null!;
    public static GameDataLoader dataLoader = null!;

    public static bool GameFinished => TTSolver.cachedScreenState == null;
    internal static bool openTT = false;

    public LimbManager LimbManager = null!;
    public static ModuleManager ModuleManager = null!;
    public Saucy(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, ECommons.Module.All);
        PunishLibMain.Init(pluginInterface, "Saucy", new AboutPlugin() { Sponsor = "https://ko-fi.com/taurenkey" });
        EzConfig.Migrate<Configuration>();
        C = EzConfig.Init<Configuration>();
        P = this;

        EzConfigGui.Init(new PluginUI());

        Svc.Commands.AddHandler(commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Saucy menu."
        });

        dataLoader = new GameDataLoader();
        dataLoader.StartAsyncWork();

        TTSolver.profileGS = new UnsafeReaderProfileGS();

        uiReaderGame = new UIReaderTriadGame();
#pragma warning disable CS8622 // Nullability mismatch in vendored delegate
        uiReaderGame.OnUIStateChanged += TTSolver.UpdateGame;
#pragma warning restore CS8622

        uiReaderPrep = new UIReaderTriadPrep
        {
            shouldScanDeckData = (TTSolver.profileGS == null) || TTSolver.profileGS.HasErrors
        };
        uiReaderPrep.OnUIStateChanged += TTSolver.UpdateDecks;

        uiReaderCardList = new UIReaderTriadCardList();

        var uiReaderMatchResults = new UIReaderTriadResults();
        uiReaderMatchResults.OnUpdated += CheckResults;

        uiReaderGamesResults = new UIReaderGamesResults(Svc.GameGui);
        uiReaderGamesResults.OnCuffUpdated += CheckCuffResults;
        uiReaderGamesResults.OnLimbUpdated += CheckLimbResults;

        uiReaderScheduler = new UIReaderScheduler(Svc.GameGui);
        uiReaderScheduler.AddObservedAddon(uiReaderGame);
        uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
        uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
        uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);
        uiReaderScheduler.AddObservedAddon(uiReaderGamesResults);

        var memReaderTriadFunc = new UnsafeReaderTriadCards();
        GameCardDB.Get().memReader = memReaderTriadFunc;
        GameNpcDB.Get().memReader = memReaderTriadFunc;

        Svc.Framework.Update += RunBot;

        LimbManager = new(C.LimbConfig);
        ModuleManager = new();
        C.EnabledModules.CollectionChanged += OnChange;
    }

    private void OnChange(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var m in ModuleManager.Modules)
        {
            if (C.EnabledModules.Contains(m.InternalName) && !m.IsEnabled)
                m.EnableInternal();

            if (!C.EnabledModules.Contains(m.InternalName) && m.IsEnabled)
                m.DisableInternal();
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

    private async void CheckCuffResults(UIStateCuffResults obj)
    {
        try
        {
            if (CufModule.ModuleEnabled)
            {
                C.UpdateStats(stats =>
                {
                    stats.CuffMGP += GetBonusMGP(obj.numMGP);
                    if (obj.isPunishing) stats.CuffPunishings += 1;
                    if (obj.isBrutal) stats.CuffBrutals += 1;
                    if (obj.isBruising) stats.CuffBruisings += 1;

                    stats.CuffGamesPlayed += 1;
                });

                if (TriadAutomater.PlayXTimes)
                    TriadAutomater.NumberOfTimes--;

                if (TriadAutomater.NumberOfTimes == 0 && TriadAutomater.PlayXTimes)
                {
                    TriadAutomater.NumberOfTimes = 1;
                    CufModule.ModuleEnabled = false;
                    if (C.PlaySound)
                    {
                        PlaySound();
                    }

                    if (TriadAutomater.LogOutAfterCompletion)
                        await PerformLogout();
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

                var npcName = TTSolver.lastGameNpc.Name.GetLocalized();
                if (stats.NPCsPlayed.TryGetValue(npcName, out var plays))
                    stats.NPCsPlayed[npcName] += 1;
                else
                    stats.NPCsPlayed.TryAdd(npcName, 1);

                if (obj.isLose)
                    stats.GamesLostWithSaucy++;
                if (obj.isDraw)
                    stats.GamesDrawnWithSaucy++;
            });

            if (TriadAutomater.PlayXTimes)
                TriadAutomater.NumberOfTimes--;

            if (obj.isWin)
            {
                C.UpdateStats(stats => stats.GamesWonWithSaucy++);
                if (obj.cardItemId > 0)
                {
                    if (TriadAutomater.PlayUntilCardDrops)
                        TriadAutomater.NumberOfTimes--;

                    C.UpdateStats(stats => stats.CardsDroppedWithSaucy++);

                    var cardDB = GameCardDB.Get();

                    foreach ((var _, var cardInfo) in cardDB.mapCards)
                    {
                        if (cardInfo.ItemId == obj.cardItemId)
                        {
                            C.UpdateStats(stats =>
                            {
                                if (stats.CardsWon.ContainsKey((uint)cardInfo.CardId))
                                    stats.CardsWon[(uint)cardInfo.CardId] += 1;
                                else
                                    stats.CardsWon[(uint)cardInfo.CardId] = 1;
                            });

                            if (TriadAutomater.TempCardsWonList.ContainsKey((uint)cardInfo.CardId))
                                TriadAutomater.TempCardsWonList[(uint)cardInfo.CardId] += 1;
                        }
                    }
                }
            }

            Rematch();
            C.Save();
        }
    }

    private int GetBonusMGP(int numMGP)
    {
        double multiplier = 1;
        var localPlayer = Svc.Objects.LocalPlayer;
        if (localPlayer == null) return numMGP;

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
        try
        {
            if (TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon) && TriadAutomater.ModuleEnabled)
            {
                if (((TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops) && TriadAutomater.NumberOfTimes == 0) || (TriadAutomater.TempCardsWonList.Count > 0 && TriadAutomater.TempCardsWonList.All(x => x.Value >= TriadAutomater.NumberOfTimes)))
                {
                    addon->Close(true);
                    return;
                }

                var values = stackalloc AtkValue[2];
                values[0] = new()
                {
                    Type = AtkValueType.Int,
                    Int = 0,
                };
                values[1] = new()
                {
                    Type = AtkValueType.UInt,
                    UInt = 1,
                };
                addon->FireCallback(2, values);
            }
        }
        catch { }
    }

    private async void RunBot(IFramework framework)
    {
        try
        {
            if (dataLoader.IsDataReady)
            {
                var deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
                uiReaderScheduler.Update(deltaSeconds);
            }

            if (C.OpenAutomatically && uiReaderPrep.HasMatchRequestUI && !TriadAutomater.ModuleEnabled)
            {
                EzConfigGui.Open();
                openTT = true;
            }

            if (CufModule.ModuleEnabled)
            {
                CufModule.RunModule();
                return;
            }

            if (TriadAutomater.ModuleEnabled)
            {
                if (((TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops) && TriadAutomater.NumberOfTimes == 0) || (TriadAutomater.TempCardsWonList.Count > 0 && TriadAutomater.TempCardsWonList.All(x => x.Value >= TriadAutomater.NumberOfTimes)))
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
                        TriadAutomater.NumberOfTimes = 1;

                        if (TriadAutomater.LogOutAfterCompletion)
                            await PerformLogout();
                    }

                    return;
                }

                TriadAutomater.RunModule();
                return;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "bot update failed");
        }
    }

    private unsafe void SkipDialogue()
    {
        try
        {
            var talk = Svc.GameGui.GetAddonByName("Talk", 1);
            if (talk == IntPtr.Zero) return;
            var talkAddon = (AtkUnitBase*)talk.Address;
            if (!IsAddonReady(talkAddon)) return;
            new AddonMaster.Talk(talk).Click();
        }
        catch { }
    }

    private static async Task PerformLogout()
    {
        await Svc.Framework.RunOnTick(TriadAutomater.Logout, TimeSpan.FromMilliseconds(2000));
        await Svc.Framework.RunOnTick(TriadAutomater.SelectYesLogout, TimeSpan.FromMilliseconds(3500));
    }

    private readonly object _lockObj = new();
    private WaveOutEvent? _currentWaveOut;
    private Mp3FileReader? _currentReader;

    private void PlaySound()
    {
        lock (_lockObj)
        {
            DisposeAudio();

            var sound = C.SelectedSound;
            var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds", $"{sound}.mp3");
            if (!File.Exists(path)) return;

            _currentReader = new Mp3FileReader(path);
            _currentWaveOut = new WaveOutEvent();
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

    public void Dispose()
    {
        Svc.Commands.RemoveHandler(commandName);
        Svc.Framework.Update -= RunBot;
        lock (_lockObj) { DisposeAudio(); }
        CufModule.FuncHook?.Dispose();
        LimbManager.Dispose();
        ModuleManager.Dispose();
        ECommonsMain.Dispose(); //Don't forget!
        P = null!; //necessary to free the reference for GC
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
            return;

        var subCommand = args[1].ToLower();

        if (subCommand == "go")
        {
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
