using System.Collections.Generic;

namespace Saucy.TripleTriad.GameLogic;

public class PlayerSettingsDB
{
    private static readonly PlayerSettingsDB instance = new();
    public List<TriadCard> ownedCards = [];
    public TriadCard?[] starterCards;

    public PlayerSettingsDB()
    {
        var cardDB = TriadCardDB.Get();
        starterCards = new TriadCard?[5];

        starterCards[0] = cardDB.FindById(1); // Dodo
        starterCards[1] = cardDB.FindById(3); // Sabotender
        starterCards[2] = cardDB.FindById(6); // Bomb
        starterCards[3] = cardDB.FindById(7); // Mandragora
        starterCards[4] = cardDB.FindById(10); // Coeurl
    }

    public static PlayerSettingsDB Get() => instance;
}

public class ScannerTriad
{
    public enum ETurnState
    {
        MissingTimer,
        Waiting,
        Active
    }

    public class GameState
    {
        public TriadCard?[] blueDeck = new TriadCard[5];
        public TriadCard?[] board = new TriadCard[9];
        public ETriadCardOwner[] boardOwner = new ETriadCardOwner[9];
        public TriadCard? forcedBlueCard = null;
        public List<TriadGameModifier> mods = [];
        public TriadCard?[] redDeck = new TriadCard[5];
        public ETurnState turnState = ETurnState.MissingTimer;
    }
}
