using ClickLib;
using ClickLib.Clicks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using NAudio.Wave;
using PunishLib;
using Saucy.CuffACur;
using Saucy.OtherGames;
using Saucy.OutOnALimb;
using Saucy.TripleTriad;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECommons.Logging;
using TriadBuddyPlugin;
using static ECommons.GenericHelpers;

namespace Saucy
{
    public sealed class Saucy : IDalamudPlugin
    {
        public string Name => "Saucy";
        public static Saucy P;

        private const string commandName = "/saucy";
        public static PluginUI PluginUi { get; set; }

        public static Solver TTSolver = new();

        public static UIReaderTriadGame uiReaderGame;
        public static UIReaderTriadPrep uiReaderPrep;
        public static UIReaderTriadCardList uiReaderCardList;
        public static UIReaderTriadDeckEdit uiReaderDeckEdit;
        public static UIReaderScheduler uiReaderScheduler;
        public static UIReaderGamesResults uiReaderGamesResults;
        public static StatTracker statTracker;
        public static GameDataLoader dataLoader;
        public static List<Task> AirForceOneTask = new List<Task>();
        public static CancellationTokenSource AirForceOneToken = new();
        public static Configuration Config;

        public static bool GameFinished => TTSolver.cachedScreenState == null;
        internal static bool openTT = false;

        public LimbManager LimbManager;
        public MiniCactpotManager MiniCactpotManager;

        public Saucy(IDalamudPluginInterface pluginInterface)
        {
            ECommonsMain.Init(pluginInterface, this, Module.All);
            PunishLibMain.Init(pluginInterface, "Saucy", new AboutPlugin() { Sponsor = "https://ko-fi.com/taurenkey" });

            Config = Svc.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Config.Initialize(Svc.PluginInterface);
            P = this;

            PluginUi = new PluginUI(Config);

            Svc.Commands.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Saucy menu."
            });

            Svc.PluginInterface.UiBuilder.Draw += DrawUI;
            Svc.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            dataLoader = new GameDataLoader();
            dataLoader.StartAsyncWork(Svc.Data);

            TTSolver.profileGS = new UnsafeReaderProfileGS(Svc.GameGui);

            uiReaderGame = new UIReaderTriadGame(Svc.GameGui);
            uiReaderGame.OnUIStateChanged += TTSolver.UpdateGame;

            uiReaderPrep = new UIReaderTriadPrep(Svc.GameGui);
            uiReaderPrep.shouldScanDeckData = (TTSolver.profileGS == null) || TTSolver.profileGS.HasErrors;
            uiReaderPrep.OnUIStateChanged += TTSolver.UpdateDecks;

            uiReaderCardList = new UIReaderTriadCardList(Svc.GameGui);

            var uiReaderMatchResults = new UIReaderTriadResults(Svc.GameGui);
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

            var memReaderTriadFunc = new UnsafeReaderTriadCards(Svc.SigScanner);
            GameCardDB.Get().memReader = memReaderTriadFunc;
            GameNpcDB.Get().memReader = memReaderTriadFunc;
            
            SliceIsRightModule.Initialize();

            Svc.Framework.Update += RunBot;
            Click.Initialize();

