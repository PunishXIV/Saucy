using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Saucy.TripleTriad.Data;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad;

public static class TriadMemoryReads
{
    public static bool IsAvailable
        => Svc.ClientState.IsLoggedIn && Svc.Objects.LocalPlayer != null;

    public static unsafe bool TryIsCardOwned(int cardId)
    {
        if (cardId is <= 0 or > ushort.MaxValue)
        {
            return false;
        }

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
            {
                return false;
            }

            return uiState->IsTripleTriadCardUnlocked((ushort)cardId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "TryIsCardOwned failed for card {CardId}", cardId);
            return false;
        }
    }

    public static unsafe bool TryIsNpcBeatenOnce(int triadSheetRowId)
    {
        if (triadSheetRowId < 0x230002)
        {
            return false;
        }

        try
        {
            var uiState = UIState.Instance();
            if (uiState == null)
            {
                return false;
            }

            return uiState->IsTripleTriadNpcBeaten((uint)triadSheetRowId);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "TryIsNpcBeatenOnce failed for triad {TriadId}", triadSheetRowId);
            return false;
        }
    }

    private const uint QuestRowIdOffset = 65536;

    public static IEnumerable<uint> EnumerateQuestCompletionIds(uint questRowId)
    {
        if (questRowId == 0)
        {
            yield break;
        }

        yield return questRowId;

        var shortId = (ushort)(questRowId & 0xFFFF);
        yield return shortId;

        if (questRowId > QuestRowIdOffset)
        {
            yield return questRowId - QuestRowIdOffset;
        }
        else if (questRowId + QuestRowIdOffset != questRowId)
        {
            yield return questRowId + QuestRowIdOffset;
        }
    }

    public static unsafe bool IsQuestCompleteOrUnneeded(uint questId)
    {
        if (questId == 0)
        {
            return true;
        }

        try
        {
            foreach (var candidate in EnumerateQuestCompletionIds(questId))
            {
                if (QuestManager.IsQuestComplete(candidate))
                {
                    return true;
                }
            }

            var uiState = UIState.Instance();
            if (uiState != null)
            {
                foreach (var candidate in EnumerateQuestCompletionIds(questId))
                {
                    if (uiState->IsUnlockLinkUnlockedOrQuestCompleted(candidate))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "IsQuestCompleteOrUnneeded failed for quest {QuestId}", questId);
        }

        return false;
    }

    public static bool HasAnyNpcRewardCard(GameNpcInfo info)
    {
        foreach (var cardId in info.rewardCards)
        {
            if (TryIsCardOwned(cardId))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsNpcUnlockedByProgress(GameNpcInfo info) =>
        info.triadId > 0 &&
        (TryIsNpcBeatenOnce(info.triadId) || HasAnyNpcRewardCard(info));
}
