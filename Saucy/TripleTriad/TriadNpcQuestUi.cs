using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using Lumina.Excel.Sheets;
using TriadBuddyPlugin;

namespace Saucy.TripleTriad;

internal static class TriadNpcQuestUi
{
    public static void DrawUnlockQuest(GameNpcInfo? npcInfo)
    {
        if (npcInfo == null || npcInfo.UnlockQuestId == 0)
            return;

        var questName = npcInfo.UnlockQuestName;
        if (string.IsNullOrEmpty(questName))
            questName = $"Quest #{npcInfo.UnlockQuestId}";

        ImGui.TextDisabled("Unlock quest:");
        ImGui.SameLine();
        ImGui.Text(questName);

        if (!QuestionableInterop.IsInstalled)
        {
            ImGui.TextDisabled("Install Questionable (/qst) to auto-start this quest.");
            return;
        }

        var ready = QuestionableInterop.TryGetReadyToAccept(npcInfo.UnlockQuestId);
        if (ready == false)
            ImGui.TextDisabled("Quest not ready to accept yet (prerequisites may be incomplete).");

        using var dis = ImRaii.Disabled(ready == false);
        if (ImGui.Button("Start with Questionable"))
        {
            if (QuestionableInterop.TryStartQuest(npcInfo.UnlockQuestId))
                Svc.Chat.Print($"[Saucy] Sent \"{questName}\" to Questionable.");
            else
                Svc.Chat.PrintError($"[Saucy] Questionable could not start quest \"{questName}\".");
        }
    }

    public static string? ResolveQuestName(uint questId)
    {
        if (questId == 0)
            return null;

        var sheet = Svc.Data.GetExcelSheet<Quest>();
        var row = sheet?.GetRowOrDefault(questId);
        return row?.Name.ToString();
    }
}
