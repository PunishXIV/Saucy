using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.Framework;
using Saucy.TripleTriad.Utils;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static ECommons.GenericHelpers;

namespace Saucy.JumboCactpot;

internal static unsafe class LotteryWeeklyInputHelper
{
    private const int ExpectedDigitCount = 4;
    private const uint PurchaseButtonNodeId = 31;
    private const uint RandomButtonNodeId = 32;
    private const uint MaxScannedNodeId = 80;
    private const int ExpectedKeypadButtons = 10;

    private static uint[]? cachedKeypadNodeIds;

    private static readonly Regex BracketedNumberPattern = new(@"\[\s*(\d{4})\s*\]", RegexOptions.Compiled);

    public static void ClearDiscoveryCache() => cachedKeypadNodeIds = null;

    public static bool TryReadTicketNumber(AtkUnitBase* addon, out int number)
    {
        number = 0;
        if (addon is null)
        {
            return false;
        }

        return TryReadFromBracketedText(addon, out number) ||
               TryReadFromDisplayDigits(addon, out number);
    }

    public static bool TryClickKeypadDigit(AtkUnitBase* addon, int digit) =>
        digit is >= 0 and <= 9 &&
        TryGetKeypadNodeIds(addon, out var keypadNodeIds) &&
        AddonButton.TryClick(addon, keypadNodeIds[digit]);

    public static void CollectDebugLines(AtkUnitBase* addon, List<string> lines)
    {
        if (addon is null)
        {
            lines.Add("LotteryWeeklyInput addon pointer is null.");
            return;
        }

        lines.Add($"LotteryWeeklyInput visible={addon->IsVisible} ready={IsAddonReady(addon)}");
        if (TryGetKeypadNodeIds(addon, out var keypadNodeIds))
        {
            lines.Add("Discovered keypad node IDs:");
            for (var digit = 0; digit < ExpectedKeypadButtons; digit++)
            {
                lines.Add($"  [{digit}] node={keypadNodeIds[digit]}");
            }
        }
        else
        {
            lines.Add("Discovered keypad node IDs: failed");
        }

        if (TryReadTicketNumber(addon, out var current))
        {
            Span<char> formattedCurrent = stackalloc char[4];
            JumboCactpotSettings.FormatTicketNumber(current, formattedCurrent);
            lines.Add($"Read ticket number: {new string(formattedCurrent)} ({current})");
        }
        else
        {
            lines.Add("Read ticket number: failed");
        }

        lines.Add("Visible buttons (GetComponentButtonById scan):");
        var buttons = CollectVisibleButtonsById(addon);
        buttons.Sort((left, right) =>
        {
            var yCompare = left.Y.CompareTo(right.Y);
            return yCompare != 0 ? yCompare : left.X.CompareTo(right.X);
        });
        foreach (var button in buttons)
        {
            lines.Add(
                $"  nodeId={button.NodeId} label='{button.Label}' enabled={button.Enabled} x={button.X:F0} y={button.Y:F0}");
        }

        lines.Add("Visible text nodes (GetTextNodeById scan):");
        var textNodes = CollectVisibleTextNodesById(addon);
        textNodes.Sort((left, right) => left.NodeId.CompareTo(right.NodeId));
        foreach (var textNode in textNodes)
        {
            lines.Add($"  nodeId={textNode.NodeId} text='{textNode.Text}' x={textNode.X:F0} y={textNode.Y:F0}");
        }
    }

    private static bool TryGetKeypadNodeIds(AtkUnitBase* addon, out uint[] keypadNodeIds)
    {
        keypadNodeIds = [];
        if (cachedKeypadNodeIds is not null && cachedKeypadNodeIds.Length == ExpectedKeypadButtons)
        {
            keypadNodeIds = cachedKeypadNodeIds;
            return true;
        }

        if (!TryDiscoverKeypadNodeIds(addon, out keypadNodeIds))
        {
            return false;
        }

        cachedKeypadNodeIds = keypadNodeIds;
        return true;
    }

    private static bool TryDiscoverKeypadNodeIds(AtkUnitBase* addon, out uint[] keypadNodeIds)
    {
        keypadNodeIds = new uint[ExpectedKeypadButtons];
        var candidatesByDigit = new List<VisibleButton>[ExpectedKeypadButtons];
        for (var digit = 0; digit < ExpectedKeypadButtons; digit++)
        {
            candidatesByDigit[digit] = [];
        }

        foreach (var button in CollectVisibleButtonsById(addon))
        {
            if (button.NodeId is PurchaseButtonNodeId or RandomButtonNodeId ||
                !TryParseKeypadDigit(button.Label, out var digit))
            {
                continue;
            }

            candidatesByDigit[digit].Add(button);
        }

        var foundDigits = 0;
        for (var digit = 0; digit < ExpectedKeypadButtons; digit++)
        {
            var candidates = candidatesByDigit[digit];
            if (candidates.Count == 0)
            {
                continue;
            }

            // Display diamonds reuse digit labels above the keypad; prefer the lower keypad row.
            var keypadButton = candidates[0];
            for (var i = 1; i < candidates.Count; i++)
            {
                if (candidates[i].Y > keypadButton.Y)
                {
                    keypadButton = candidates[i];
                }
            }

            keypadNodeIds[digit] = keypadButton.NodeId;
            foundDigits++;
        }

        return foundDigits == ExpectedKeypadButtons;
    }

