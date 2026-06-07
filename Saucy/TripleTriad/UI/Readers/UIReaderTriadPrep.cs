using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadPrep
{
    public UIStateTriadPrep cachedState = new();
    public Action<bool>? OnDeckSelectionChanged;
    public Action<bool>? OnMatchRequestChanged;

    public Action<UIStateTriadPrep>? OnUIStateChanged;
    public bool shouldScanDeckData = false;
    public UIReaderTriadPrepDeckSelect uiReaderDeckSelect = new();

    public UIReaderTriadPrepMatchRequest uiReaderMatchRequest = new();

    public UIReaderTriadPrep()
    {
        uiReaderMatchRequest.parentReader = this;
        uiReaderDeckSelect.parentReader = this;
    }

    public bool HasMatchRequestUI { get; private set; }
    public bool HasDeckSelectionUI { get; private set; }

    public void OnMatchRequestLost() => SetIsMatchRequest(false);

    public void OnDeckSelectLost()
    {
        SetIsDeckSelect(false);

        foreach (var deckOb in cachedState.decks)
        {
            deckOb.rootNodeAddr = 0;
        }
    }

    public unsafe void OnAddonUpdateMatchRequest(nint addonPtr)
    {
        var addon = (AddonRequest*)addonPtr;
        if (addon == null)
        {
            return;
        }

        ApplyMatchRequestState(addon, true);
    }

    public unsafe void SyncMatchRegistrationFromLiveAddon()
    {
        if (!TriadLocalClientStructs.TryGetRequest(out var addon))
        {
            return;
        }

        ApplyMatchRequestState(addon, false);

        if (TriadCardFarmSession.IsModeActive() || TriadCardFarmSession.SessionActive)
        {
            TriadCardFarmSession.EnsureArmed();
        }
    }

    private unsafe void ApplyMatchRequestState(AddonRequest* addon, bool notifyDeckEval)
    {
        var wasFirstShow = !HasMatchRequestUI;
        var previousNpc = cachedState.npc;

        TriadPrepRequestReader.Read(addon, cachedState);

        if (wasFirstShow)
        {
            SetIsMatchRequest(true);
        }

        var prepChanged = !string.IsNullOrWhiteSpace(cachedState.npc) &&
                          (wasFirstShow || cachedState.npc != previousNpc || TriadRun.preGameNpc == null);

        if (prepChanged)
        {
            TriadRun.OnMatchPrepDetected(cachedState);

            if (notifyDeckEval && (wasFirstShow || cachedState.npc != previousNpc))
            {
                OnUIStateChanged?.Invoke(cachedState);
            }
        }
        else if (!string.IsNullOrWhiteSpace(cachedState.npc) && TriadRun.SyncPrepRulesFromState(cachedState))
        {
            TriadRun.OnPrepRulesUpdated(TriadRun.preGameNpc!);
        }
        else if (notifyDeckEval && wasFirstShow && !string.IsNullOrWhiteSpace(cachedState.npc))
        {
            OnUIStateChanged?.Invoke(cachedState);
        }

        (cachedState.screenPos, cachedState.screenSize) =
            GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);
    }

    public unsafe void SyncDeckSelectFromLiveAddon()
    {
        if (!TriadLocalClientStructs.TryGetSelDeck(out var addon))
        {
            return;
        }

        OnAddonUpdateDeckSelect((nint)addon);
    }

    public unsafe void RefreshDeckSelectList(nint addonPtr)
    {
        var addon = (AddonTripleTriadSelDeck*)addonPtr;
        if (addon == null)
        {
            return;
        }

        UpdateDeckSelect(addon);
    }

    public unsafe void OnAddonUpdateDeckSelect(nint addonPtr)
    {
        var addon = (AddonTripleTriadSelDeck*)addonPtr;
        if (addon == null)
        {
            return;
        }

        var wasFirstShow = !HasDeckSelectionUI;
        var previousDeckCount = cachedState.decks.Count;
        UpdateDeckSelect(addon);

        var newHasDeckSelectUI = cachedState.decks.Count > 0;
        if (newHasDeckSelectUI)
        {
            if (wasFirstShow)
            {
                SetIsDeckSelect(true);
            }

            if (wasFirstShow || cachedState.decks.Count != previousDeckCount)
            {
                OnUIStateChanged?.Invoke(cachedState);
            }

            foreach (var deckOb in cachedState.decks)
            {
                var updateNode = (AtkResNode*)deckOb.rootNodeAddr;
                if (updateNode != null)
                {
                    (deckOb.screenPos, deckOb.screenSize) = GUINodeUtils.GetNodePosAndSize(updateNode);
                }
            }
        }
        else if (wasFirstShow)
        {
            SetIsDeckSelect(true);
            OnUIStateChanged?.Invoke(cachedState);
        }
    }

    private unsafe void UpdateDeckSelect(AddonTripleTriadSelDeck* addon) =>
        TriadPrepDeckSelectReader.Read(addon, cachedState, shouldScanDeckData);

    private void SetIsMatchRequest(bool value)
    {
        if (HasMatchRequestUI != value)
        {
            HasMatchRequestUI = value;
            OnMatchRequestChanged?.Invoke(value);
        }
    }

    private void SetIsDeckSelect(bool value)
    {
        if (HasDeckSelectionUI != value)
        {
            HasDeckSelectionUI = value;
            OnDeckSelectionChanged?.Invoke(value);
        }
    }
}

public class UIReaderTriadPrepMatchRequest : IUIReader
{
    public UIReaderTriadPrep? parentReader;

    public string GetAddonName() => "TripleTriadRequest";

    public void OnAddonLost() => parentReader?.OnMatchRequestLost();

    public void OnAddonUpdate(nint addonPtr) => parentReader?.OnAddonUpdateMatchRequest(addonPtr);

    public void OnAddonShown(nint addonPtr) => OnAddonUpdate(addonPtr);
}

public class UIReaderTriadPrepDeckSelect : IUIReader
{
    public UIReaderTriadPrep? parentReader;

    public string GetAddonName() => "TripleTriadSelDeck";

    public void OnAddonLost() => parentReader?.OnDeckSelectLost();

    public void OnAddonUpdate(nint addonPtr) => parentReader?.OnAddonUpdateDeckSelect(addonPtr);

    public void OnAddonShown(nint addonPtr) => OnAddonUpdate(addonPtr);
}

public class UIStateTriadPrepDeck
{
    public string[] cardTexPaths = new string[5];
    public int id;
    public string name = string.Empty;

    public ulong rootNodeAddr;

    public Vector2 screenPos;
    public Vector2 screenSize;
}

public class UIStateTriadPrep
{
    public List<UIStateTriadPrepDeck> decks = [];
    public string npc = string.Empty;
    public string[] rules = new string[4];

    public Vector2 screenPos;
    public Vector2 screenSize;
}
