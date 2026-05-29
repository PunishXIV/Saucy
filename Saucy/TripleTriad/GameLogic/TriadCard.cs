using System;
namespace FFTriadBuddy;

public enum ETriadCardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public enum ETriadCardType
{
    None,
    Beastman,
    Primal,
    Scion,
    Garlean
}

public enum ETriadCardOwner
{
    Unknown,
    Blue,
    Red
}

public enum ETriadGameSide
{
    Up,
    Left,
    Down,
    Right
}

public class TriadCard : IEquatable<TriadCard>
{
    public int Group;
    public int Id;
    public string Name = string.Empty;
    public float OptimizerScore;
    public ETriadCardRarity Rarity;
    public int SameNumberId;
    public int[] Sides;
    public int SortOrder;
    public ETriadCardType Type;

    public TriadCard()
    {
        Id = -1;
        Sides = [0, 0, 0, 0];
        SameNumberId = -1;
        SortOrder = 0;
        Group = 0;
        OptimizerScore = 0.0f;
    }

    public TriadCard(int id, ETriadCardRarity rarity, ETriadCardType type, int numUp, int numDown, int numLeft, int numRight, int sortOrder, int group)
    {
        Id = id;
        Rarity = rarity;
        Type = type;
        Sides = [numUp, numLeft, numDown, numRight];
        SameNumberId = -1;
        SortOrder = sortOrder;
        Group = group;

        if (group != 0 && SortOrder < 1000)
        {
            SortOrder += 1000;
        }

        OptimizerScore = TriadDeckOptimizer.GetCardScore(this);
    }

    public int SmallIconId => 88000 + Id;
    public int BigIconId => 87000 + Id;

    public bool Equals(TriadCard other) => (other != null) && (Id == other.Id);

    public override bool Equals(object obj) => Equals(obj as TriadCard);

    public override int GetHashCode() => 2108858624 + Id.GetHashCode();

    public bool IsValid() =>
        (Id >= 0) &&
        (Sides[0] >= 1) && (Sides[0] <= 10) &&
        (Sides[1] >= 1) && (Sides[1] <= 10) &&
        (Sides[2] >= 1) && (Sides[2] <= 10) &&
        (Sides[3] >= 1) && (Sides[3] <= 10);

    public string ToShortCodeString() => "[" + Id + ":" + Name + "]";

    public string ToShortLocalizedString() => "[" + Id + ":" + Name + "]";

    public string ToLocalizedString() =>
        string.Format("[{0}] {1} {2} [{3}, {4}, {5}, {6}]",
            Id, Name,
            new string('*', (int)Rarity + 1),
            Sides[0], Sides[1], Sides[2], Sides[3],
            (Type != ETriadCardType.None) ? " [" + GameDataText.GetCardTypeName(Type) + "]" : "");

    public override string ToString() =>
        string.Format("[{0}] {1} {2} [{3}, {4}, {5}, {6}]",
            Id, Name,
            new string('*', (int)Rarity + 1),
            Sides[0], Sides[1], Sides[2], Sides[3],
            (Type != ETriadCardType.None) ? " [" + Type + "]" : "");
}

public class TriadCardInstance
{
    public readonly TriadCard card;
    public ETriadCardOwner owner;
    public int scoreModifier;

    public TriadCardInstance(TriadCard card, ETriadCardOwner owner)
    {
        this.card = card;
        this.owner = owner;
        scoreModifier = 0;
    }

    public TriadCardInstance(TriadCardInstance copyFrom)
    {
        card = copyFrom.card;
        owner = copyFrom.owner;
        scoreModifier = copyFrom.scoreModifier;
    }

    public override string ToString() =>
        owner + " " + card +
        ((scoreModifier > 0) ? (" +" + scoreModifier) :
            (scoreModifier < 0) ? (" -" + scoreModifier) :
            "");

    public int GetRawNumber(ETriadGameSide side) => card.Sides[(int)side];

    public int GetNumber(ETriadGameSide side) => Math.Min(Math.Max(GetRawNumber(side) + scoreModifier, 1), 10);

    public int GetOppositeNumber(ETriadGameSide side) => GetNumber((ETriadGameSide)(((int)side + 2) % 4));
}
