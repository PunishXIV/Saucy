using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using System;
using TriadBuddyPlugin;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal sealed class TriadBuddyHost : IDisposable
{
    private readonly WindowSystem _windowSystem = new("SaucyTriadBuddy");
    private readonly UIReaderTriadCardList _uiReaderCardList = new();
    private readonly StatTracker _statTracker = new();
    private readonly PluginWindowCardSearch _cardSearchWindow;
    private readonly PluginWindowCardInfo _cardInfoWindow;
    private readonly PluginWindowNpcStats _npcStatsWindow;
    private readonly Localization _locManager;

    public TriadBuddyHost(IDalamudPluginInterface pluginInterface)
    {
        var assemblyName = typeof(Saucy).Assembly.GetName().Name!;
        _locManager = new($"{assemblyName}.TripleTriad.TriadBuddy.assets.loc.", "", true);
        _locManager.SetupWithLangCode(pluginInterface.UiLanguage);
        TriadCollectionUi.Loc = _locManager;

        pluginInterface.LanguageChanged += OnLanguageChanged;

        _npcStatsWindow = new(_statTracker);
        _cardSearchWindow = new(_uiReaderCardList, _npcStatsWindow);
        _cardInfoWindow = new(_uiReaderCardList);

        _windowSystem.AddWindow(_cardSearchWindow);
        _windowSystem.AddWindow(_cardInfoWindow);
        _windowSystem.AddWindow(_npcStatsWindow);

        QuestionableInterop.Init(pluginInterface);
        pluginInterface.UiBuilder.Draw += OnDraw;

        Saucy.uiReaderScheduler.AddObservedAddon(_uiReaderCardList);
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= OnDraw;
        Svc.PluginInterface.LanguageChanged -= OnLanguageChanged;
        QuestionableInterop.Dispose();
        _windowSystem.RemoveAllWindows();
        _cardSearchWindow.Dispose();
        TriadCollectionUi.Loc = null;
    }

    private void OnLanguageChanged(string langCode)
    {
        var supported = new[] { "de", "en", "es", "fr", "ja", "ko", "zh" };
        if (Array.Find(supported, x => x == langCode) != null)
            _locManager.SetupWithLangCode(langCode);
        else
            _locManager.SetupWithFallbacks();
    }

    private void OnDraw()
    {
        if (!Saucy.C.TriadBuddyCollectionUiEnabled)
            return;

        RefreshCardListReader();

        _cardSearchWindow.SyncVisibility();
        _cardInfoWindow.SyncVisibility();

        _windowSystem.Draw();
    }

    private unsafe void RefreshCardListReader()
    {
        if (!TryGetAddonByName<AtkUnitBase>("GSInfoCardList", out var addon) || !IsAddonReady(addon))
        {
            if (_uiReaderCardList.status != UIReaderTriadCardList.Status.AddonNotFound)
                _uiReaderCardList.OnAddonLost();
            return;
        }

        var addonPtr = addon.Address;
        if (_uiReaderCardList.status is UIReaderTriadCardList.Status.AddonNotFound or UIReaderTriadCardList.Status.AddonNotVisible)
            _uiReaderCardList.OnAddonShown(addonPtr);

        _uiReaderCardList.OnAddonUpdate(addonPtr);
    }
}
