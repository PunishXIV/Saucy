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
    public uint BaseID;
    public string Name = string.Empty;
    public Regex NamePartialRegex;

    public Regex NameRegex;
    public List<TriadGameModifier> Rules;

    public TriadNpc(int id, uint dataId, List<TriadGameModifier> rules, int[] cardsAlways, int[] cardsPool)
    {
        Id = id;
        BaseID = dataId;
        Rules = rules;
        Deck = new(cardsAlways, cardsPool);
        hasLocMarkup = false;
    }

    public void OnNameUpdated()
    {
        hasLocMarkup = Name.Contains('[');
        if (hasLocMarkup)
        {
            var namePattern = Regex.Replace(NormalizeNameForMatch(Name), "\\[[a-z]+\\]", ".*");
            var regexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            NameRegex = new(namePattern, regexOptions);

            var maxMatchLen = 15;
            var partialPattern = (namePattern.Length < maxMatchLen) ? namePattern : namePattern[..maxMatchLen].TrimEnd('*').TrimEnd('.');
            NamePartialRegex = new(partialPattern, regexOptions);
        }
    }

    private static string NormalizeNameForMatch(string name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim().ToLowerInvariant();

    public override string ToString() => Name;

    public bool IsMatchingObject(Dalamud.Game.ClientState.Objects.Types.IGameObject obj) =>
        obj != null && BaseID != 0 && obj.BaseId == BaseID;

    public bool IsMatchingName(string testName)
    {
        if (string.IsNullOrWhiteSpace(testName))
        {
            return false;
        }

        testName = NormalizeNameForMatch(testName);
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

        testName = NormalizeNameForMatch(testName);
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
        foreach (var x in SnapshotNpcs())
        {
            if (x != null && x.IsMatchingName(Name))
            {
                return x;
            }
        }

        return null;
    }

    public TriadNpc FindByNameStart(string Name)
    {
        foreach (var x in SnapshotNpcs())
        {
            if (x != null && x.IsMatchingNameStart(Name))
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

    public TriadNpc FindByBaseID(uint baseId)
    {
        foreach (var x in SnapshotNpcs())
        {
            if (x != null && x.BaseID == baseId)
            {
                return x;
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
