using System;
using System.Linq;
namespace Saucy.TripleTriad.Data;

public class GameUIParser
{
    public const int PartialNpcNameLength = 20;

    private static string? lastLoggedNpc;

    public TriadCardDB cards = TriadCardDB.Get();

    public bool hasFailedCard;
    public bool hasFailedModifier;
    public bool hasFailedNpc;
    public TriadGameModifierDB mods = TriadGameModifierDB.Get();
    public TriadNpcDB npcs = TriadNpcDB.Get();
    public bool HasErrors => hasFailedCard || hasFailedModifier || hasFailedNpc;

    public void Reset()
    {
        hasFailedCard = false;
        hasFailedModifier = false;
        hasFailedNpc = false;
    }

    public TriadCard? ParseCard(int numU, int numL, int numD, int numR, string texPath, bool markFailed = true)
    {
        var matchOb = cards.Find(numU, numL, numD, numR);
        if (matchOb != null)
        {
            if (matchOb.SameNumberId >= 0)
            {
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
        var matchInfoOb = GameCardDB.Get().FindByGridLocationAnyFilter(pageIdx, cellIdx, filterMode);
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
        else if (markFailed)
        {
            OnFailedCard($"page:{pageIdx}, cell:{cellIdx}, mode:{filterMode}");
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
        if (string.IsNullOrWhiteSpace(desc))
        {
            return null;
        }

        desc = desc.Trim();
        var matchOb = mods.mods.Find(x =>
            x.GetLocalizedName().Equals(desc, StringComparison.OrdinalIgnoreCase) ||
            x.GetCodeName().Equals(desc, StringComparison.OrdinalIgnoreCase)) ?? TryParseRouletteWithResolvedRule(desc);
        if (matchOb == null && markFailed)
        {
            OnFailedModifier(desc);
        }

        if (matchOb is TriadGameModifierRoulette rouletteTemplate)
        {
            var roulette = (TriadGameModifierRoulette)rouletteTemplate.Clone();
            if (TryParseRouletteResolvedRuleName(desc, out var resolvedRule))
            {
                roulette.SetRuleInstance(resolvedRule);
            }

            return roulette;
        }

        return matchOb?.Clone();
    }

    private TriadGameModifier? TryParseRouletteWithResolvedRule(string desc)
    {
        if (!TryParseRouletteResolvedRuleName(desc, out var _))
        {
            return null;
        }

        var rouletteTemplate = mods.mods.OfType<TriadGameModifierRoulette>().FirstOrDefault();
        if (rouletteTemplate == null)
        {
            return null;
        }

        var roulette = (TriadGameModifierRoulette)rouletteTemplate.Clone();
        if (TryParseRouletteResolvedRuleName(desc, out var resolvedRule))
        {
            roulette.SetRuleInstance(resolvedRule);
        }

        return roulette;
    }

    private bool TryParseRouletteResolvedRuleName(string desc, out TriadGameModifier? resolvedRule)
    {
        resolvedRule = null;
        var openIdx = desc.IndexOf('(');
        var closeIdx = desc.LastIndexOf(')');
        if (openIdx < 0 || closeIdx <= openIdx)
        {
            return false;
        }

        var innerName = desc[(openIdx + 1)..closeIdx].Trim();
        if (innerName.Length == 0)
        {
            return false;
        }

        resolvedRule = mods.mods.Find(x =>
            x is not TriadGameModifierRoulette &&
            (x.GetLocalizedName().Equals(innerName, StringComparison.OrdinalIgnoreCase) ||
             x.GetCodeName().Equals(innerName, StringComparison.OrdinalIgnoreCase)));
        return resolvedRule != null;
    }

    public void OnFailedModifier(string desc)
    {
        Svc.Log.Error($"failed to match rule: {desc}");
        hasFailedModifier = true;
    }

    public TriadNpc? ParseNpcNameStart(string desc, bool markFailed = true)
    {
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
        var trimmed = desc?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        var matchOb = npcs.Find(trimmed) ??
                      npcs.FindByNameStart(trimmed.Length > PartialNpcNameLength
                          ? trimmed[..PartialNpcNameLength]
                          : trimmed);
        if (matchOb == null && markFailed)
        {
            OnFailedNpc(trimmed);
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
        if (lastLoggedNpc != desc)
        {
            lastLoggedNpc = desc;
            Svc.Log.Info($"failed to match npc: {string.Join(", ", desc)}, is it pvp?");
        }

        hasFailedNpc = true;
    }
}
