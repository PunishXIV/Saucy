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

    public TriadCard? ToTriadCard(GameUIParser ctx) =>
        !isPresent ? null :
        IsHidden ? ctx.cards.hiddenCard :
        ctx.ParseCard(numU, numL, numD, numR, texturePath ?? "");
}

public class UIStateTriadGame : IEquatable<UIStateTriadGame>
{
    public UIStateTriadCard[] blueDeck = new UIStateTriadCard[5];
    public UIStateTriadCard[] board = new UIStateTriadCard[9];
    public bool isPvP;
    public byte move;
    public UIStateTriadCard[] redDeck = new UIStateTriadCard[5];
    public List<string> redPlayerDesc = [];
    public List<string> rules = [];

    public bool Equals(UIStateTriadGame? other)
    {
        if (other == null)
        {
            return false;
        }

        if (move != other.move)
        {
            return false;
        }

        // not real list comparison, but will be enough here
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
            var matchOb = ctx.ParseNpcNameStart(name, false);
            if (matchOb != null)
            {
                if (resultOb == null || resultOb == matchOb)
                {
                    resultOb = matchOb;
                }
                else
                {
                    // um.. names matched two different npc, fail 
                    resultOb = null;
                    canLogError = true;
                    break;
                }
            }
        }

        if (redPlayerDesc.Count > 0 && resultOb == null)
        {
            // don't spam errors on npc match, this will happened when someone tries playing pvp match too
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

    public List<TriadGameModifier> ToTriadModifier(GameUIParser ctx)
    {
        var list = new List<TriadGameModifier>();
        foreach (var rule in rules)
        {
            var matchOb = ctx.ParseModifier(rule);
            if (matchOb != null)
            {
                list.Add(matchOb);
            }
        }

        return list;
    }

    public ScannerTriad.GameState ToTriadScreenState(GameUIParser ctx)
    {
        var screenOb = new ScannerTriad.GameState
        {
            mods = ToTriadModifier(ctx), turnState = (move == 0) ? ScannerTriad.ETurnState.Waiting : ScannerTriad.ETurnState.Active
        };

        for (var idx = 0; idx < board.Length; idx++)
        {
            screenOb.board[idx] = board[idx].ToTriadCard(ctx);
            screenOb.boardOwner[idx] =
                (board[idx].owner == 1) ? ETriadCardOwner.Blue :
                (board[idx].owner == 2) ? ETriadCardOwner.Red :
                ETriadCardOwner.Unknown;
        }

        var hasForcedMove = (move == 2);
        for (var idx = 0; idx < blueDeck.Length; idx++)
        {
            screenOb.blueDeck[idx] = blueDeck[idx].ToTriadCard(ctx);
            screenOb.redDeck[idx] = redDeck[idx].ToTriadCard(ctx);

            if (hasForcedMove && blueDeck[idx].isPresent && !blueDeck[idx].isLocked)
            {
                screenOb.forcedBlueCard = screenOb.blueDeck[idx];
            }
        }

        return screenOb;
    }
}
