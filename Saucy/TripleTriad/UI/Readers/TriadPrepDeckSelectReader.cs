using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadPrepDeckSelectReader
{
    private const int DeckListNodeIndex = 4;

    public static void Read(AddonTripleTriadSelDeck* addon, UIStateTriadPrep state, bool scanCardTextures)
    {
        state.decks.Clear();

        var baseNode = &addon->AtkUnitBase;
        if (TryReadDeckListAt(baseNode, state, scanCardTextures))
        {
            return;
        }

        TryReadGenericDeckLists(baseNode, state, scanCardTextures);
    }

    private static bool TryReadDeckListAt(AtkUnitBase* baseNode, UIStateTriadPrep state, bool scanCardTextures)
    {
        var nodeA = baseNode->UldManager.NodeListCount >= DeckListNodeIndex + 1
            ? baseNode->UldManager.NodeList[DeckListNodeIndex]
            : null;
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
                id = state.decks.Count, rootNodeAddr = (ulong)rowNode, name = ReadDeckRowName(rowNode)
            };

            if (scanCardTextures)
            {
                TryScanDeckRowCards(rowNode, deckOb);
            }

            state.decks.Add(deckOb);
        }

        return state.decks.Count > 0;
    }

    private static void TryReadGenericDeckLists(AtkUnitBase* baseNode, UIStateTriadPrep state, bool scanCardTextures)
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

                if (scanCardTextures)
                {
                    TryScanDeckRowCards(rowNode, deckOb);
                }

                parsedRows.Add(deckOb);
            }

            if (parsedRows.Count > 0)
            {
                state.decks.AddRange(parsedRows);
                return;
            }
        }
    }

    private static string ReadDeckRowName(AtkResNode* rowNode)
    {
        var name = GUINodeUtils.GetNodeText(GUINodeUtils.PickChildNode(rowNode, 11, 12));
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return ExtractDeckRowName(rowNode) ?? string.Empty;
    }

    private static void TryScanDeckRowCards(AtkResNode* rowNode, UIStateTriadPrepDeck deckOb)
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

    private static AtkResNode* FindChildComponentWithChildCount(AtkResNode* rowNode, int childCount)
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

    private static bool RowLooksLikeDeckRow(AtkResNode* rowNode) =>
        FindChildComponentWithChildCount(rowNode, 5) != null ||
        GUINodeUtils.PickChildNode(rowNode, 3, 12) != null;

    private static string? ExtractDeckRowName(AtkResNode* rowNode)
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

        if (!RowLooksLikeDeckRow(rowNode))
        {
            return null;
        }

        string? bestName = null;
        foreach (var child in GUINodeUtils.GetAllChildNodes(rowNode) ?? [])
        {
            var text = GUINodeUtils.GetNodeText(child);
            if (string.IsNullOrWhiteSpace(text) || text.Length is < 2 or > 64 || LooksLikePrepChromeText(text))
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

    private static bool LooksLikePrepChromeText(string text)
    {
        if (int.TryParse(text, out var _))
        {
            return true;
        }

        var colonCount = 0;
        var digitCount = 0;
        foreach (var ch in text)
        {
            if (ch == ':')
            {
                colonCount++;
            }
            else if (char.IsDigit(ch))
            {
                digitCount++;
            }
        }

        return colonCount >= 2 && digitCount >= 2;
    }
}
