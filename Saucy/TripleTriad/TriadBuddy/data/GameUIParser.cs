using FFTriadBuddy;
using System;

namespace TriadBuddyPlugin;

public class GameUIParser
{
    public const int PartialNpcNameLength = 20;

    public TriadCardDB cards = TriadCardDB.Get();
    public TriadNpcDB npcs = TriadNpcDB.Get();
    public TriadGameModifierDB mods = TriadGameModifierDB.Get();

    public bool hasFailedCard = false;
    public bool hasFailedModifier = false;
    public bool hasFailedNpc = false;
    public bool HasErrors => hasFailedCard || hasFailedModifier || hasFailedNpc;

    private static string? lastLoggedNpc;

    public void Reset()
    {
        hasFailedCard = false;
        hasFailedModifier = false;
        hasFailedNpc = false;
    }

    public TriadCard? ParseCard(int numU, int numL, int numD, int numR, string texPath, bool markFailed = true)
    {
        // there's hardly any point in doing side comparison since plugin can access card id directly, but i still like it :<
        var matchOb = cards.Find(numU, numL, numD, numR);
        if (matchOb != null)
        {
            if (matchOb.SameNumberId >= 0)
            {
                // ambiguous match, use texture for exact Id
                matchOb = ParseCard(texPath, false);
            }
        }

        if (matchOb == null && markFailed)
        {
            OnFailedCard($"[{numU}-{numL}-{numD}-{numR}], tex:{texPath}");
        }

        return matchOb;
    }

    public TriadCard? ParseCard(int numU, int numL, int numD, int numR, ETriadCardType type, ETriadCardRarity rarity, bool markFailed = true)
    {
        var matchOb = cards.Find(numU, numL, numD, numR, type, rarity);
        if (matchOb == null && markFailed)
        {
            OnFailedCard($"[{numU}-{numL}-{numD}-{numR}], type:{type}, rarity:{rarity}");
            return null;
        }

        return matchOb;
    }

    public TriadCard? ParseCard(string texPath, bool markFailed = true)
    {
        var matchOb = cards.FindByTexture(texPath);
        if (matchOb == null && markFailed)
        {
            OnFailedCard(texPath);
        }

        return matchOb;
    }

    public TriadCard? ParseCardByGridLocation(int pageIdx, int cellIdx, int filterMode, bool markFailed = true)
    {
        var matchInfoOb = GameCardDB.Get().FindByGridLocation(pageIdx, cellIdx, filterMode);
        if (matchInfoOb != null)
        {
            var matchOb = cards.FindById(matchInfoOb.CardId);
            if (matchOb != null)
            {
                return matchOb;
            }

            if (markFailed)
            {
                OnFailedCard($"page:{pageIdx}, cell:{cellIdx}, mode:{filterMode}, id:{matchInfoOb.CardId}");
            }
        }
        else
        {
            if (markFailed)
            {
                OnFailedCard($"page:{pageIdx}, cell:{cellIdx}, mode:{filterMode}");
            }
        }

        return null;
    }

    public void OnFailedCard(string desc)
    {
        Svc.Log.Error($"failed to match card: {desc}");
        hasFailedCard = true;
    }

    public TriadGameModifier? ParseModifier(string desc, bool markFailed = true)
    {
        var matchOb = mods.mods.Find(x => x.GetLocalizedName().Equals(desc, StringComparison.OrdinalIgnoreCase));
        if (matchOb == null && markFailed)
        {
            OnFailedModifier(desc);
        }

        return matchOb;
    }

    public void OnFailedModifier(string desc)
    {
        Svc.Log.Error($"failed to match rule: {desc}");
        hasFailedModifier = true;
    }

    public TriadNpc? ParseNpcNameStart(string desc, bool markFailed = true)
    {
        // some names will be truncated in UI, e.g. 'Guhtwint of the Three...'
        // limit match to first 20 characters and hope that SE will keep it unique
        var matchPattern = (desc.Length > PartialNpcNameLength) ? desc[..PartialNpcNameLength] : desc;

        var matchOb = npcs.FindByNameStart(matchPattern);
        if (matchOb == null && markFailed)
        {
            OnFailedNpc(desc);
        }

        return matchOb;
    }

    public TriadNpc? ParseNpc(string desc, bool markFailed = true)
    {
        var matchOb = npcs.Find(desc);
        if (matchOb == null && markFailed)
        {
            OnFailedNpc(desc);
        }

        return matchOb;
    }

    public void OnFailedNpc(string desc)
    {
        Svc.Log.Error($"failed to match npc: {string.Join(", ", desc)}");
        hasFailedNpc = true;
    }

    public void OnFailedNpcSilent(string desc)
    {
        // limit number of log spam, use normal verbocity
        if (lastLoggedNpc != desc)
        {
            lastLoggedNpc = desc;
            Svc.Log.Info($"failed to match npc: {string.Join(", ", desc)}, is it pvp?");
        }

        // always mark as failure though
        hasFailedNpc = true;
    }
}
