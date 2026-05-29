using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.TripleTriad.UI;
using System;
using static ECommons.GenericHelpers;

namespace Saucy.TripleTriad;

internal sealed class TriadCollectionHost : IDisposable
{
    private const float SlowCheckInterval = 0.5f;

    private readonly WindowSystem _windowSystem = new("SaucyTriadCollection");
    private readonly UIReaderTriadCardList _uiReaderCardList = new();
    private readonly StatTracker _statTracker = new();
    private readonly PluginWindowCardSearch _cardSearchWindow;
    private readonly PluginWindowCardInfo _cardInfoWindow;
    private readonly PluginWindowNpcStats _npcStatsWindow;
    private bool _sawGameDataReady;
    private float _slowCheckRemaining;

    public TriadCollectionHost(IDalamudPluginInterface pluginInterface)
    {
        _npcStatsWindow = new(_statTracker);
        _cardSearchWindow = new(_uiReaderCardList, _npcStatsWindow);
        _cardInfoWindow = new(_uiReaderCardList);

        _windowSystem.AddWindow(_cardSearchWindow);
        _windowSystem.AddWindow(_cardInfoWindow);
        _windowSystem.AddWindow(_npcStatsWindow);

        pluginInterface.UiBuilder.Draw += OnDraw;
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.PluginInterface.UiBuilder.Draw -= OnDraw;
        _windowSystem.RemoveAllWindows();
        _cardSearchWindow.Dispose();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!C.CollectionUiEnabled || !Svc.ClientState.IsLoggedIn)
        {
            return;
        }

        if (_uiReaderCardList.IsVisible)
        {
            RefreshCardListReader();
            return;
        }

        _slowCheckRemaining -= (float)framework.UpdateDelta.TotalSeconds;
        if (_slowCheckRemaining > 0)
        {
            return;
        }

        _slowCheckRemaining = SlowCheckInterval;
        RefreshCardListReader();
    }

    private void OnDraw()
    {
        if (!C.CollectionUiEnabled || !Svc.ClientState.IsLoggedIn || !_uiReaderCardList.IsVisible)
        {
            return;
        }

        if (!dataLoader.IsDataReady)
        {
            return;
        }

        if (!_sawGameDataReady)
        {
            _sawGameDataReady = true;
            _cardSearchWindow.OnGameDataReady();
        }

        _cardSearchWindow.SyncVisibility();
        _cardInfoWindow.SyncVisibility();

        if (C.SaucyThemeEnabled)
        {
            SaucyTheme.Push();
        }

        _windowSystem.Draw();

        if (C.SaucyThemeEnabled)
        {
            SaucyTheme.Pop();
        }
    }

    private void RefreshCardListReader()
    {
        var addonPtr = ResolveCardListAddonPtr();
        if (addonPtr == nint.Zero)
        {
            if (_uiReaderCardList.status != UIReaderTriadCardList.Status.AddonNotFound)
            {
                _uiReaderCardList.OnAddonLost();
            }

            return;
        }

        if (_uiReaderCardList.status is UIReaderTriadCardList.Status.AddonNotFound or UIReaderTriadCardList.Status.AddonNotVisible)
        {
            _uiReaderCardList.OnAddonShown(addonPtr);
        }

        _uiReaderCardList.OnAddonUpdate(addonPtr);
    }

    private static unsafe nint ResolveCardListAddonPtr()
    {
        for (var i = 0; i < 8; i++)
        {
            var handle = Svc.GameGui.GetAddonByName("GSInfoCardList", i);
            if (handle.Address == nint.Zero)
            {
                continue;
            }

            var unit = (AtkUnitBase*)handle.Address;
            if (unit->IsVisible && unit->RootNode != null && unit->RootNode->IsVisible())
            {
                return handle.Address;
            }
        }

        return nint.Zero;
    }
}
