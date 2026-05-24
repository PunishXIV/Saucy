using Dalamud;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using MgAl2O4.Utils;
using System;
namespace TriadBuddyPlugin;

public class Plugin
{
    public static Localization? CurrentLocManager;
    private readonly GameDataLoader dataLoader;
    private readonly Localization locManager;
    private readonly PluginOverlays overlays;
    private readonly StatTracker statTracker;
    private readonly CommandInfo statusCommand;

    private readonly PluginWindowStatus statusWindow;
    private readonly string[] supportedLangCodes = ["de", "en", "es", "fr", "ja", "ko", "zh"];
    private readonly UIReaderTriadCardList uiReaderCardList;
    private readonly UIReaderTriadDeckEdit uiReaderDeckEdit;

    private readonly UIReaderTriadGame uiReaderGame;
    private readonly UIReaderTriadPrep uiReaderPrep;
    private readonly UIReaderScheduler uiReaderScheduler;

    private readonly WindowSystem windowSystem = new("TriadBuddy");

    public Plugin()
    {
        pluginInterface.Create<Service>();

        Service.plugin = this;
        Service.pluginInterface = pluginInterface;
        //Service.pluginConfig = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // prep utils
        var myAssemblyName = GetType().Assembly.GetName().Name;
        locManager = new($"{myAssemblyName}.assets.loc.", "", true); // res stream format: TriadBuddy.assets.loc.en.json
        locManager.SetupWithLangCode(pluginInterface.UiLanguage);
        CurrentLocManager = locManager;

        dataLoader = new();
        dataLoader.StartAsyncWork();

        SolverUtils.CreateSolvers();
        if (Service.pluginConfig != null && Service.pluginConfig.CanUseProfileReader && SolverUtils.solverPreGameDecks != null)
        {
            SolverUtils.solverPreGameDecks.profileGS = new();
        }

        statTracker = new();

        // prep data scrapers
        uiReaderGame = new();
        uiReaderGame.OnUIStateChanged += state =>
        {
            if (state != null) { SolverUtils.solverGame?.UpdateGame(state); }
        };

        uiReaderPrep = new()
        {
            shouldScanDeckData = (SolverUtils.solverPreGameDecks?.profileGS == null) || SolverUtils.solverPreGameDecks.profileGS.HasErrors
        };
        uiReaderPrep.OnUIStateChanged += state => SolverUtils.solverPreGameDecks?.UpdateDecks(state);

        uiReaderCardList = new();
        uiReaderDeckEdit = new();

        var uiReaderMatchResults = new UIReaderTriadResults();
        uiReaderMatchResults.OnUpdated += state =>
        {
            if (SolverUtils.solverGame != null) { statTracker.OnMatchFinished(SolverUtils.solverGame, state); }
        };

        uiReaderScheduler = new(Svc.GameGui);
        uiReaderScheduler.AddObservedAddon(uiReaderGame);
        uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderMatchRequest);
        uiReaderScheduler.AddObservedAddon(uiReaderPrep.uiReaderDeckSelect);
        uiReaderScheduler.AddObservedAddon(uiReaderCardList);
        uiReaderScheduler.AddObservedAddon(uiReaderDeckEdit);
        uiReaderScheduler.AddObservedAddon(uiReaderMatchResults);

        var memReaderTriadFunc = new UnsafeReaderTriadCards();
        GameCardDB.Get().memReader = memReaderTriadFunc;
        GameNpcDB.Get().memReader = memReaderTriadFunc;

        uiReaderDeckEdit.unsafeDeck = new();

        // prep UI
        overlays = new(uiReaderGame, uiReaderPrep);
        statusWindow = new(uiReaderGame, uiReaderPrep);
        windowSystem.AddWindow(statusWindow);

        var npcStatsWindow = new PluginWindowNpcStats(statTracker);
        var deckOptimizerWindow = new PluginWindowDeckOptimize(uiReaderDeckEdit);
        var deckEvalWindow = new PluginWindowDeckEval(uiReaderPrep, deckOptimizerWindow, npcStatsWindow);
        deckOptimizerWindow.OnConfigRequested += OnOpenConfig;
        windowSystem.AddWindow(deckEvalWindow);
        windowSystem.AddWindow(deckOptimizerWindow);
        windowSystem.AddWindow(npcStatsWindow);

        windowSystem.AddWindow(new PluginWindowCardInfo(uiReaderCardList));
        windowSystem.AddWindow(new PluginWindowCardSearch(uiReaderCardList, npcStatsWindow));
        windowSystem.AddWindow(new PluginWindowDeckSearch(uiReaderDeckEdit));

        // prep plugin hooks
        statusCommand = new(OnCommand)
        {
            HelpMessage = string.Format(Localization.Localize("Cmd_Status", "Show state of {0} plugin"), Name)
        };
        Svc.Commands.AddHandler("/triadbuddy", statusCommand);

        pluginInterface.LanguageChanged += OnLanguageChanged;
        pluginInterface.UiBuilder.Draw += OnDraw;
        pluginInterface.UiBuilder.OpenConfigUi += OnOpenConfig;
        pluginInterface.UiBuilder.OpenMainUi += () => OnCommand("", "");

        Svc.Framework.Update += Framework_Update;
    }
    public string Name => "Triad Buddy";

    [PluginService] internal static IDalamudPluginInterface pluginInterface { get; } = null!;

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

        statusCommand.HelpMessage = string.Format(Localization.Localize("Cmd_Status", "Show state of {0} plugin"), Name);
    }

    public void Dispose()
    {
        Svc.Commands.RemoveHandler("/triadbuddy");
        Svc.Framework.Update -= Framework_Update;
        windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string args)
    {
        statusWindow.showConfigs = false;
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

    private void Framework_Update(IFramework framework)
    {
        try
        {
            if (dataLoader.IsDataReady)
            {
                var deltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
                uiReaderScheduler.Update(deltaSeconds);
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "state update failed");
        }
    }
}
