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
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        if (!HasMatchRequestUI)
        {
            UpdateRequest(baseNode);
            SetIsMatchRequest(true);

            // notify always, if deck data depends on UI, it will be ignored by solver
            OnUIStateChanged?.Invoke(cachedState);
        }

        (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(baseNode->RootNode);
    }

    public unsafe void OnAddonUpdateDeckSelect(nint addonPtr)
    {
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        if (!HasDeckSelectionUI)
        {
            UpdateDeckSelect(baseNode);
        }

        var newHasDeckSelectUI = cachedState.decks.Count > 0;

        // notify only when deck data is coming from UI
        if (!HasDeckSelectionUI && newHasDeckSelectUI)
        {
            SetIsDeckSelect(true);

            if (shouldScanDeckData)
            {
                OnUIStateChanged?.Invoke(cachedState);
            }
        }

        if (newHasDeckSelectUI)
        {
            foreach (var deckOb in cachedState.decks)
            {
                var updateNode = (AtkResNode*)deckOb.rootNodeAddr;
                if (updateNode != null)
                {
                    (deckOb.screenPos, deckOb.screenSize) = GUINodeUtils.GetNodePosAndSize(updateNode);
                }
            }
        }
    }

    private unsafe void UpdateRequest(AtkUnitBase* baseNode)
    {
        // 13 child nodes (sibling scan, root node list huge)
        //     [6] match/tournament rules, simple node
        //         [0] comp node with 3 children: [2] = text
        //         [1] comp node with 3 children: [2] = text
        //     [7] region rules, simple node
        //         [0] comp node with 3 children: [2] = text
        //         [1] comp node with 3 children: [2] = text
        //     [8] npc, simple node
        //         [0] text

        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(baseNode->RootNode);
        var nodeRulesA = GUINodeUtils.PickNode(nodeArrL0, 6, 13);
        var nodeArrL1A = GUINodeUtils.GetImmediateChildNodes(nodeRulesA);
        var nodeL2A1 = GUINodeUtils.PickNode(nodeArrL1A, 0, 6);
        cachedState.rules[3] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A1, 2, 3)) ?? "";
        var nodeL2A2 = GUINodeUtils.PickNode(nodeArrL1A, 1, 6);
        cachedState.rules[2] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2A2, 2, 3)) ?? "";

        var nodeRulesB = GUINodeUtils.PickNode(nodeArrL0, 7, 13);
        var nodeArrL1B = GUINodeUtils.GetImmediateChildNodes(nodeRulesB);
        var nodeL2B1 = GUINodeUtils.PickNode(nodeArrL1B, 0, 3);
        cachedState.rules[1] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B1, 2, 3)) ?? "";
        var nodeL2B2 = GUINodeUtils.PickNode(nodeArrL1B, 1, 3);
        cachedState.rules[0] = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(nodeL2B2, 2, 3)) ?? "";

        var nodeNpc = GUINodeUtils.PickNode(nodeArrL0, 8, 13);
        cachedState.npc = GUINodeUtils.GetNodeText(GUINodeUtils.GetChildNode(nodeNpc)) ?? "";

        cachedState.decks.Clear();
    }

    private unsafe void UpdateDeckSelect(AtkUnitBase* baseNode)
    {
        // 5 child nodes (node list)
        //    [4] list 
        //        [x] comp nodes, each has 12 child nodes
        //            [3] simple node with 5 children, each is a card
        //                [x] comp node with 2 children
        //                    [1] comp node with 4 children
        //                        [0] card image
        //            [11] text, deck name

        cachedState.decks.Clear();

        var nodeA = (baseNode->UldManager.NodeListCount == 5) ? baseNode->UldManager.NodeList[4] : null;
        if (nodeA != null && (int)nodeA->Type > 1000)
        {
            var compNodeA = (AtkComponentNode*)nodeA;
            for (var idxA = 0; idxA < compNodeA->Component->UldManager.NodeListCount; idxA++)
            {
                var nodeB = compNodeA->Component->UldManager.NodeList[idxA];
                var nodeC1 = GUINodeUtils.PickChildNode(nodeB, 3, 12);

                if (nodeC1 != null)
                {
                    var deckOb = new UIStateTriadPrepDeck
                    {
                        id = cachedState.decks.Count, rootNodeAddr = (ulong)nodeB
                    };

                    if (shouldScanDeckData)
                    {
                        var nodeArrC1 = GUINodeUtils.GetImmediateChildNodes(nodeC1);
                        if (nodeArrC1 != null && nodeArrC1.Length == 5)
                        {
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

                        var nodeC2 = GUINodeUtils.PickChildNode(nodeB, 11, 12);
                        deckOb.name = GUINodeUtils.GetNodeText(nodeC2) ?? "";
                    }

                    cachedState.decks.Add(deckOb);
                }
            }
        }
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

    public void OnAddonShown(nint addonPtr)
    {
        // nothing to cache
    }

    public void OnAddonUpdate(nint addonPtr) => parentReader?.OnAddonUpdateMatchRequest(addonPtr);
}

// helper class for scheduler: handles three octaves performance UI and passes all notifies to parent
public class UIReaderTriadPrepDeckSelect : IUIReader
{
    public UIReaderTriadPrep? parentReader;

    public string GetAddonName() => "TripleTriadSelDeck";

    public void OnAddonLost() => parentReader?.OnDeckSelectLost();

    public void OnAddonShown(nint addonPtr)
    {
        // nothing to cache
    }

    public void OnAddonUpdate(nint addonPtr) => parentReader?.OnAddonUpdateDeckSelect(addonPtr);
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