    private static bool TryReadFromBracketedText(AtkUnitBase* addon, out int number)
    {
        number = 0;
        foreach (var textNode in CollectVisibleTextNodesById(addon))
        {
            var match = BracketedNumberPattern.Match(textNode.Text);
            if (!match.Success)
            {
                continue;
            }

            return JumboCactpotSettings.TryParseTicketNumber(match.Groups[1].Value, out number);
        }

        return false;
    }

    private static bool TryReadFromDisplayDigits(AtkUnitBase* addon, out int number)
    {
        number = 0;
        var digitNodes = new List<DigitTextNode>();
        foreach (var textNode in CollectVisibleTextNodesById(addon))
        {
            if (textNode.Text.Length != 1 || textNode.Text[0] is < '0' or > '9')
            {
                continue;
            }

            digitNodes.Add(new DigitTextNode
            {
                X = textNode.X,
                Y = textNode.Y,
                Digit = textNode.Text[0] - '0',
            });
        }

        if (digitNodes.Count < ExpectedDigitCount)
        {
            return false;
        }

        digitNodes.Sort((left, right) =>
        {
            var yCompare = left.Y.CompareTo(right.Y);
            return yCompare != 0 ? yCompare : left.X.CompareTo(right.X);
        });

        var topY = digitNodes[0].Y;
        const float displayRowTolerance = 12f;
        var rowDigits = new List<DigitTextNode>();
        foreach (var digitNode in digitNodes)
        {
            if (Math.Abs(digitNode.Y - topY) <= displayRowTolerance)
            {
                rowDigits.Add(digitNode);
            }
        }

        if (rowDigits.Count < ExpectedDigitCount)
        {
            rowDigits = digitNodes.GetRange(0, Math.Min(ExpectedDigitCount, digitNodes.Count));
        }

        rowDigits.Sort((left, right) => left.X.CompareTo(right.X));
        if (rowDigits.Count < ExpectedDigitCount)
        {
            return false;
        }

        Span<int> digits = stackalloc int[ExpectedDigitCount];
        for (var i = 0; i < ExpectedDigitCount; i++)
        {
            digits[i] = rowDigits[i].Digit;
        }

        number = ComposeTicketNumber(digits);
        return true;
    }

    private static bool TryParseKeypadDigit(string? label, out int digit)
    {
        digit = 0;
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        label = label.Trim();
        if (label.Length != 1 || label[0] is < '0' or > '9')
        {
            return false;
        }

        digit = label[0] - '0';
        return true;
    }

    private readonly struct VisibleButton
    {
        public uint NodeId { get; init; }

        public string Label { get; init; }

        public float X { get; init; }

        public float Y { get; init; }

        public bool Enabled { get; init; }
    }

    private readonly struct VisibleTextNode
    {
        public uint NodeId { get; init; }

        public string Text { get; init; }

        public float X { get; init; }

        public float Y { get; init; }
    }

    private readonly struct DigitTextNode
    {
        public float X { get; init; }

        public float Y { get; init; }

        public int Digit { get; init; }
    }

    private static List<VisibleButton> CollectVisibleButtonsById(AtkUnitBase* addon)
    {
        var buttons = new List<VisibleButton>();
        for (uint nodeId = 1; nodeId <= MaxScannedNodeId; nodeId++)
        {
            var button = addon->GetComponentButtonById(nodeId);
            if (button is null || button->AtkResNode is null || !button->AtkResNode->IsVisible())
            {
                continue;
            }

            var pos = GUINodeUtils.GetNodePosition(button->AtkResNode);
            buttons.Add(new VisibleButton
            {
                NodeId = nodeId,
                Label = GetButtonLabel(button) ?? string.Empty,
                X = pos.X,
                Y = pos.Y,
                Enabled = button->IsEnabled,
            });
        }

        return buttons;
    }

    private static List<VisibleTextNode> CollectVisibleTextNodesById(AtkUnitBase* addon)
    {
        var textNodes = new List<VisibleTextNode>();
        for (uint nodeId = 1; nodeId <= MaxScannedNodeId; nodeId++)
        {
            var textNode = addon->GetTextNodeById(nodeId);
            if (textNode is null || !textNode->AtkResNode.IsVisible())
            {
                continue;
            }

            var text = GUINodeUtils.GetNodeText(&textNode->AtkResNode) ?? string.Empty;
            var pos = GUINodeUtils.GetNodePosition(&textNode->AtkResNode);
            textNodes.Add(new VisibleTextNode
            {
                NodeId = nodeId,
                Text = text,
                X = pos.X,
                Y = pos.Y,
            });
        }

        return textNodes;
    }

    private static string? GetButtonLabel(AtkComponentButton* button)
    {
        if (button is null)
        {
            return null;
        }

        var textNode = button->ButtonTextNode;
        if (textNode is not null)
        {
            var text = GUINodeUtils.GetNodeText(&textNode->AtkResNode);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (button->AtkResNode->Type != NodeType.Component)
        {
            return null;
        }

        var component = ((AtkComponentNode*)button->AtkResNode)->Component;
        if (component is null)
        {
            return null;
        }

        ref var uld = ref component->UldManager;
        for (var i = 0; i < uld.NodeListCount; i++)
        {
            var node = uld.NodeList[i];
            if (node is null || node->Type != NodeType.Text || !node->IsVisible())
            {
                continue;
            }

            var text = GUINodeUtils.GetNodeText(node);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static int ComposeTicketNumber(ReadOnlySpan<int> digits) =>
        digits[0] * 1000 + digits[1] * 100 + digits[2] * 10 + digits[3];
}
