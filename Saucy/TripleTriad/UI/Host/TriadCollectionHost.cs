using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using System;
namespace Saucy.TripleTriad;

internal sealed class TriadCollectionHost : IDisposable

{
    private readonly TriadCardInfoWindow _cardInfoWindow;

    private readonly TriadCardSearchWindow _cardSearchWindow;

    private readonly TriadNpcStatsWindow _npcStatsWindow;

    private readonly StatTracker _statTracker = new();

    private readonly UIReaderTriadCardList _uiReaderCardList = new();

    private readonly WindowSystem _windowSystem = new("SaucyTriadCollection");

    private bool _isDrawing;

    private bool _sawGameDataReady;

    public TriadCollectionHost(IDalamudPluginInterface pluginInterface)

    {
        _npcStatsWindow = new(_statTracker);

        _cardSearchWindow = new(_uiReaderCardList, _npcStatsWindow);

        _cardInfoWindow = new(_uiReaderCardList, _cardSearchWindow);

        _windowSystem.AddWindow(_cardSearchWindow);

        _windowSystem.AddWindow(_cardInfoWindow);

        _windowSystem.AddWindow(_npcStatsWindow);

        uiReaderScheduler.AddObservedAddon(new GatedCardListReader(_uiReaderCardList));

        pluginInterface.UiBuilder.Draw += OnDraw;
    }

    public void Dispose()

    {
        Svc.PluginInterface.UiBuilder.Draw -= OnDraw;

        _windowSystem.RemoveAllWindows();

        _cardSearchWindow.Dispose();
    }

    private void OnDraw()

    {
        if (_isDrawing || !C.CollectionUiEnabled || !Svc.ClientState.IsLoggedIn || !_uiReaderCardList.IsVisible)

        {
            return;
        }

        if (!dataLoader.IsDataReady)

        {
            return;
        }

        _isDrawing = true;

        try

        {
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

        finally

        {
            _isDrawing = false;
        }
    }

    private sealed class GatedCardListReader(UIReaderTriadCardList inner) : IUIReader

    {
        public string GetAddonName() => inner.GetAddonName();

        public void OnAddonLost()

        {
            if (C.CollectionUiEnabled)

            {
                inner.OnAddonLost();
            }
        }

        public void OnAddonShown(nint addonPtr)

        {
            if (C.CollectionUiEnabled)

            {
                inner.OnAddonShown(addonPtr);
            }
        }

        public void OnAddonUpdate(nint addonPtr)

        {
            if (C.CollectionUiEnabled)

            {
                inner.OnAddonUpdate(addonPtr);
            }
        }
    }
}
