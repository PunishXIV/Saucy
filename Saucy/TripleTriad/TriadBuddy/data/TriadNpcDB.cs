using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FFTriadBuddy
{
    public class TriadNpc
    {
        public int Id;
        public LocString Name;
        public List<TriadGameModifier> Rules;
        public TriadDeck Deck;

        public Regex NameRegex;
        public Regex NamePartialRegex;

        // sometimes (German client only?) npc names from game data will be using tags for handling inflections
        // those will show up as:
        //   [marker]
        // in the middle of string and require special handling
        public bool hasLocMarkup;

        public TriadNpc(int id, List<TriadGameModifier> rules, int[] cardsAlways, int[] cardsPool)
        {
            Id = id;
            Name = LocalizationDB.Get().FindOrAddLocString(ELocStringType.NpcName, id);
            Rules = rules;
            Deck = new TriadDeck(cardsAlways, cardsPool);
            hasLocMarkup = false;
        }

        public TriadNpc(int id, List<TriadGameModifier> rules, List<TriadCard> rewards, TriadDeck deck)
        {
            Id = id;
            Name = LocalizationDB.Get().FindOrAddLocString(ELocStringType.NpcName, id);
            Rules = rules;
            Deck = deck;
            hasLocMarkup = false;
        }

        public void OnNameUpdated()
        {
            hasLocMarkup = Name.GetCodeName().Contains('[');
            if (hasLocMarkup)
            {
                string namePattern = Regex.Replace(Name.GetCodeName().ToLower(), "\\[[a-z]\\]", ".*");
                NameRegex = new Regex(namePattern);

                // not really a partial regex match, but good enough for GameUIParser.ParseNpcNameStart
                int maxMatchLen = 15;
                string partialPattern = (namePattern.Length < maxMatchLen) ? namePattern : namePattern.Substring(0, maxMatchLen).TrimEnd('*').TrimEnd('.');
                NamePartialRegex = new Regex(partialPattern);
            }
        }

        public override string ToString()
        {
            return Name.GetCodeName();
        }

        public bool IsMatchingName(string testName)
        {
            if (NameRegex != null)
            {
                return NameRegex.IsMatch(testName);
            }

            return Name.GetCodeName().Equals(testName, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsMatchingNameStart(string testName)
        {
            if (NamePartialRegex != null)
            {
                return NamePartialRegex.IsMatch(testName);
            }

            return Name.GetCodeName().StartsWith(testName, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class TriadNpcDB
    {
        private static TriadNpcDB instance = new TriadNpcDB();
        public List<TriadNpc> npcs = new List<TriadNpc>();

        public static TriadNpcDB Get()
        {
            return instance;
        }

        public TriadNpc Find(string Name)
        {
            string nameLower = Name.ToLower();
            return npcs.Find(x => (x != null) && x.IsMatchingName(nameLower));
        }

        public TriadNpc FindByNameStart(string Name)
        {
            string nameLower = Name.ToLower();
            return npcs.Find(x => (x != null) && x.IsMatchingNameStart(nameLower));
        }
    }
}
