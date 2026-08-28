using Dalamud.Plugin;
using ECommons;
using ECommons.Configuration;
using ECommons.SimpleGui;
using NAudio.Wave;
using PunishLib;
using Saucy.CuffACur;
using Saucy.Framework;
using Saucy.IPC;
using Saucy.OutOnALimb;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Module = ECommons.Module;

namespace Saucy;

public sealed partial class Saucy(IDalamudPluginInterface pluginInterface) : IAsyncDalamudPlugin
{
    private const string commandName = "/saucy";
    public static Saucy P = null!;

    public static TriadSession TriadRun = new();

    public static UIReaderTriadGame uiReaderGame = null!;
    public static UIReaderTriadPrep uiReaderPrep = null!;
    public static UIReaderTriadResults uiReaderMatchResults = null!;
    public static UIReaderScheduler uiReaderScheduler = null!;
    public static UIReaderGamesResults uiReaderGamesResults = null!;
    public static GameDataLoader dataLoader = null!;
    public static ModuleManager ModuleManager = null!;

    private readonly object _lockObj = new();
    private readonly PluginUI _pluginUi = new();
    private bool _autoOpenedForTriadFlow;
    private Mp3FileReader? _currentReader;
    private WaveOutEvent? _currentWaveOut;
    private bool _ecommonsReady;
    private bool _initialized;
    private TriadCollectionHost? _triadCollectionHost;

