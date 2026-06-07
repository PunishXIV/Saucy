#nullable disable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace Saucy.TripleTriad.Data;

public class TriadNpc
{
    public TriadDeck Deck;

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

    private TriadNpc[] SnapshotNpcs() => npcs.Count == 0 ? [] : [.. npcs];

    public TriadNpc Find(string Name)
    {
        var nameLower = Name.ToLower();
        foreach (var x in SnapshotNpcs())
        {
            if (x != null && x.IsMatchingName(nameLower))
            {
                return x;
            }
        }

        return null;
    }

    public TriadNpc FindByNameStart(string Name)
    {
        var nameLower = Name.ToLower();
        foreach (var x in SnapshotNpcs())
        {
            if (x != null && x.IsMatchingNameStart(nameLower))
            {
                return x;
            }
        }

        return null;
    }

    public TriadNpc FindByID(int id)
    {
        foreach (var x in SnapshotNpcs())
        {
            if (x != null && x.Id == id)
            {
                return x;
            }
        }

        return null;
    }

    public TriadNpc FindMatchingName(string testName)
    {
        if (string.IsNullOrWhiteSpace(testName))
        {
            return null;
        }

        foreach (var npc in SnapshotNpcs())
        {
            if (npc != null && npc.IsMatchingName(testName))
            {
                return npc;
            }
        }

        return null;
    }

    public TriadNpc GetByIndex(int index)
    {
        var snapshot = SnapshotNpcs();
        return index >= 0 && index < snapshot.Length ? snapshot[index] : null;
    }
}
