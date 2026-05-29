using ECommons.EzIpcManager;
using System;
using System.Collections.Generic;
using System.Globalization;
namespace Saucy.IPC;

/// <summary>
///     IPC client for PunishXIV/Questionable (<c>Questionable/External/QuestionableIpc.cs</c>).
/// </summary>
[IPC(IPCNames.Questionable)]
internal static class Questionable
{
    [EzIPC]
    public static Func<string, bool> StartSingleQuest;

    [EzIPC]
    public static Func<string, bool> IsQuestLocked;

    [EzIPC]
    public static Func<string, bool> IsReadyToAcceptQuest;

    [EzIPC]
    public static Func<string, bool> IsQuestComplete;

    [EzIPC]
    public static Func<string, bool> IsQuestAccepted;

    [EzIPC]
    public static Func<string, bool> IsQuestUnobtainable;

    public static bool IsInstalled => SubscriptionManager.IsInitialized(IPCNames.Questionable);
}

/// <summary>Saucy helpers for Questionable quest IPC.</summary>
internal static class QuestionableTriad
{
    private static readonly HashSet<uint> QuestsWithoutPath = [];

    public static void ClearUnsupportedCache() => QuestsWithoutPath.Clear();

    public static bool HasAutomationPath(uint luminaQuestRowId)
    {
        if (!Questionable.IsInstalled || luminaQuestRowId == 0)
        {
            return true;
        }

        if (QuestsWithoutPath.Contains(luminaQuestRowId))
        {
            return false;
        }

        var locked = IsQuestLocked(luminaQuestRowId);
        var ready = IsReadyToAccept(luminaQuestRowId);

        if (locked && ready)
        {
            return false;
        }

        if (!locked)
        {
            return true;
        }

        return true;
    }

    public static bool IsQuestComplete(uint luminaQuestRowId) =>
        InvokeBool(Questionable.IsQuestComplete, luminaQuestRowId);

    public static bool IsQuestAccepted(uint luminaQuestRowId) =>
        InvokeBool(Questionable.IsQuestAccepted, luminaQuestRowId);

    public static bool IsReadyToAccept(uint luminaQuestRowId) =>
        InvokeBool(Questionable.IsReadyToAcceptQuest, luminaQuestRowId);

    public static bool IsQuestLocked(uint luminaQuestRowId) =>
        InvokeBool(Questionable.IsQuestLocked, luminaQuestRowId);

    public static bool IsQuestUnobtainable(uint luminaQuestRowId) =>
        InvokeBool(Questionable.IsQuestUnobtainable, luminaQuestRowId);

    public static bool TryStartSingleQuest(uint luminaQuestRowId)
    {
        if (!Questionable.IsInstalled || luminaQuestRowId == 0)
        {
            return false;
        }

        if (!Questionable.StartSingleQuest.TryInvoke(FormatQuestId(luminaQuestRowId), out var started))
        {
            return false;
        }

        if (!started)
        {
            QuestsWithoutPath.Add(luminaQuestRowId);
        }

        return started;
    }

    private static string FormatQuestId(uint luminaQuestRowId) =>
        ((ushort)(luminaQuestRowId & 0xFFFF)).ToString(CultureInfo.InvariantCulture);

    private static bool InvokeBool(Func<string, bool> ipc, uint luminaQuestRowId)
    {
        if (!Questionable.IsInstalled || luminaQuestRowId == 0)
        {
            return false;
        }

        return ipc.TryInvoke(FormatQuestId(luminaQuestRowId), out var ret) && ret;
    }
}