    public LimbManager LimbManager = null!;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Initialize();
        await dataLoader.StartAsyncWork(cancellationToken).ConfigureAwait(false);
    }

    private void Initialize()
    {
        ECommonsMain.Init(pluginInterface, this, Module.All);
        _ecommonsReady = true;
        PunishLibMain.Init(pluginInterface, "Saucy", new AboutPlugin());
        EzConfig.Migrate<Configuration>();
        C = EzConfig.Init<Configuration>();
        C.MigrateToBackgroundCpuCores();
        TriadRunSession.ModuleEnabled = false;
        TriadCardFarmSession.DeactivateSession(clearProgress: true);
        TriadRunSession.ResetRunModeForPluginLoad();
        C.SetModuleEnabled(ModuleNames.OutOnALimb, false);
        C.SetModuleEnabled(ModuleNames.CuffACur, false);
        C.Save();
        PrepareTriadSessionForPluginLoad();
        P = this;
        ArcadeMachineSession.WireCompleteShutdown(
            GoldSaucerArcadeMachine.Cuff,
            () => P.DisableArcadeModule(ModuleNames.CuffACur));
        ArcadeMachineSession.WireCompleteShutdown(
            GoldSaucerArcadeMachine.Limb,
            () => P.DisableArcadeModule(ModuleNames.OutOnALimb));

        EzConfigGui.Init(_pluginUi);
        Svc.PluginInterface.UiBuilder.OpenMainUi += EzConfigGui.Open;

        Svc.Commands.AddHandler(commandName, new(OnCommand)
        {
            HelpMessage = "Opens the Saucy menu.\n" +
                          "/saucy stop → stop all navigation and automation\n" +
                          "/saucy tt go → enable Triple Triad automation\n" +
                          "/saucy tt stop → stop Triple Triad automation\n" +
                          "/saucy tt play <n> → fixed match count\n" +
                          "/saucy tt cards any → stop after first card drop\n" +
                          "/saucy tt cards all → farm all NPC cards once\n" +
                          "/saucy d → toggle debug panels"
        });

        dataLoader = new();

        TriadRun.profileGS = new();

        uiReaderGame = new();
#pragma warning disable CS8622
        uiReaderGame.OnUIStateChanged += TriadRun.UpdateGame;
#pragma warning restore CS8622

        uiReaderPrep = new()
        {
            shouldScanDeckData = (TriadRun.profileGS == null) || TriadRun.profileGS.HasErrors
        };
        uiReaderPrep.OnUIStateChanged += TriadRun.UpdateDecks;
        uiReaderPrep.OnMatchRequestChanged += OnTriadPrepUiChanged;
        uiReaderPrep.OnDeckSelectionChanged += OnTriadPrepUiChanged;

        uiReaderMatchResults = new();
        uiReaderMatchResults.OnUpdated += CheckResults;

        uiReaderGamesResults = new();
        uiReaderGamesResults.OnCuffUpdated += CheckCuffResults;
        uiReaderGamesResults.OnLimbUpdated += CheckLimbResults;
        uiReaderGamesResults.OnAirForceUpdated += CheckAirForceResults;

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
        _initialized = true;
    }

    public string Name => "Saucy";
    public static Configuration C { get; private set; } = null!;

    public ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            Svc.Commands.RemoveHandler(commandName);
            Svc.PluginInterface.UiBuilder.OpenMainUi -= EzConfigGui.Open;
            Svc.Framework.Update -= RunBot;
            PrepareTriadSessionForPluginUnload();
            _triadCollectionHost?.Dispose();
            YesAlready.ResumeIfPausedBySaucy();
            SubscriptionManager.DisposeAll();
            TriadMapNavigation.CancelActiveNavigation();
            _triadCollectionHost = null;
            lock (_lockObj) { DisposeAudio(); }
            CuffACurAutomation.FuncHook?.Dispose();
            ModuleManager.Dispose();
        }

        if (_ecommonsReady)
        {
            ECommonsMain.Dispose();
        }

        P = null!;
        return ValueTask.CompletedTask;
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

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Length == 0)
        {
            EzConfigGui.Toggle();
            return;
        }

        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length >= 1 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            TriadRunSession.StopAllAutomation();
            return;
        }

        if (args.Length >= 1 && args[0].Equals("d", StringComparison.OrdinalIgnoreCase))
        {
            _pluginUi.ToggleDebug();
            if (C.ShowDebugUi)
            {
                EzConfigGui.Open();
            }

            return;
        }

        if (args[0].Equals("tt", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
        {
            var subCommand = args[1];

            if (subCommand.Equals("go", StringComparison.OrdinalIgnoreCase))
            {
                TriadRunSession.ModuleEnabled = true;
                TriadRunSession.BeginAutomationSession();
                Svc.Chat.Print("[Saucy] Triple Triad automation enabled.");
                return;
            }

            if (subCommand.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                TriadRunSession.StopAllAutomation();
                return;
            }

            if (subCommand.Equals("play", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 3 && int.TryParse(args[2], out var val) && val > 0)
                {
                    TriadRunSession.ApplyRunMode(TriadRunMode.PlayXTimes, matchCount: val);
                    Svc.Chat.Print($"[Saucy] Fixed match count enabled: {val} matches.");
                }
                else
                {
                    Svc.Chat.Print("[Saucy] Usage: /saucy tt play <number of matches>");
                }

                return;
            }

            if (subCommand.Equals("cards", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
            {
                if (args[2].Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAnyCard);
                    Svc.Chat.Print("[Saucy] Stopping after the first card drop.");
                    return;
                }

                if (args[2].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAllCards);
                    Svc.Chat.Print("[Saucy] Farming all NPC cards once.");
                    return;
                }
            }
        }

        Svc.Chat.Print(
            "[Saucy] Unknown command. Available: /saucy, /saucy stop, /saucy d, " +
            "/saucy tt go | stop | play <n> | cards any | cards all");
    }

    private static void PrepareTriadSessionForPluginLoad()
    {
        TriadDeckOptimizerJobs.CancelActive(userCancelled: true);
        SaucyParallelism.ResetEvalConcurrency();
        TriadRun = new();
        TriadRun.DebugScreenMemory.ResetSolver();
    }

    private static void PrepareTriadSessionForPluginUnload()
    {
        TriadRunSession.StopAllAutomation(announce: false);
        DetachTriadUiReaders();
        TriadRun.InvalidatePendingMoveCalc();
        SaucyParallelism.ResetEvalConcurrency();
    }

    private static void DetachTriadUiReaders()
    {
        uiReaderGame.OnUIStateChanged -= TriadRun.UpdateGame;

        uiReaderPrep.OnUIStateChanged -= TriadRun.UpdateDecks;
        uiReaderPrep.OnMatchRequestChanged = null;
        uiReaderPrep.OnDeckSelectionChanged = null;

        uiReaderMatchResults.OnUpdated = null;

        uiReaderGamesResults.OnCuffUpdated = null;
        uiReaderGamesResults.OnLimbUpdated = null;
        uiReaderGamesResults.OnAirForceUpdated = null;
    }
}
