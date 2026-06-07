#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadBoardScanner
{
    public enum ETurnState
    {
        Waiting,
        Active
    }

    public class GameState
    {
        public TriadCard[] blueDeck = new TriadCard[5];
        public TriadCard[] board = new TriadCard[9];
        public ETriadCardOwner[] boardOwner = new ETriadCardOwner[9];
        public TriadCard forcedBlueCard = null;
        public List<TriadGameModifier> mods = [];
        public TriadCard[] redDeck = new TriadCard[5];
        public ETurnState turnState = ETurnState.Waiting;
    }
}
