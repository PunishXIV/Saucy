using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

internal static unsafe class TriadCardListSelectionReader
{
    private static readonly string[] CatalogNumberPrefixes =
    [
        "n°", "nº", "no.", "no ", "nr.", "nr ", "№", "°", "º", "#"
    ];
    public static int ReadSelectedCardId(
        AddonGSInfoCardList* addon,
        byte filterMode = 0,
        AgentGoldSaucer* agent = null,
        int displayCardId = -1)
    {
        if (displayCardId < 0)
        {
            displayCardId = TryParseCardIdFromDisplayLabel(addon);
        }

        var gridCardId = TryReadCardIdFromGrid(addon, filterMode, agent, displayCardId);
        var detailCardId = TryReadCardIdFromDetailPane(addon, displayCardId);

        if (gridCardId > 0)
        {
            if (detailCardId <= 0 || detailCardId == gridCardId)
            {
                return gridCardId;
            }

            if (ShouldPreferGridOverDetail(gridCardId, detailCardId, addon, displayCardId))
            {
                return gridCardId;
            }
        }

        if (detailCardId > 0)
        {
            return detailCardId;
        }

        return gridCardId > 0 ? gridCardId : TryParseCardIdFromDescription(addon);
    }

    public static bool IconMatchesCard(int iconId, int cardId) =>
        cardId > 0 && TriadCardDB.Get().TryGetCardIdFromIconId(iconId) == cardId;

    public static int TryParseCardIdFromDisplayLabel(AddonGSInfoCardList* addon)
    {
        if (addon->SelectedCardNumber == null)
        {
            return -1;
        }

        return TryParseCardIdFromDisplayLabel(GUINodeUtils.GetNodeText(&addon->SelectedCardNumber->AtkResNode));
    }

    public static int TryParseCardIdFromDisplayLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        var exIndex = text.IndexOf("Ex.", StringComparison.OrdinalIgnoreCase);
        if (exIndex >= 0)
        {
            var exNumber = ParseDisplayNumber(text[(exIndex + 3)..]);
            if (exNumber > 0)
            {
                return TriadCardDB.Get().FindBySortOrder(1000 + exNumber)?.Id ?? -1;
            }
        }

        var noIndex = text.IndexOf("No.", StringComparison.OrdinalIgnoreCase);
        if (noIndex >= 0)
        {
            var noNumber = ParseDisplayNumber(text[(noIndex + 3)..]);
            if (noNumber > 0)
            {
                return TriadCardDB.Get().FindBySortOrder(noNumber)?.Id ?? -1;
            }
        }

