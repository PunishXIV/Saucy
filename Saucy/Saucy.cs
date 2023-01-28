using ClickLib;
using ClickLib.Clicks;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using MgAl2O4.Utils;
using Saucy.CuffACur;
using Saucy.TripleTriad;
using System;
using System.IO;
using System.Linq;
using TriadBuddyPlugin;
using static ECommons.GenericHelpers;

namespace Saucy
{
    public sealed class Saucy : IDalamudPlugin
    {
        public string Name => "Saucy";

        private const string commandName = "/saucy";
        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        public static Configuration Configuration { get; set; }
        private PluginUI PluginUi { get; init; }

        public static Solver TTSolver = new();

        public readonly UIReaderTriadGame uiReaderGame;
        public readonly UIReaderTriadPrep uiReaderPrep;
        public readonly UIReaderTriadCardList uiReaderCardList;
        public readonly UIReaderTriadDeckEdit uiReaderDeckEdit;
        public readonly UIReaderScheduler uiReaderScheduler;
        public readonly StatTracker statTracker;
        public readonly GameDataLoader dataLoader;


        public Saucy(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            GameGui gameGui,
            DataManager dataManager)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;

            Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(this.PluginInterface);

            // you might normally want to embed resources and load them from the manifest stream
            var imagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "punish.png");
            var demoImage = this.PluginInterface.UiBuilder.LoadImage(imagePath);
            this.PluginUi = new PluginUI(Configuration, demoImage);

            this.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "A useful message to display in /xlhelp"
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            ECommons.ECommons.Init(pluginInterface, this);

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
            uiReaderScheduler = new UIReaderScheduler(gameGui);
            uiReaderScheduler.AddObservedAddon(uiReaderGame);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
            uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);

            Svc.Framework.Update += RunBot;
            Click.Initialize();
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
            this.CommandManager.RemoveHandler(commandName);
            ECommons.ECommons.Dispose();

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
