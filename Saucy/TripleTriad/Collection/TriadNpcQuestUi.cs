using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using Saucy.IPC;
using TriadBuddyPlugin;

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
            return;

        var snapshot = GetSnapshot(npcInfo.UnlockQuestId);
        if (snapshot.IsComplete)
            return;

        var questName = npcInfo.UnlockQuestName;
        if (string.IsNullOrEmpty(questName))
            questName = $"Quest #{npcInfo.UnlockQuestId}";

        var tooltip = BuildTooltip(snapshot, questName);

        ImGui.AlignTextToFramePadding();
        var rowY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(rowY - ImGui.GetStyle().FramePadding.Y);

        using (ImRaii.Disabled(!snapshot.HasAutomationPath))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.BookOpen))
                HandleUnlockQuestClick(npcInfo, questName, snapshot);
        }

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        ImGui.SetCursorPosY(rowY);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(questName);
    }

    private static void HandleUnlockQuestClick(GameNpcInfo npcInfo, string questName, QuestSnapshot snapshot)
    {
        if (!QuestionableInterop.IsInstalled)
        {
            Svc.Chat.Print("[Saucy] Install Questionable (/qst) to start quests from Saucy.");
            return;
        }

        if (!snapshot.HasAutomationPath)
            return;

        InvalidateCache();
        snapshot = GetSnapshot(npcInfo.UnlockQuestId);

        if (QuestionableInterop.TryStartSingleQuest(npcInfo.UnlockQuestId))
        {
            Svc.Chat.Print($"[Saucy] Sent \"{questName}\" to Questionable.");
            InvalidateCache();
            return;
        }

        if (!string.IsNullOrEmpty(snapshot.StatusMessage))
            Svc.Chat.PrintError($"[Saucy] {snapshot.StatusMessage}");
        else
            Svc.Chat.PrintError($"[Saucy] Questionable could not start \"{questName}\".");
    }

    private static string? BuildTooltip(QuestSnapshot snapshot, string questName)
    {
        if (!QuestionableInterop.IsInstalled)
            return "Install Questionable (/qst) to start this quest.";

        if (!snapshot.HasAutomationPath)
            return "Not supported in Questionable yet.";

        if (snapshot.CanStart)
            return $"Start \"{questName}\" with Questionable";

        return snapshot.StatusMessage;
    }

    private static QuestSnapshot GetSnapshot(uint questId)
    {
        if (_snapshot != null && _cachedQuestId == questId)
            return _snapshot;

        _cachedQuestId = questId;
        _snapshot = BuildSnapshot(questId);
        return _snapshot;
    }

    private static QuestSnapshot BuildSnapshot(uint questId)
    {
        if (!QuestionableInterop.IsInstalled)
        {
            return new QuestSnapshot
            {
                IsComplete = false,
                HasAutomationPath = true,
                CanStart = false,
                StatusMessage = null,
            };
        }

        var hasAutomationPath = QuestionableInterop.HasAutomationPath(questId);

        if (QuestionableInterop.IsQuestComplete(questId))
        {
            return new QuestSnapshot
            {
                IsComplete = true,
                HasAutomationPath = hasAutomationPath,
                CanStart = false,
                StatusMessage = null,
            };
        }

        if (!hasAutomationPath)
        {
            return new QuestSnapshot
            {
                IsComplete = false,
                HasAutomationPath = false,
                CanStart = false,
                StatusMessage = null,
            };
        }

        if (QuestionableInterop.IsQuestAccepted(questId))
        {
            return new QuestSnapshot
            {
                IsComplete = false,
                HasAutomationPath = true,
                CanStart = false,
                StatusMessage = "Quest already accepted.",
            };
        }

        if (QuestionableInterop.IsQuestUnobtainable(questId))
        {
            return new QuestSnapshot
            {
                IsComplete = false,
                HasAutomationPath = true,
                CanStart = false,
                StatusMessage = "Quest unavailable in Questionable.",
            };
        }

        if (!QuestionableInterop.IsReadyToAccept(questId))
        {
            return new QuestSnapshot
            {
                IsComplete = false,
                HasAutomationPath = true,
                CanStart = false,
                StatusMessage = "Prerequisites not met yet (check Questionable /qst).",
            };
        }

        return new QuestSnapshot
        {
            IsComplete = false,
            HasAutomationPath = true,
            CanStart = true,
            StatusMessage = null,
        };
    }

    private sealed class QuestSnapshot
    {
        public required bool IsComplete { get; init; }
        public required bool HasAutomationPath { get; init; }
        public required bool CanStart { get; init; }
        public required string? StatusMessage { get; init; }
    }
}