        return TryParseCatalogSortOrder(text);
    }

    public static bool IsMaskedUnownedSelection(AddonGSInfoCardList* addon, int displayCardId = -1)
    {
        if (displayCardId < 0)
        {
            displayCardId = TryParseCardIdFromDisplayLabel(addon);
        }

        var name = addon->SelectedCardName != null
            ? GUINodeUtils.GetNodeText(&addon->SelectedCardName->AtkResNode)?.Trim()
            : null;

        if (IsMaskedCardName(name))
        {
            return true;
        }

        if (displayCardId > 0)
        {
            var fromIcon = TriadCardDB.Get().TryGetCardIdFromIconId(addon->CardIconId);
            if (fromIcon >= 0 && fromIcon != displayCardId)
            {
                return true;
            }

            var fromStats = TryReadCardIdFromStats(addon);
            if (fromStats >= 0 && fromStats != displayCardId)
            {
                return true;
            }
        }

        if (TriadCardDB.Get().TryGetCardIdFromIconId(addon->CardIconId) < 0 &&
            (string.IsNullOrWhiteSpace(name) || IsMaskedCardName(name)))
        {
            return true;
        }

        return false;
    }

    private static int TryReadCardIdFromDetailPane(AddonGSInfoCardList* addon, int displayCardId)
    {
        if (IsMaskedUnownedSelection(addon, displayCardId))
        {
            if (displayCardId > 0)
            {
                return displayCardId;
            }

            return -1;
        }

        if (displayCardId > 0)
        {
            var fromIcon = TriadCardDB.Get().TryGetCardIdFromIconId(addon->CardIconId);
            if (fromIcon < 0 || fromIcon == displayCardId)
            {
                return displayCardId;
            }
        }

        var iconCardId = TriadCardDB.Get().TryGetCardIdFromIconId(addon->CardIconId);
        if (iconCardId >= 0)
        {
            return iconCardId;
        }

        if (addon->SelectedCardName != null)
        {
            var name = GUINodeUtils.GetNodeText(&addon->SelectedCardName->AtkResNode)?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !IsMaskedCardName(name))
            {
                var cardFromName = TriadCardDB.Get().Find(name);
                if (cardFromName != null)
                {
                    return cardFromName.Id;
                }
            }
        }

        var fromStats = TryReadCardIdFromStats(addon);
        if (fromStats >= 0)
        {
            return fromStats;
        }

        var fromNumber = TryParseCardIdFromText(
            addon->SelectedCardNumber != null ? &addon->SelectedCardNumber->AtkResNode : null);
        if (fromNumber > 0)
        {
            return fromNumber;
        }

        return -1;
    }

    private static bool ShouldPreferGridOverDetail(
        int gridCardId,
        int detailCardId,
        AddonGSInfoCardList* addon,
        int displayCardId)
    {
        if (displayCardId > 0 && displayCardId == gridCardId && displayCardId != detailCardId)
        {
            return true;
        }

        if (IsMaskedUnownedSelection(addon, displayCardId))
        {
            return true;
        }

        return IsCardUnowned(gridCardId);
    }

    private static bool IsCardUnowned(int cardId)
    {
        if (cardId <= 0)
        {
            return false;
        }

        if (TriadMemoryReads.IsAvailable)
        {
            return !TriadMemoryReads.TryIsCardOwned(cardId);
        }

        return !GameCardDB.Get().ownedCardIds.Contains(cardId);
    }

    private static int TryParseCatalogSortOrder(string text)
    {
        var dashIdx = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx >= 0)
        {
            var afterDash = ParseDisplayNumber(text[(dashIdx + 3)..]);
            if (TryMapCatalogNumber(afterDash, out var fromDash))
            {
                return fromDash;
            }
        }

        foreach (var prefix in CatalogNumberPrefixes)
        {
            var idx = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var afterPrefix = ParseDisplayNumber(text[(idx + prefix.Length)..]);
            if (TryMapCatalogNumber(afterPrefix, out var fromPrefix))
            {
                return fromPrefix;
            }
        }

        var lastNumber = ExtractLastPositiveInteger(text);
        return TryMapCatalogNumber(lastNumber, out var fromLast) ? fromLast : -1;
    }

    private static bool TryMapCatalogNumber(int catalogNo, out int cardId)
    {
        cardId = -1;
        if (catalogNo <= 0)
        {
            return false;
        }

        var card = TriadCardDB.Get().FindBySortOrder(catalogNo);
        if (card != null)
        {
            cardId = card.Id;
            return true;
        }

        card = TriadCardDB.Get().FindBySortOrder(1000 + catalogNo);
        if (card != null)
        {
            cardId = card.Id;
            return true;
        }

        return false;
    }

    private static int ExtractLastPositiveInteger(string text)
    {
        var last = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i]))
            {
                continue;
            }

            var end = i;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            if (int.TryParse(text[i..end], out var number) && number > 0)
            {
                last = number;
            }

            i = end;
        }

        return last;
    }

    private static bool IsMaskedCardName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (name.Contains("???", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var ch in name)
        {
            if (ch is '?' or '？')
            {
                return true;
            }
        }

        return false;
    }

    private static int ParseDisplayNumber(string text)
    {
        text = text.TrimStart(' ', '.', ':', '°', 'º', '№', '#');
        var end = 0;
        while (end < text.Length && char.IsDigit(text[end]))
        {
            end++;
        }

        if (end == 0)
        {
            return -1;
        }

        return int.TryParse(text[..end], out var number) && number > 0 ? number : -1;
    }

    private static int TryReadCardIdFromGrid(
        AddonGSInfoCardList* addon,
        byte filterMode,
        AgentGoldSaucer* agent,
        int displayCardId = -1)
    {
        var candidates = new List<int>();

        void AddCandidate(int page, int cell)
        {
            if (page < 0 || cell < 0)
            {
                return;
            }

            var cardInfo = GameCardDB.Get().FindByGridLocationAnyFilter(page, cell, filterMode);
            if (cardInfo == null)
            {
                return;
            }

            if (!candidates.Contains(cardInfo.CardId))
            {
                candidates.Add(cardInfo.CardId);
            }
        }

        AddCandidate(addon->SelectedPage, addon->SelectedCardIndex);

        if (agent != null)
        {
            AddCandidate(agent->EditDeckSelectedPage, agent->EditDeckSelectedCardIndex);
        }

        if (displayCardId > 0)
        {
            foreach (var candidate in candidates)
            {
                if (candidate == displayCardId)
                {
                    return candidate;
                }
            }
        }

        return candidates.Count > 0 ? candidates[0] : -1;
    }

    private static int TryReadCardIdFromStats(AddonGSInfoCardList* addon)
    {
        if (addon->NumSideU == 0 &&
            addon->NumSideL == 0 &&
            addon->NumSideD == 0 &&
            addon->NumSideR == 0)
        {
            return -1;
        }

        var card = TriadCardDB.Get().Find(
            addon->NumSideU,
            addon->NumSideL,
            addon->NumSideD,
            addon->NumSideR,
            (ETriadCardType)addon->CardType,
            (ETriadCardRarity)addon->CardRarity);
        return card?.Id ?? -1;
    }

    private static int TryParseCardIdFromDescription(AddonGSInfoCardList* addon)
    {
        var descNode = addon->SelectedCardDescription;
        if (descNode == null)
        {
            return -1;
        }

        foreach (var node in GUINodeUtils.GetAllChildNodes(&descNode->AtkResNode) ?? [])
        {
            if (node == null)
            {
                continue;
            }

            var nodeText = GUINodeUtils.GetNodeText(node);
            var fromDisplay = TryParseCardIdFromDisplayLabel(nodeText);
            if (fromDisplay > 0)
            {
                return fromDisplay;
            }

            var fromNumber = TryParseCardIdFromText(nodeText);
            if (fromNumber > 0)
            {
                return fromNumber;
            }
        }

        return -1;
    }

    private static int TryParseCardIdFromText(AtkResNode* node) =>
        TryParseCardIdFromText(GUINodeUtils.GetNodeText(node));

    private static int TryParseCardIdFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        if (text.Contains("Ex.", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("No.", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var fromCatalog = TryParseCatalogSortOrder(text);
        if (fromCatalog > 0)
        {
            return fromCatalog;
        }

        foreach (var part in text.Split([' ', '.', '#', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out var cardId) && cardId > 0 && IsKnownCardId(cardId))
            {
                return cardId;
            }
        }

        return -1;
    }

    private static bool IsKnownCardId(int cardId) =>
        TriadCardDB.Get().FindById(cardId) != null || GameCardDB.Get().FindById(cardId) != null;
}
