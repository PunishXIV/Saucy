using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using TriadNpcSheet = Lumina.Excel.Sheets.TripleTriad;

namespace Saucy.TripleTriad;

internal static class TriadNpcUnlockHelper
{
    public enum RejectMode
    {
        Silent,
        Announce
    }

    private static readonly TimeSpan AnnounceThrottle = TimeSpan.FromSeconds(30);
    private static string? lastAnnouncedReason;
    private static DateTime lastAnnouncedUtc;

    public static bool IsUnlocked(TriadNpc? npc, out string reason)
    {
        reason = string.Empty;
        if (npc == null)
        {
            return true;
        }

        if (!TryResolveGameNpcInfo(npc, out var info))
        {
            reason = FormatCouldNotVerifyMessage(npc.Name);
            return false;
        }

        if (!AreUnlockQuestsComplete(info, out var incompleteQuests))
        {
            reason = FormatLockedMessageAnyOf(npc.Name, incompleteQuests);
            return false;
        }

        return true;
    }

    public static bool TryRejectForClick(TriadNpc? npc, out string reason) =>
        TryReject(npc, out reason, RejectMode.Announce);

    public static bool TryReject(TriadNpc? npc, out string reason, RejectMode mode = RejectMode.Silent)
    {
        if (IsUnlocked(npc, out reason))
        {
            return false;
        }

        if (mode == RejectMode.Announce)
        {
            Announce(reason);
        }

        return true;
    }

    public static void Announce(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return;
        }

        if (string.Equals(reason, lastAnnouncedReason, StringComparison.Ordinal) &&
            DateTime.UtcNow - lastAnnouncedUtc < AnnounceThrottle)
        {
            return;
        }

        lastAnnouncedReason = reason;
        lastAnnouncedUtc = DateTime.UtcNow;
        Svc.Chat.PrintError(reason);
    }

    public static string? TryGetTooltipLine(TriadNpc? npc) =>
        npc == null || IsUnlocked(npc, out var reason) ? null : reason;

    public static bool IsUnlockRequirementSatisfied(GameNpcInfo? info) =>
        info != null && AreUnlockQuestsComplete(info, out var _);

    private static string FormatLockedMessage(string npcName, uint incompleteQuestId, string? incompleteQuestName) =>
        Loc.T(
            "[Saucy] {0}'s Triple Triad isn't unlocked yet — complete {1} first.",
            npcName,
            FormatQuestLabel(incompleteQuestId, incompleteQuestName));

    private static string FormatLockedMessageAnyOf(string npcName, IReadOnlyList<(uint QuestId, string? QuestName)> quests)
    {
        if (quests.Count == 0)
        {
            return Loc.T("[Saucy] {0}'s Triple Triad isn't unlocked yet.", npcName);
        }

        if (quests.Count == 1)
        {
            return FormatLockedMessage(npcName, quests[0].QuestId, quests[0].QuestName);
        }

        // The separator and the quote marks are keys of their own: CJK wants 、 and “”
        // where English wants ", " and "". Their English values are the fallback, so the
        // untranslated rendering is unchanged.
        var labels = string.Join(Loc.T(", "), quests.Select(q => FormatQuestLabel(q.QuestId, q.QuestName)));
        return Loc.T("[Saucy] {0}'s Triple Triad isn't unlocked yet — complete one of: {1}.", npcName, labels);
    }

    private static string FormatCouldNotVerifyMessage(string npcName) =>
        Loc.T("[Saucy] Could not verify Triple Triad unlock for {0}.", npcName);

    public static string FormatNavigationInteractAbortMessage(TriadNpc? npc) =>
        Loc.T(
            "[Saucy] Triple Triad is not available with {0} (unlocked yet?). Aborting.",
            npc?.Name ?? Loc.T("this NPC"));

    private static string FormatQuestLabel(uint questId, string? questName) =>
        string.IsNullOrEmpty(questName) ? Loc.T("quest #{0}", questId) : Loc.T("\"{0}\"", questName);

    private static bool TryResolveGameNpcInfo(TriadNpc npc, out GameNpcInfo info) =>
        GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out info!);

    private static bool AreUnlockQuestsComplete(
        GameNpcInfo info,
        out List<(uint QuestId, string? QuestName)> incompleteQuests)
    {
        incompleteQuests = [];

        if (TriadMemoryReads.IsNpcUnlockedByProgress(info))
        {
            return true;
        }

        var sheet = Svc.Data.GetExcelSheet<TriadNpcSheet>();
        if (sheet != null && info.triadId > 0)
        {
            var row = sheet.GetRowOrDefault((uint)info.triadId);
            if (row != null)
            {
                var prerequisites = new List<uint>();
                foreach (var questRef in row.Value.PreviousQuest)
                {
                    if (questRef.RowId == 0)
                    {
                        continue;
                    }

                    prerequisites.Add(questRef.RowId);
                    if (TriadMemoryReads.IsQuestCompleteOrUnneeded(questRef.RowId))
                    {
                        return true;
                    }
                }

                if (prerequisites.Count > 0)
                {
                    var questSheet = Svc.Data.GetExcelSheet<Quest>();
                    foreach (var questId in prerequisites)
                    {
                        incompleteQuests.Add((questId, questSheet?.GetRowOrDefault(questId)?.Name.ToString()));
                    }

                    return false;
                }
            }
        }

        if (info.UnlockQuestId == 0)
        {
            return true;
        }

        if (TriadMemoryReads.IsQuestCompleteOrUnneeded(info.UnlockQuestId))
        {
            return true;
        }

        incompleteQuests.Add((info.UnlockQuestId, info.UnlockQuestName));
        return false;
    }
}
