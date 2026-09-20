using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Saucy.IPC;
namespace Saucy.TripleTriad;

internal static class TriadNpcQuestUi
{
    private static uint _cachedQuestId;
    private static QuestSnapshot? _snapshot;

    public static void InvalidateCache()
    {
        _cachedQuestId = 0;
        _snapshot = null;
    }

    public static void DrawUnlockQuestIconRow(GameNpcInfo? npcInfo)
    {
        if (npcInfo == null || npcInfo.UnlockQuestId == 0)
        {
            return;
        }

        if (TriadMemoryReads.IsNpcUnlockedByProgress(npcInfo) ||
            TriadNpcUnlockHelper.IsUnlockRequirementSatisfied(npcInfo))
        {
            return;
        }

        var snapshot = GetSnapshot(npcInfo);
        if (snapshot.IsComplete)
        {
            return;
        }

        var questName = npcInfo.UnlockQuestName;
        if (string.IsNullOrEmpty(questName))
        {
            questName = Loc.T("Quest #{0}", npcInfo.UnlockQuestId);
        }

        var tooltip = BuildTooltip(snapshot, questName);

        using var questDisabled = ImRaii.Disabled(!snapshot.HasAutomationPath);
        ImGuiLayout.DrawIconTextRow(
            FontAwesomeIcon.BookOpen,
            tooltip,
            () => HandleUnlockQuestClick(npcInfo, questName, snapshot),
            () => ImGui.Text(questName));
    }

    private static void HandleUnlockQuestClick(GameNpcInfo npcInfo, string questName, QuestSnapshot snapshot)
    {
        if (!Questionable.IsInstalled)
        {
            Svc.Chat.Print(Loc.T("[Saucy] Install Questionable (/qst) to start quests from Saucy."));
            return;
        }

        if (!snapshot.HasAutomationPath)
        {
            return;
        }

        InvalidateCache();
        snapshot = GetSnapshot(npcInfo);

        if (QuestionableTriad.TryStartSingleQuest(npcInfo.UnlockQuestId))
        {
            Svc.Chat.Print(Loc.T("[Saucy] Sent \"{0}\" to Questionable.", questName));
            InvalidateCache();
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.StatusMessage))
        {
            // Not wrapped: the whole payload is Questionable's own status text, already in the
            // user's language. Only the "[Saucy] " tag is ours, and that is a brand prefix.
            Svc.Chat.PrintError($"[Saucy] {snapshot.StatusMessage}");
        }
        else
        {
            Svc.Chat.PrintError(Loc.T("[Saucy] Questionable could not start \"{0}\".", questName));
        }
    }

    private static string? BuildTooltip(QuestSnapshot snapshot, string questName)
    {
        if (!Questionable.IsInstalled)
        {
            return Loc.T("Install Questionable (/qst) to start this quest.");
        }

        if (!snapshot.HasAutomationPath)
        {
            return Loc.T("Not supported in Questionable yet.");
        }

        if (snapshot.CanStart)
        {
            return Loc.T("Start \"{0}\" with Questionable", questName);
        }

        return snapshot.StatusMessage;
    }

    private static QuestSnapshot GetSnapshot(GameNpcInfo npcInfo)
    {
        var questId = npcInfo.UnlockQuestId;
        if (_snapshot != null && _cachedQuestId == questId)
        {
            return _snapshot;
        }

        _cachedQuestId = questId;
        _snapshot = BuildSnapshot(npcInfo);
        return _snapshot;
    }

    private static QuestSnapshot BuildSnapshot(GameNpcInfo npcInfo)
    {
        var questId = npcInfo.UnlockQuestId;
        if (TriadNpcUnlockHelper.IsUnlockRequirementSatisfied(npcInfo))
        {
            return CompleteSnapshot(QuestionableTriad.HasAutomationPath(questId));
        }

        if (!Questionable.IsInstalled)
        {
            return new()
            {
                IsComplete = false, HasAutomationPath = true, CanStart = false, StatusMessage = null
            };
        }

        var hasAutomationPath = QuestionableTriad.HasAutomationPath(questId);

        if (TriadMemoryReads.IsQuestCompleteOrUnneeded(questId) ||
            QuestionableTriad.IsQuestComplete(questId))
        {
            return CompleteSnapshot(hasAutomationPath);
        }

        if (!hasAutomationPath)
        {
            return new()
            {
                IsComplete = false, HasAutomationPath = false, CanStart = false, StatusMessage = null
            };
        }

        if (QuestionableTriad.IsQuestAccepted(questId))
        {
            return new()
            {
                IsComplete = false, HasAutomationPath = true, CanStart = false, StatusMessage = Loc.T("Quest already accepted.")
            };
        }

        if (QuestionableTriad.IsQuestUnobtainable(questId))
        {
            return new()
            {
                IsComplete = false, HasAutomationPath = true, CanStart = false, StatusMessage = Loc.T("Quest unavailable in Questionable.")
            };
        }

        if (!QuestionableTriad.IsReadyToAccept(questId))
        {
            // Finished quests are also not "ready to accept" in Questionable — don't blame prerequisites for that.
            if (TriadMemoryReads.IsQuestCompleteOrUnneeded(questId))
            {
                return CompleteSnapshot(hasAutomationPath);
            }

            return new()
            {
                IsComplete = false, HasAutomationPath = true, CanStart = false, StatusMessage = Loc.T("Prerequisites not met yet (check Questionable /qst).")
            };
        }

        return new()
        {
            IsComplete = false, HasAutomationPath = true, CanStart = true, StatusMessage = null
        };
    }

    private static QuestSnapshot CompleteSnapshot(bool hasAutomationPath) =>
        new()
        {
            IsComplete = true, HasAutomationPath = hasAutomationPath, CanStart = false, StatusMessage = null
        };

    private sealed class QuestSnapshot
    {
        public required bool IsComplete { get; init; }
        public required bool HasAutomationPath { get; init; }
        public required bool CanStart { get; init; }
        public required string? StatusMessage { get; init; }
    }
}
