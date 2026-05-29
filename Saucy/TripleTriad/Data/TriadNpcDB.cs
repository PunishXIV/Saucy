#nullable disable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace Saucy.TripleTriad.Data;

public class TriadNpc
{
    public TriadDeck Deck;

    // sometimes (German client only?) npc names from game data will be using tags for handling inflections
    // those will show up as:
    //   [marker]
    // in the middle of string and require special handling
    public bool hasLocMarkup;
    public int Id;
    public string Name = string.Empty;
    public Regex NamePartialRegex;

    public Regex NameRegex;
    public List<TriadGameModifier> Rules;

    public TriadNpc(int id, List<TriadGameModifier> rules, int[] cardsAlways, int[] cardsPool)
    {
        Id = id;
        Rules = rules;
        Deck = new(cardsAlways, cardsPool);
        hasLocMarkup = false;
    }

    public TriadNpc(int id, List<TriadGameModifier> rules, List<TriadCard> rewards, TriadDeck deck)
    {
        Id = id;
        Rules = rules;
        Deck = deck;
        hasLocMarkup = false;
    }

    public void OnNameUpdated()
    {
        hasLocMarkup = Name.Contains('[');
        if (hasLocMarkup)
        {
            var namePattern = Regex.Replace(Name.ToLower(), "\\[[a-z]\\]", ".*");
            NameRegex = new(namePattern);

            // not really a partial regex match, but good enough for GameUIParser.ParseNpcNameStart
            var maxMatchLen = 15;
            var partialPattern = (namePattern.Length < maxMatchLen) ? namePattern : namePattern[..maxMatchLen].TrimEnd('*').TrimEnd('.');
            NamePartialRegex = new(partialPattern);
        }
    }

    public override string ToString() => Name;

    public bool IsMatchingName(string testName)
    {
        if (NameRegex != null)
        {
            return NameRegex.IsMatch(testName);
        }

        return Name.Equals(testName, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsMatchingNameStart(string testName)
    {
        if (string.IsNullOrWhiteSpace(testName))
        {
            return false;
        }

        testName = testName.Trim();
        if (NamePartialRegex != null)
        {
            return NamePartialRegex.IsMatch(testName);
        }

        return Name.StartsWith(testName, StringComparison.OrdinalIgnoreCase) ||
               testName.StartsWith(Name, StringComparison.OrdinalIgnoreCase);
    }
}

public class TriadNpcDB
{
    private static readonly TriadNpcDB instance = new();
    public List<TriadNpc> npcs = [];

    public static TriadNpcDB Get() => instance;

    public TriadNpc Find(string Name)
    {
        var nameLower = Name.ToLower();
        return npcs.Find(x => (x != null) && x.IsMatchingName(nameLower));
    }

    public TriadNpc FindByNameStart(string Name)
    {
        var nameLower = Name.ToLower();
        return npcs.Find(x => (x != null) && x.IsMatchingNameStart(nameLower));
    }

    public TriadNpc FindByID(int id) => npcs.Find(x => x.Id == id);
}
