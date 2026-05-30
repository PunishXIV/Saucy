using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using static ECommons.GenericHelpers;
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
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        ApplyMatchRequestState(baseNode, true);
    }

    public unsafe void SyncMatchRegistrationFromLiveAddon()
    {
        if (!TryGetAddonByName<AtkUnitBase>("TripleTriadRequest", out var addon) || !addon->IsVisible)
        {
            return;
        }

        ApplyMatchRequestState(addon, false);

        if (TriadAutomater.IsCardFarmModeActive() || TriadAutomater.CardFarmSessionActive)
        {
            TriadAutomater.EnsureCardFarmArmed();
        }
    }

    private unsafe void ApplyMatchRequestState(AtkUnitBase* baseNode, bool notifyDeckEval)
    {
        var wasFirstShow = !HasMatchRequestUI;
        var previousNpc = cachedState.npc;

        UpdateRequest(baseNode);

        if (wasFirstShow)
        {
            SetIsMatchRequest(true);
        }

        var prepChanged = !string.IsNullOrWhiteSpace(cachedState.npc) &&
                          (wasFirstShow || cachedState.npc != previousNpc || TTSolver.preGameNpc == null);

        if (prepChanged)
        {
            TTSolver.OnMatchPrepDetected(cachedState);

            if (notifyDeckEval && (wasFirstShow || cachedState.npc != previousNpc))
            {
                OnUIStateChanged?.Invoke(cachedState);
            }
        }
        else if (notifyDeckEval && wasFirstShow && !string.IsNullOrWhiteSpace(cachedState.npc))
        {
            OnUIStateChanged?.Invoke(cachedState);
        }

        (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(baseNode->RootNode);
    }

    public unsafe void SyncDeckSelectFromLiveAddon()
    {
        if (!TryGetAddonByName<AtkUnitBase>("TripleTriadSelDeck", out var addon) || !addon->IsVisible)
        {
            return;
        }

        OnAddonUpdateDeckSelect((nint)addon);
    }

    /// <summary>Re-parses deck rows from the live addon (e.g. after a profile deck write).</summary>
    public unsafe void RefreshDeckSelectList(nint addonPtr)
    {
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        UpdateDeckSelect(baseNode);
    }

    public unsafe void OnAddonUpdateDeckSelect(nint addonPtr)
    {
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        var wasFirstShow = !HasDeckSelectionUI;
        var previousDeckCount = cachedState.decks.Count;
        UpdateDeckSelect(baseNode);

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

    private unsafe void UpdateRequest(AtkUnitBase* baseNode)
    {
        cachedState.decks.Clear();
        cachedState.npc = string.Empty;

        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        if (nodeArrL0 != null && nodeArrL0.Length >= 9)
        {
            UpdateRequestLegacy(nodeArrL0);
        }

        if (string.IsNullOrWhiteSpace(cachedState.npc))
        {
            cachedState.npc = TryFindNpcNameOnRequest(baseNode) ?? string.Empty;
        }
    }

    private unsafe void UpdateRequestLegacy(AtkResNode*[] nodeArrL0)
    {
        var nodeRulesA = GUINodeUtils.PickNode(nodeArrL0, 6, nodeArrL0.Length);
        var nodeArrL1A = GUINodeUtils.GetImmediateChildNodes(nodeRulesA);
        if (nodeArrL1A != null)
        {
            var nodeL2A1 = GUINodeUtils.PickNode(nodeArrL1A, 0, nodeArrL1A.Length);
            cachedState.rules[3] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A1, 2, 3)) ?? "";
            var nodeL2A2 = GUINodeUtils.PickNode(nodeArrL1A, 1, nodeArrL1A.Length);
            cachedState.rules[2] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A2, 2, 3)) ?? "";
        }

        var nodeRulesB = GUINodeUtils.PickNode(nodeArrL0, 7, nodeArrL0.Length);
        var nodeArrL1B = GUINodeUtils.GetImmediateChildNodes(nodeRulesB);
        if (nodeArrL1B != null)
        {
            var nodeL2B1 = GUINodeUtils.PickNode(nodeArrL1B, 0, nodeArrL1B.Length);
            cachedState.rules[1] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B1, 2, 3)) ?? "";
            var nodeL2B2 = GUINodeUtils.PickNode(nodeArrL1B, 1, nodeArrL1B.Length);
            cachedState.rules[0] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B2, 2, 3)) ?? "";
        }

        var nodeNpc = GUINodeUtils.PickNode(nodeArrL0, 8, nodeArrL0.Length);
        cachedState.npc = GUINodeUtils.GetNodeText(GUINodeUtils.GetChildNode(nodeNpc)) ?? "";
    }

    private static unsafe string? TryFindNpcNameOnRequest(AtkUnitBase* baseNode)
    {
        var parseCtx = new GameUIParser();

        for (var i = 0; i < baseNode->UldManager.NodeListCount; i++)
        {
            if (TryParseNpcLabel(parseCtx, GUINodeUtils.GetNodeText(baseNode->UldManager.NodeList[i]), out var npcName))
            {
                return npcName;
            }
        }

        foreach (var node in GUINodeUtils.GetAllChildNodes(baseNode->RootNode) ?? [])
        {
            if (node == null)
            {
                continue;
            }

            if (TryParseNpcLabel(parseCtx, GUINodeUtils.GetNodeText(node), out var npcName))
            {
                return npcName;
            }
        }

        return null;
    }

    private static bool TryParseNpcLabel(GameUIParser parseCtx, string? text, out string? npcName)
    {
        npcName = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.Length < 3)
        {
            return false;
        }

        if (text is "None" or "Challenge" or "Quit" or "Match Registration" or "Please confirm the rules and rewards." or
            "Regional Rules" or "Match Rules" or "Possible Prize" or "Match Fee" or "Time Remaining")
        {
            return false;
        }

        if (int.TryParse(text, out var _))
        {
            return false;
        }

        if (parseCtx.ParseNpc(text, false) != null ||
            parseCtx.ParseNpcNameStart(text, false) != null)
        {
            npcName = text;
            return true;
        }

        return false;
    }

    private unsafe void UpdateDeckSelect(AtkUnitBase* baseNode)
    {
        cachedState.decks.Clear();

        if (TryUpdateDeckSelectLegacy(baseNode))
        {
            return;
        }

        TryUpdateDeckSelectGeneric(baseNode);
    }

    private unsafe bool TryUpdateDeckSelectLegacy(AtkUnitBase* baseNode)
    {
        var nodeA = baseNode->UldManager.NodeListCount >= 5 ? baseNode->UldManager.NodeList[4] : null;
        if (nodeA == null || (int)nodeA->Type <= 1000)
        {
            return false;
        }

        var compNodeA = (AtkComponentNode*)nodeA;
        var rowCount = compNodeA->Component->UldManager.NodeListCount;
        if (rowCount <= 0)
        {
            return false;
        }

        for (var rowIdx = 0; rowIdx < rowCount; rowIdx++)
        {
            var rowNode = compNodeA->Component->UldManager.NodeList[rowIdx];
            if (rowNode == null)
            {
                continue;
            }

            var cardRowNode = GUINodeUtils.PickChildNode(rowNode, 3, 12);
            if (cardRowNode == null && !RowLooksLikeDeckRow(rowNode))
            {
                continue;
            }

            var deckOb = new UIStateTriadPrepDeck
            {
                id = cachedState.decks.Count, rootNodeAddr = (ulong)rowNode, name = ReadDeckRowName(rowNode)
            };

            if (shouldScanDeckData)
            {
                TryScanDeckRowCards(rowNode, deckOb);
            }

            cachedState.decks.Add(deckOb);
        }

        return cachedState.decks.Count > 0;
    }

    private unsafe void TryUpdateDeckSelectGeneric(AtkUnitBase* baseNode)
    {
        for (var listIdx = 0; listIdx < baseNode->UldManager.NodeListCount; listIdx++)
        {
            var nodeA = baseNode->UldManager.NodeList[listIdx];
            if (nodeA == null || (int)nodeA->Type <= 1000)
            {
                continue;
            }

            var compNodeA = (AtkComponentNode*)nodeA;
            var rowCount = compNodeA->Component->UldManager.NodeListCount;
            if (rowCount <= 0)
            {
                continue;
            }

            var parsedRows = new List<UIStateTriadPrepDeck>();
            for (var rowIdx = 0; rowIdx < rowCount; rowIdx++)
            {
                var rowNode = compNodeA->Component->UldManager.NodeList[rowIdx];
                if (rowNode == null)
                {
                    continue;
                }

                var deckName = ExtractDeckRowName(rowNode);
                if (string.IsNullOrWhiteSpace(deckName) && !RowLooksLikeDeckRow(rowNode))
                {
                    continue;
                }

                var deckOb = new UIStateTriadPrepDeck
                {
                    id = parsedRows.Count, rootNodeAddr = (ulong)rowNode, name = deckName ?? string.Empty
                };

                if (shouldScanDeckData)
                {
                    TryScanDeckRowCards(rowNode, deckOb);
                }

                parsedRows.Add(deckOb);
            }

            if (parsedRows.Count > 0)
            {
                cachedState.decks.AddRange(parsedRows);
                return;
            }
        }
    }

    private static unsafe string ReadDeckRowName(AtkResNode* rowNode)
    {
        var name = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(rowNode, 11, 12));
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return ExtractDeckRowName(rowNode) ?? string.Empty;
    }

    private static unsafe void TryScanDeckRowCards(AtkResNode* rowNode, UIStateTriadPrepDeck deckOb)
    {
        var nodeC1 = GUINodeUtils.PickChildNode(rowNode, 3, 12);
        if (nodeC1 == null)
        {
            nodeC1 = FindChildComponentWithChildCount(rowNode, 5);
        }

        if (nodeC1 == null)
        {
            return;
        }

        var nodeArrC1 = GUINodeUtils.GetImmediateChildNodes(nodeC1);
        if (nodeArrC1 == null || nodeArrC1.Length != 5)
        {
            return;
        }

        for (var idxC = 0; idxC < nodeArrC1.Length; idxC++)
        {
            var nodeD = GUINodeUtils.PickChildNode(nodeArrC1[idxC], 1, 2);
            var nodeE = GUINodeUtils.PickChildNode(nodeD, 0, 4);
            var texPath = GUINodeUtils.GetNodeTexturePath(nodeE);
            if (string.IsNullOrEmpty(texPath))
            {
                break;
            }

            deckOb.cardTexPaths[idxC] = texPath;
        }
    }

    private static unsafe AtkResNode* FindChildComponentWithChildCount(AtkResNode* rowNode, int childCount)
    {
        foreach (var child in GUINodeUtils.GetAllChildNodes(rowNode) ?? [])
        {
            if (child == null || (int)child->Type <= 1000)
            {
                continue;
            }

            var comp = ((AtkComponentNode*)child)->Component;
            if (comp != null && comp->UldManager.NodeListCount == childCount)
            {
                return child;
            }
        }

        return null;
    }

    private static unsafe bool RowLooksLikeDeckRow(AtkResNode* rowNode) =>
        FindChildComponentWithChildCount(rowNode, 5) != null ||
        GUINodeUtils.PickChildNode(rowNode, 3, 12) != null;

    private static unsafe string? ExtractDeckRowName(AtkResNode* rowNode)
    {
        foreach (var childCount in new[]
        {
            12, 13, 11, 10, 14
        })
        {
            var nameNode = GUINodeUtils.PickChildNode(rowNode, 11, childCount);
            var name = GUINodeUtils.GetNodeText(nameNode);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        string? bestName = null;
        foreach (var child in GUINodeUtils.GetAllChildNodes(rowNode) ?? [])
        {
            var text = GUINodeUtils.GetNodeText(child);
            if (string.IsNullOrWhiteSpace(text) || text.Length is < 2 or > 64)
            {
                continue;
            }

            if (text.Contains("Time Remaining", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Recommended", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bestName = text;
            if (text.Contains("(Saucy)", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return bestName;
    }

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

// helper class for scheduler: handles single octave performance UI and passes all notifies to parent
public class UIReaderTriadPrepMatchRequest : IUIReader
{
    public UIReaderTriadPrep? parentReader;

    public string GetAddonName() => "TripleTriadRequest";

    public void OnAddonLost() => parentReader?.OnMatchRequestLost();

    public void OnAddonUpdate(nint addonPtr) => parentReader?.OnAddonUpdateMatchRequest(addonPtr);

    public void OnAddonShown(nint addonPtr) => OnAddonUpdate(addonPtr);
}

// helper class for scheduler: handles three octaves performance UI and passes all notifies to parent
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
