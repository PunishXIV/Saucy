using ClickLib;
using ClickLib.Clicks;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using PunishLib;
using PunishLib.Sponsor;
using Saucy.CuffACur;
using Saucy.TripleTriad;
using System;
using TriadBuddyPlugin;
using static ECommons.GenericHelpers;

namespace Saucy
{
    public sealed class Saucy : IDalamudPlugin
    {
        public string Name => "Saucy";
        internal static Saucy P;

        private const string commandName = "/saucy";
        private PluginUI PluginUi { get; init; }

        public static Solver TTSolver = new();

        public readonly UIReaderTriadGame uiReaderGame;
        public readonly UIReaderTriadPrep uiReaderPrep;
        public readonly UIReaderTriadCardList uiReaderCardList;
        public readonly UIReaderTriadDeckEdit uiReaderDeckEdit;
        public readonly UIReaderScheduler uiReaderScheduler;
        public readonly StatTracker statTracker;
        public readonly GameDataLoader dataLoader;

        public static int GamesPlayed { get; set; }
        public static int GamesWon { get; set; }
        public static int GamesLost { get; set; }
        public static int GamesDrawn { get; set; }
        public static int CardsDropped { get; set; }
        public static bool GameFinished => TTSolver.cachedScreenState == null;

        public Saucy([RequiredVersion("1.0")] DalamudPluginInterface pluginInterface, [RequiredVersion("1.0")] CommandManager commandManager, GameGui gameGui, DataManager dataManager)
        {
            pluginInterface.Create<Service>();
            Service.Plugin = this;

            Service.Configuration = Service.Interface.GetPluginConfig() as Configuration ?? new Configuration();
            Service.Configuration.Initialize(Service.Interface);

            Dalamud.Logging.PluginLog.Debug($"{Service.Configuration.UseRecommendedDeck}");

            ECommonsMain.Init(Service.Interface, this);
            PunishLibMain.Init(Service.Interface, this);
            SponsorManager.SetSponsorInfo("https://ko-fi.com/taurenkey");
            P = this;

            this.PluginUi = new PluginUI(Service.Configuration);

            Service.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the Saucy menu."
            });

            Service.Interface.UiBuilder.Draw += DrawUI;
            Service.Interface.UiBuilder.OpenConfigUi += DrawConfigUI;

            dataLoader = new GameDataLoader();
            dataLoader.StartAsyncWork(dataManager);

            TTSolver.profileGS = new UnsafeReaderProfileGS(gameGui);

            uiReaderGame = new UIReaderTriadGame(gameGui);
            uiReaderGame.OnUIStateChanged += TTSolver.UpdateGame;

            uiReaderPrep = new UIReaderTriadPrep(gameGui);
            uiReaderPrep.shouldScanDeckData = (TTSolver.profileGS == null) || TTSolver.profileGS.HasErrors;
            uiReaderPrep.OnUIStateChanged += TTSolver.UpdateDecks;

            uiReaderCardList = new UIReaderTriadCardList(gameGui);

            var uiReaderMatchResults = new UIReaderTriadResults(gameGui);
            uiReaderMatchResults.OnUpdated += CheckResults;

            uiReaderScheduler = new UIReaderScheduler(gameGui);
            uiReaderScheduler.AddObservedAddon(uiReaderGame);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
            uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);

            Svc.Framework.Update += RunBot;
            Click.Initialize();
        }

        private unsafe void CheckResults(UIStateTriadResults obj)
        {
            if (TriadAutomater.ModuleEnabled)
            {
                Service.Configuration.Stats.GamesPlayedWithSaucy++;
                Service.Configuration.Stats.MGPWon += obj.numMGP;

                if (TriadAutomater.PlayXTimes)
                    TriadAutomater.NumberOfTimes--;

                if (Service.Configuration.Stats.NPCsPlayed.TryGetValue(TTSolver.lastGameNpc.Name.GetLocalized(), out int plays))
                {
                    Service.Configuration.Stats.NPCsPlayed[TTSolver.lastGameNpc.Name.GetLocalized()] += 1;
                    Service.Configuration.Save();
                }
                else
                {
                    Service.Configuration.Stats.NPCsPlayed.TryAdd(TTSolver.lastGameNpc.Name.GetLocalized(), 1);
                    Service.Configuration.Save();
                }

                if (obj.isWin)
                {
                    Service.Configuration.Stats.GamesWonWithSaucy++;
                    if (obj.cardItemId > 0)
                    {
                        if (TriadAutomater.PlayUntilCardDrops)
                            TriadAutomater.NumberOfTimes--;

                        Service.Configuration.Stats.CardsDroppedWithSaucy++;
                    }
                }
                if (obj.isLose)
                {
                    Service.Configuration.Stats.GamesLostWithSaucy++;
                }
                if (obj.isDraw)
                {
                    Service.Configuration.Stats.GamesDrawnWithSaucy++;
                }
            }
            Service.Configuration.Save();

            {
                if (TryGetAddonByName<AtkUnitBase>("TripleTriadResult", out var addon) && TriadAutomater.ModuleEnabled)
                {
                    if ((TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops) && TriadAutomater.NumberOfTimes == 0)
                    {
                        addon->Hide(true);
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
        }

        private unsafe void RunBot(Framework framework)
        {
            try
            {
                if (dataLoader.IsDataReady)
                {
                    float deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
                    uiReaderScheduler.Update(deltaSeconds);
                }
            }
            catch (Exception ex)
            {
                Dalamud.Logging.PluginLog.Error(ex, "state update failed");
            }

            if (CufModule.ModuleEnabled)
            {
                CufModule.RunModule();
                return;
            }
            else
            {
                if (CufModule.DisableInput)
                {
                    CufModule.Inputs.ForceRelease(Dalamud.Game.ClientState.Keys.VirtualKey.NUMPAD0);
                    CufModule.DisableInput = false;
                }
            }

            if (TriadAutomater.ModuleEnabled)
            {
                if ((TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops) && TriadAutomater.NumberOfTimes == 0)
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
                        var talk = Svc.GameGui.GetAddonByName("Talk", 1);
                        if (talk == IntPtr.Zero) return;
                        var talkAddon = (AtkUnitBase*)talk;
                        if (!IsAddonReady(talkAddon)) return;
                        ClickTalk.Using(talk).Click();
                    }
                    else
                    {
                        TriadAutomater.PlayXTimes = false;
                        TriadAutomater.PlayUntilCardDrops = false;
                        TriadAutomater.ModuleEnabled = false;

                        if (TriadAutomater.LogOutAfterCompletion)
                        {
                            Svc.Framework.RunOnTick(() => TriadAutomater.Logout(), TimeSpan.FromMilliseconds(2000));
                            Svc.Framework.RunOnTick(() => TriadAutomater.SelectYesLogout(), TimeSpan.FromMilliseconds(3500));
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

        public void Dispose()
        {
            this.PluginUi.Dispose();
            Service.CommandManager.RemoveHandler(commandName);
            Svc.Framework.Update -= RunBot;

        }

        private void OnCommand(string command, string args)
        {
            this.PluginUi.Visible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.Visible = true;
        }
    }


}