            LimbManager = new(Config.LimbConfig);
            MiniCactpotManager = new();
				}

        private void CheckLimbResults(UIStateLimbResults results)
        {
            if (LimbManager.C.EnableLimb)
            {
                Config.UpdateStats(stats =>
                {
                    stats.LimbMGP += GetBonusMGP(results.numMGP);
                    stats.LimbGamesPlayed++;
                });

                uiReaderGamesResults.SetIsResultsUI(false);
                Saucy.Config.Save();
            }
        }

        private async void CheckCuffResults(UIStateCuffResults obj)
        {
            if (CufModule.ModuleEnabled)
            {
                Saucy.Config.UpdateStats(stats =>
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
                    if (Saucy.Config.PlaySound)
                    {
                        PlaySound();
                    }

                    if (TriadAutomater.LogOutAfterCompletion)
                    {
                        await Svc.Framework.RunOnTick(() => TriadAutomater.Logout(), TimeSpan.FromMilliseconds(2000));
                        await Svc.Framework.RunOnTick(() => TriadAutomater.SelectYesLogout(), TimeSpan.FromMilliseconds(3500));
                    }
                }

                uiReaderGamesResults.SetIsResultsUI(false);
                Saucy.Config.Save();
            }
        }

        private void CheckResults(UIStateTriadResults obj)
        {
            if (TriadAutomater.ModuleEnabled)
            {
                Saucy.Config.UpdateStats(stats =>
                {
                    stats.GamesPlayedWithSaucy++;
                    stats.MGPWon += this.GetBonusMGP(obj.numMGP);

                    if (stats.NPCsPlayed.TryGetValue(TTSolver.lastGameNpc.Name.GetLocalized(), out int plays))
                        stats.NPCsPlayed[TTSolver.lastGameNpc.Name.GetLocalized()] += 1;
                    else
                        stats.NPCsPlayed.TryAdd(TTSolver.lastGameNpc.Name.GetLocalized(), 1);

                    if (obj.isLose)
                        stats.GamesLostWithSaucy++;
                    if (obj.isDraw)
                        stats.GamesDrawnWithSaucy++;
                });

                if (TriadAutomater.PlayXTimes)
                    TriadAutomater.NumberOfTimes--;

                if (obj.isWin)
                {
                    Saucy.Config.UpdateStats(stats => stats.GamesWonWithSaucy++);
                    if (obj.cardItemId > 0)
                    {
                        if (TriadAutomater.PlayUntilCardDrops)
                            TriadAutomater.NumberOfTimes--;

                        Saucy.Config.UpdateStats(stats => stats.CardsDroppedWithSaucy++);

                        var cardDB = GameCardDB.Get();

                        foreach ((int _, GameCardInfo cardInfo) in cardDB.mapCards)
                        {
                            if (cardInfo.ItemId == obj.cardItemId)
                            {
                                Saucy.Config.UpdateStats(stats =>
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

                this.Rematch();
            }
            Saucy.Config.Save();


        }

        private int GetBonusMGP(int numMGP)
        {
            double multiplier = 1;
            //Jackpot
            if (Svc.ClientState.LocalPlayer.StatusList.Any(x => x.StatusId == 902))
            {
                multiplier += (double)Svc.ClientState.LocalPlayer.StatusList.First(x => x.StatusId == 902).Param / 100;
            }

            //MGP Card
            if (Svc.ClientState.LocalPlayer.StatusList.Any(x => x.StatusId == 1079))
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
                        Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int,
                        Int = 0,
                    };
                    values[1] = new()
                    {
                        Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.UInt,
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
                    float deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
                    uiReaderScheduler.Update(deltaSeconds);
                }
            }
            catch (Exception)
            {
                DuoLog.Error("state update failed");
            }

          
            if (Saucy.Config.OpenAutomatically && uiReaderPrep.HasMatchRequestUI && !TriadAutomater.ModuleEnabled)
            {
                PluginUi.Visible = true;
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
                        if (Saucy.Config.PlaySound)
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
                        {
                            await Svc.Framework.RunOnTick(() => TriadAutomater.Logout(), TimeSpan.FromMilliseconds(2000));
                            await Svc.Framework.RunOnTick(() => TriadAutomater.SelectYesLogout(), TimeSpan.FromMilliseconds(3500));
                        }
                    }

                    return;
                }

                TriadAutomater.RunModule();
                return;
            }
            else
            {

            }

        }

        private unsafe void SkipDialogue()
        {
            try
            {
                var talk = Svc.GameGui.GetAddonByName("Talk", 1);
                if (talk == IntPtr.Zero) return;
                var talkAddon = (AtkUnitBase*)talk;
                if (!IsAddonReady(talkAddon)) return;
                ClickTalk.Using(talk).Click();
            }
            catch { }
        }

        private readonly object _lockObj = new();

        private void PlaySound()
        {
            lock (_lockObj)
            {
                string sound = Saucy.Config.SelectedSound;
                string path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds", $"{sound}.mp3");
                if (!File.Exists(path)) return;
                var reader = new Mp3FileReader(path);
                var waveOut = new WaveOutEvent();

                waveOut.Init(reader);
                waveOut.Play();
            }
        }

        public void Dispose()
        {
            PluginUi.Dispose();
            Svc.Commands.RemoveHandler(commandName);
            Svc.Framework.Update -= RunBot;
            SliceIsRightModule.ModuleEnabled = false;
            LimbManager.Dispose();
            MiniCactpotManager.Dispose();
						ECommonsMain.Dispose(); //Don't forget!
            P = null; //necessary to free the reference for GC
        }

        private void OnCommand(string command, string arguments)
        {
            if (arguments.Length == 0)
                PluginUi.Visible = !PluginUi.Visible;

            var args = arguments.Split();
            if (args.Length > 0)
            {
                
                if (args[0].ToLower() == "sr")
                {
                    SliceIsRightModule.ModuleEnabled = false;
                }

                if (args[0].ToLower() == "tt")
                {
                    if (args[1].ToLower() == "go")
                    {
                        TriadAutomater.ModuleEnabled = true;
                        Svc.Chat.Print("[Saucy] Triad Module Enabled!");
                        return;
                    }

                    if (args[1].ToLower() == "play")
                    {
                        TriadAutomater.PlayXTimes = true;
                        Svc.Chat.Print("[Saucy] Play X Amount of Times Enabled!");

                        if (int.TryParse(args[2], out int val))
                        {
                            TriadAutomater.NumberOfTimes = val;
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }

                    if (args[1].ToLower() == "cards")
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

                        if (int.TryParse(args[3], out int val))
                        {
                            TriadAutomater.NumberOfTimes = val;
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
        }

        private void DrawUI()
        {
            PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            PluginUi.Visible = true;
        }
    }


}
