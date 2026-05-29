using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.IPC;
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
    private bool _sawGameDataReady;

    public TriadBuddyHost(IDalamudPluginInterface pluginInterface)
    {
        _npcStatsWindow = new(_statTracker);
        _cardSearchWindow = new(_uiReaderCardList, _npcStatsWindow);
        _cardInfoWindow = new(_uiReaderCardList);

        _windowSystem.AddWindow(_cardSearchWindow);
        _windowSystem.AddWindow(_cardInfoWindow);
        _windowSystem.AddWindow(_npcStatsWindow);

        IpcSubscriptions.Refresh();
        pluginInterface.UiBuilder.Draw += OnDraw;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= OnDraw;
        IpcSubscriptions.Dispose();
        _windowSystem.RemoveAllWindows();
        _cardSearchWindow.Dispose();
    }

    private void OnDraw()
    {
        if (!Saucy.C.TriadBuddyCollectionUiEnabled)
            return;

        IpcSubscriptions.Refresh();

        if (!Svc.ClientState.IsLoggedIn)
            return;

        RefreshCardListReader();

        if (!_uiReaderCardList.IsVisible)
            return;

        if (!Saucy.dataLoader.IsDataReady)
            return;

        if (!_sawGameDataReady)
        {
            _sawGameDataReady = true;
            _cardSearchWindow.OnGameDataReady();
        }

        _cardSearchWindow.SyncVisibility();
        _cardInfoWindow.SyncVisibility();

        if (Saucy.C.SaucyThemeEnabled)
            SaucyTheme.Push();

        _windowSystem.Draw();

        if (Saucy.C.SaucyThemeEnabled)
            SaucyTheme.Pop();
    }

    private unsafe void RefreshCardListReader()
    {
        var addonPtr = ResolveCardListAddonPtr();
        if (addonPtr == nint.Zero)
        {
            if (_uiReaderCardList.status != UIReaderTriadCardList.Status.AddonNotFound)
                _uiReaderCardList.OnAddonLost();
            return;
        }

        if (_uiReaderCardList.status is UIReaderTriadCardList.Status.AddonNotFound or UIReaderTriadCardList.Status.AddonNotVisible)
            _uiReaderCardList.OnAddonShown(addonPtr);

        _uiReaderCardList.OnAddonUpdate(addonPtr);
    }

    /// <summary>Upstream TriadBuddy uses <c>GetAddonByName("GSInfoCardList", 1)</c> — index 0 is often not the visible instance.</summary>
    private static unsafe nint ResolveCardListAddonPtr()
    {
        for (var i = 0; i < 8; i++)
        {
            var handle = Svc.GameGui.GetAddonByName("GSInfoCardList", i);
            if (handle.Address == nint.Zero)
                continue;

            var unit = (AtkUnitBase*)handle.Address;
            if (unit->IsVisible && unit->RootNode != null && unit->RootNode->IsVisible())
                return handle.Address;
        }

        return nint.Zero;
    }
}


