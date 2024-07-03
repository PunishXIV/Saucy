using Dalamud;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using MgAl2O4.Utils;
using System;
using ECommons.Logging;

namespace TriadBuddyPlugin
{
    public class Plugin
    {
        public string Name => "Triad Buddy";

        public readonly IDalamudPluginInterface pluginInterface;
        public readonly ICommandManager commandManager;
        public readonly IFramework framework;
        public readonly IDataManager dataManager;
        public readonly WindowSystem windowSystem = new("TriadBuddy");

        public readonly PluginWindowStatus statusWindow;
        public readonly CommandInfo statusCommand;

        public readonly UIReaderTriadGame uiReaderGame;
        public readonly UIReaderTriadPrep uiReaderPrep;
        public readonly UIReaderTriadCardList uiReaderCardList;
        public readonly UIReaderTriadDeckEdit uiReaderDeckEdit;
        public readonly Solver solver = new();
        public readonly StatTracker statTracker;
        public readonly GameDataLoader dataLoader;
        public readonly UIReaderScheduler uiReaderScheduler;
        public readonly PluginOverlays overlays;
        public readonly Localization locManager;

        public static Localization CurrentLocManager;
        public string[] supportedLangCodes = { "de", "en", "es", "fr", "ja", "zh" };

        private Configuration configuration { get; init; }

        public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, ICommandManager commandManager, IGameGui gameGui, IDataManager dataManager, SigScanner sigScanner)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.dataManager = dataManager;
            this.framework = framework;

            //configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            //configuration.Initialize(pluginInterface);

            // prep utils
            var myAssemblyName = GetType().Assembly.GetName().Name;
            locManager = new Localization($"{myAssemblyName}.assets.loc.", "", true);            // res stream format: TriadBuddy.assets.loc.en.json
            locManager.SetupWithLangCode(pluginInterface.UiLanguage);
            CurrentLocManager = locManager;

            dataLoader = new GameDataLoader();
            dataLoader.StartAsyncWork(dataManager);

            //solver = new Solver();
            solver.profileGS = configuration.CanUseProfileReader ? new UnsafeReaderProfileGS(gameGui) : null;

            statTracker = new StatTracker(configuration);

            // prep data scrapers
            uiReaderGame = new UIReaderTriadGame(gameGui);
            uiReaderGame.OnUIStateChanged += (state) => solver.UpdateGame(state);

            uiReaderPrep = new UIReaderTriadPrep(gameGui);
            uiReaderPrep.shouldScanDeckData = (solver.profileGS == null) || solver.profileGS.HasErrors;
            uiReaderPrep.OnUIStateChanged += (state) => solver.UpdateDecks(state);

            uiReaderCardList = new UIReaderTriadCardList(gameGui);
            uiReaderDeckEdit = new UIReaderTriadDeckEdit(gameGui);

            var uiReaderMatchResults = new UIReaderTriadResults(gameGui);
            uiReaderMatchResults.OnUpdated += (state) => statTracker.OnMatchFinished(solver, state);

            uiReaderScheduler = new UIReaderScheduler(gameGui);
            uiReaderScheduler.AddObservedAddon(uiReaderGame);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
            uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
            uiReaderScheduler.AddObservedAddon(uiReaderCardList);
            uiReaderScheduler.AddObservedAddon(uiReaderDeckEdit);
            uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);

            var memReaderTriadFunc = new UnsafeReaderTriadCards(sigScanner);
            GameCardDB.Get().memReader = memReaderTriadFunc;
            GameNpcDB.Get().memReader = memReaderTriadFunc;

            // prep UI
            overlays = new PluginOverlays(solver, uiReaderGame, uiReaderPrep, configuration);
            statusWindow = new PluginWindowStatus(dataManager, solver, uiReaderGame, uiReaderPrep, configuration);
            windowSystem.AddWindow(statusWindow);

            var npcStatsWindow = new PluginWindowNpcStats(statTracker);
            var deckOptimizerWindow = new PluginWindowDeckOptimize(dataManager, solver, uiReaderDeckEdit, configuration);
            var deckEvalWindow = new PluginWindowDeckEval(solver, uiReaderPrep, deckOptimizerWindow, npcStatsWindow);
            deckOptimizerWindow.OnConfigRequested += () => OnOpenConfig();
            windowSystem.AddWindow(deckEvalWindow);
            windowSystem.AddWindow(deckOptimizerWindow);
            windowSystem.AddWindow(npcStatsWindow);

            windowSystem.AddWindow(new PluginWindowCardInfo(uiReaderCardList, gameGui));
            windowSystem.AddWindow(new PluginWindowCardSearch(uiReaderCardList, gameGui, configuration, npcStatsWindow));
            windowSystem.AddWindow(new PluginWindowDeckSearch(uiReaderDeckEdit, gameGui));

            // prep plugin hooks
            statusCommand = new(OnCommand);
            commandManager.AddHandler("/triadbuddy", statusCommand);

            pluginInterface.LanguageChanged += OnLanguageChanged;
            pluginInterface.UiBuilder.Draw += OnDraw;
            pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;

            framework.Update += Framework_OnUpdateEvent;

            // keep at the end to update everything created here
            locManager.LocalizationChanged += (_) => CacheLocalization();
            CacheLocalization();
        }

        private void OnLanguageChanged(string langCode)
        {
            // check if resource is available, will cause exception if trying to load empty json
            if (Array.Find(supportedLangCodes, x => x == langCode) != null)
            {
                locManager.SetupWithLangCode(langCode);
            }
            else
            {
                locManager.SetupWithFallbacks();
            }
        }

        private void CacheLocalization()
        {
            statusCommand.HelpMessage = string.Format(Localization.Localize("Cmd_Status", "Show state of {0} plugin"), Name);
        }

        public void Dispose()
        {
            commandManager.RemoveHandler("/triadbuddy");
            windowSystem.RemoveAllWindows();
            framework.Update -= Framework_OnUpdateEvent;
        }

        private void OnCommand(string command, string args)
        {
            statusWindow.IsOpen = true;
        }

        private void OnDraw()
        {
            windowSystem.Draw();
            overlays.OnDraw();
        }

        private void OnOpenConfig()
        {
            statusWindow.showConfigs = true;
            statusWindow.IsOpen = true;
        }

        private void Framework_OnUpdateEvent(IFramework framework)
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
                PluginLog.Error("state update failed: " + ex);
            }
        }
    }
}
