using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

public class UIStateTriadCard : IEquatable<UIStateTriadCard>
{
    public bool isLocked;
    public bool isPresent;
    public byte numD;
    public byte numL;
    public byte numR;
    public byte numU;
    public byte owner;
    public byte rarity;
    public string? texturePath;
    public byte type;

    public bool IsHidden => isPresent && (numU == 0);

    public bool Equals(UIStateTriadCard? other) =>
        (other != null) &&
        (isPresent == other.isPresent) &&
        (isLocked == other.isLocked) &&
        (owner == other.owner) &&
        (texturePath == other.texturePath);

    public override string ToString()
    {
        if (!isPresent)
        {
            return "(empty)";
        }

        var desc = $"[{numU:X}-{numL:X}-{numD:X}-{numR:X}], tex:{texturePath}, owner:{owner}";
        if (isLocked)
        {
            desc += " (locked)";
        }

        return desc;
    }

    public TriadCard? ToTriadCard(GameUIParser ctx, bool markFailed = false)
    {
        if (!isPresent)
        {
            return null;
        }

        if (IsHidden)
        {
            return ctx.cards.hiddenCard;
        }

        var cardType = GameDataLoader.ConvertToTriadType(type);
        var cardRarity = GameDataLoader.ConvertToTriadRarity(rarity);

        return ctx.ParseCard(numU, numL, numD, numR, cardType, cardRarity, markFailed) ??
               ctx.ParseCard(numU, numL, numD, numR, texturePath ?? "", markFailed);
    }
}

public class UIStateTriadGame : IEquatable<UIStateTriadGame>
{
    public UIStateTriadCard[] blueDeck = new UIStateTriadCard[5];
    public UIStateTriadCard[] board = new UIStateTriadCard[9];
    public bool isPlayerTurn;
    public bool isPvP;
    public byte move;
    public UIStateTriadCard[] redDeck = new UIStateTriadCard[5];
    public List<string> redPlayerDesc = [];
    public List<string> rules = [];
    public bool turnBannerVisible;

    public bool Equals(UIStateTriadGame? other)
    {
        if (other == null)
        {
            return false;
        }

        if (move != other.move || isPlayerTurn != other.isPlayerTurn || turnBannerVisible != other.turnBannerVisible)
        {
            return false;
        }

        if (rules.Count != other.rules.Count || !rules.TrueForAll(other.rules.Contains))
        {
            return false;
        }

        if (redPlayerDesc.Count != other.redPlayerDesc.Count || !redPlayerDesc.TrueForAll(other.redPlayerDesc.Contains))
        {
            return false;
        }

        static bool HasCardDiffs(UIStateTriadCard a, UIStateTriadCard b)
        {
            if ((a == null) != (b == null))
            {
                return true;
            }

            return (a != null && b != null) && !a.Equals(b);
        }

        for (var idx = 0; idx < board.Length; idx++)
        {
            if (HasCardDiffs(board[idx], other.board[idx]))
            {
                return false;
            }
        }

        for (var idx = 0; idx < blueDeck.Length; idx++)
        {
            if (HasCardDiffs(blueDeck[idx], other.blueDeck[idx]) || HasCardDiffs(redDeck[idx], other.redDeck[idx]))
            {
                return false;
            }
        }

        return true;
    }

    public TriadNpc? ToTriadNpc(GameUIParser ctx)
    {
        TriadNpc? resultOb = null;
        var canLogError = false;

        foreach (var name in redPlayerDesc)
        {
            var matchOb = ctx.ParseNpc(name, false) ?? ctx.ParseNpcNameStart(name, false);
            if (matchOb != null)
            {
                if (resultOb == null || resultOb == matchOb)
                {
                    resultOb = matchOb;
                }
                else
                {
                    resultOb = null;
                    canLogError = true;
                    break;
                }
            }
        }

        if (redPlayerDesc.Count > 0 && resultOb == null)
        {
            var npcDesc = string.Join(',', redPlayerDesc);
            if (canLogError)
            {
                ctx.OnFailedNpc(npcDesc);
            }
            else
            {
                ctx.OnFailedNpcSilent(npcDesc);
            }
        }

        return resultOb;
    }

    public List<TriadGameModifier> ToTriadModifier(GameUIParser ctx, bool markFailed = false)
    {
        var list = new List<TriadGameModifier>();
        foreach (var rule in rules)
        {
            var matchOb = ctx.ParseModifier(rule, markFailed);
            if (matchOb != null)
            {
                list.Add(matchOb);
            }
        }

        return list;
    }

    public TriadBoardScanner.GameState ToTriadScreenState(GameUIParser ctx, bool markFailed = false)
    {
        var screenOb = new TriadBoardScanner.GameState
        {
            mods = ToTriadModifier(ctx, markFailed), turnState = ResolveTurnState()
        };

        for (var idx = 0; idx < board.Length; idx++)
        {
            screenOb.board[idx] = board[idx].ToTriadCard(ctx, markFailed);
            screenOb.boardOwner[idx] =
                (board[idx].owner == 1) ? ETriadCardOwner.Blue :
                (board[idx].owner == 2) ? ETriadCardOwner.Red :
                ETriadCardOwner.Unknown;
        }

        if (move == 2)
        {
            for (var idx = 0; idx < blueDeck.Length; idx++)
            {
                if (blueDeck[idx].isPresent && !blueDeck[idx].isLocked)
                {
                    screenOb.forcedBlueCard = blueDeck[idx].ToTriadCard(ctx, markFailed);
                }
            }
        }

        for (var idx = 0; idx < blueDeck.Length; idx++)
        {
            screenOb.blueDeck[idx] = blueDeck[idx].ToTriadCard(ctx, markFailed);
            screenOb.redDeck[idx] = redDeck[idx].ToTriadCard(ctx, markFailed);
        }

        return screenOb;
    }

    private TriadBoardScanner.ETurnState ResolveTurnState()
    {
        if (!TriadTurnState.CanBlueAct(move, isPlayerTurn))
        {
            return TriadBoardScanner.ETurnState.Waiting;
        }

        if (TriadTurnState.IsBoardPickPhase(move) && turnBannerVisible)
        {
            return TriadBoardScanner.ETurnState.Waiting;
        }

        return TriadBoardScanner.ETurnState.Active;
    }
}
