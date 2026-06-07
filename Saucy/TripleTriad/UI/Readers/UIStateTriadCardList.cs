using System.Numerics;
namespace Saucy.TripleTriad.UI;

public class UIStateTriadCardList
{
    public byte cardIndex;
    public Vector2 descriptionPos;
    public Vector2 descriptionSize;
    public byte filterMode;
    public int iconId;
    public bool isDeckEditMode;
    public byte numD;
    public byte numL;
    public byte numR;
    public byte numU;
    public byte pageIndex;
    public byte rarity;
    public Vector2 screenPos;
    public Vector2 screenSize;
    public int selectedCardId = -1;
    public bool selectionMasked;
    public byte type;

    public int ResolveCardId(GameUIParser ctx)
    {
        if (IsMaskedSelection())
        {
            if (selectedCardId > 0)
            {
                return selectedCardId;
            }

            return ToTriadCardFromGrid(ctx)?.Id ?? -1;
        }

        var fromGrid = ToTriadCardFromGrid(ctx);
        if (fromGrid != null)
        {
            return fromGrid.Id;
        }

        if (selectedCardId > 0)
        {
            return selectedCardId;
        }

        var fromIcon = TriadCardDB.Get().TryGetCardIdFromIconId(iconId);
        if (fromIcon >= 0)
        {
            return fromIcon;
        }

        return ToTriadCard(ctx)?.Id ?? -1;
    }

    public bool IsMaskedSelection() => selectionMasked;

    public GameCardCollectionFilter GetActiveCollectionFilter() =>
        isDeckEditMode
            ? GameCardCollectionFilter.DeckEditDefault
            : filterMode switch
            {
                1 => GameCardCollectionFilter.OnlyOwned,
                2 => GameCardCollectionFilter.OnlyMissing,
                var _ => GameCardCollectionFilter.All
            };

    public TriadCard? ToTriadCardFromGrid(GameUIParser ctx) =>
        ctx.ParseCardByGridLocation(pageIndex, cardIndex, (int)GetActiveCollectionFilter(), false);

    public TriadCard? ToTriadCard(GameUIParser ctx)
    {
        if (IsMaskedSelection())
        {
            return ToTriadCardFromGrid(ctx);
        }

        if (selectedCardId > 0)
        {
            var card = ctx.cards.FindById(selectedCardId);
            if (card != null)
            {
                var gridCard = ToTriadCardFromGrid(ctx);
                if (gridCard != null && gridCard.Id != card.Id)
                {
                    return gridCard;
                }

                return card;
            }
        }

        TriadCard? iconMatch = null;
        var iconCardId = TriadCardDB.Get().TryGetCardIdFromIconId(iconId);
        if (iconCardId >= 0)
        {
            iconMatch = ctx.cards.FindById(iconCardId);
            if (iconMatch != null)
            {
                return iconMatch;
            }
        }

        var gridMatch = ctx.ParseCardByGridLocation(pageIndex, cardIndex, filterMode, false);

        if (gridMatch != null)
        {
            if (iconMatch != null && iconMatch.Id != gridMatch.Id)
            {
                return gridMatch;
            }

            var statsMatch = ctx.ParseCard(numU, numL, numD, numR, (ETriadCardType)type, (ETriadCardRarity)rarity, false);
            if (statsMatch != null && statsMatch.SameNumberId < 0 && statsMatch.Id != gridMatch.Id)
            {
                return gridMatch;
            }

            return gridMatch;
        }

        if (iconMatch != null)
        {
            return iconMatch;
        }

        if (iconId == 0 && numU == 0 && numL == 0 && numD == 0 && numR == 0)
        {
            return null;
        }

        var matchOb = ctx.ParseCard(numU, numL, numD, numR, (ETriadCardType)type, (ETriadCardRarity)rarity, false);
        if (matchOb != null && matchOb.SameNumberId < 0)
        {
            return matchOb;
        }

        return matchOb;
    }
}
