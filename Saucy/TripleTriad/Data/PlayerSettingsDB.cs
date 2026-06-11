#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.Data;

public class PlayerSettingsDB
{
    private static readonly PlayerSettingsDB instance = new();
    public List<TriadCard> ownedCards = [];
    public TriadCard[] starterCards;

    public PlayerSettingsDB()
    {
        var cardDB = TriadCardDB.Get();
        starterCards = new TriadCard[5];

        starterCards[0] = cardDB.FindById(1);
        starterCards[1] = cardDB.FindById(3);
        starterCards[2] = cardDB.FindById(6);
        starterCards[3] = cardDB.FindById(7);
        starterCards[4] = cardDB.FindById(10);
    }

    public static PlayerSettingsDB Get() => instance;
}
