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
using Module = ECommons.Module;

namespace Saucy;

public sealed partial class Saucy : IDalamudPlugin
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
    private TriadCollectionHost? _triadCollectionHost;

    public LimbManager LimbManager = null!;
    public Saucy(IDalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.All);
        PunishLibMain.Init(pluginInterface, "Saucy", new AboutPlugin());
        EzConfig.Migrate<Configuration>();
        C = EzConfig.Init<Configuration>();
        TriadRunSession.ModuleEnabled = false;
        TriadCardFarmSession.DeactivateSession(clearProgress: true);
        TriadRunSession.ResetRunModeForPluginLoad();
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
            HelpMessage = "Opens the Saucy menu. Use /saucy stop to halt navigation and automation."
        });

        dataLoader = new();
        dataLoader.StartAsyncWork();

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
    }
    public string Name => "Saucy";
    public static Configuration C { get; private set; } = null!;

    public void Dispose()
    {
        Svc.Commands.RemoveHandler(commandName);
        Svc.PluginInterface.UiBuilder.OpenMainUi -= EzConfigGui.Open;
        Svc.Framework.Update -= RunBot;
        _triadCollectionHost?.Dispose();
        YesAlready.ResumeIfPausedBySaucy();
        SubscriptionManager.DisposeAll();
        TriadMapNavigation.CancelActiveNavigation();
        _triadCollectionHost = null;
        lock (_lockObj) { DisposeAudio(); }
        CuffACurAutomation.FuncHook?.Dispose();
        ModuleManager.Dispose();
        ECommonsMain.Dispose();
        P = null!;
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

        if (args.Length < 2 || !args[0].Equals("tt", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var subCommand = args[1];

        if (subCommand.Equals("go", StringComparison.OrdinalIgnoreCase))
        {
            TriadRunSession.BeginAutomationSession();
            TriadRunSession.ModuleEnabled = true;
            Svc.Chat.Print("[Saucy] Triad Module Enabled!");
            return;
        }

        if (subCommand.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            TriadRunSession.StopAllAutomation();
            return;
        }

        if (subCommand.Equals("play", StringComparison.OrdinalIgnoreCase) && args.Length >= 3)
        {
            if (int.TryParse(args[2], out var val))
            {
                TriadRunSession.ApplyRunMode(TriadRunMode.PlayXTimes, matchCount: val);
                Svc.Chat.Print("[Saucy] Play X Amount of Times Enabled!");
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
                TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAnyCard);
                Svc.Chat.Print("[Saucy] Play Until Any Cards Drop Enabled!");
            }

            if (args[2].ToLower() == "all")
            {
                TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAllCards);
                Svc.Chat.Print("[Saucy] Play Until All Cards Drop from NPC at Least X Times Enabled!");
            }

            if (args.Length >= 4 && int.TryParse(args[3], out var val))
            {
                TriadRunSession.NumberOfTimes = Math.Max(1, val);
                if (TriadRunSession.PlayXTimes)
                {
                    C.TriadMatchCount = TriadRunSession.NumberOfTimes;
                    C.Save();
                }
            }
        }
    }
}
