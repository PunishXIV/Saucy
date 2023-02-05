using FFTriadBuddy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saucy
{
    public class Stats
    {
        public int GamesPlayedWithSaucy = 0;

        public int GamesWonWithSaucy = 0;

        public int GamesLostWithSaucy = 0;

        public int GamesDrawnWithSaucy = 0;

        public int CardsDroppedWithSaucy = 0;

        public int MGPWon = 0;

        public Dictionary<string, int> NPCsPlayed = new Dictionary<string, int>();

        public Dictionary<uint, int> CardsWon = new Dictionary<uint, int>();
    }
}
