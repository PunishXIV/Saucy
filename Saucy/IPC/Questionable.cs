using ECommons;
using ECommons.EzIpcManager;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Saucy.IPC;

/// <summary>
/// IPC client for PunishXIV/Questionable (<c>Questionable/External/QuestionableIpc.cs</c>).
/// EzIPC pattern follows <see href="https://github.com/Knightmore/Henchman/tree/master/Henchman/IPC"/>.
/// </summary>
internal static class QuestionableInterop
{
    private const string PluginName = "Questionable";

    private static readonly HashSet<uint> QuestsWithoutPath = [];

    private static EzIPCDisposalToken[]? _disposals;

    public static bool IsInstalled => IsLoaded && _disposals != null;

    private static bool IsLoaded =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.InternalName == PluginName && x.IsLoaded);

    public static void Refresh()
    {
        if (!IsLoaded)
        {
            Unsubscribe();
            return;
        }

        if (_disposals != null)
            return;

        _disposals = EzIPC.Init(typeof(Ipc), PluginName);
    }

    public static void Dispose()
    {
        Unsubscribe();
        QuestsWithoutPath.Clear();
    }

    public static void ClearUnsupportedCache() => QuestsWithoutPath.Clear();

    /// <summary>
    /// Whether Questionable has an automation path for this quest.
    /// </summary>
    public static bool HasAutomationPath(uint luminaQuestRowId)
    {
        Refresh();

        if (!IsInstalled || luminaQuestRowId == 0)
            return true;

        if (QuestsWithoutPath.Contains(luminaQuestRowId))
            return false;

        var locked = IsQuestLocked(luminaQuestRowId);
        var ready = IsReadyToAccept(luminaQuestRowId);

        // Questionable.IsQuestLocked returns true when the quest is missing from its registry.
        // If the game says the quest is ready anyway, Questionable definitely has no path.
        if (locked && ready)
            return false;

        // Not locked means the quest is in Questionable's registry and isn't locked there.
        if (!locked)
            return true;

        return true;
    }

    public static bool IsQuestComplete(uint luminaQuestRowId) =>
        InvokeBool(Ipc.IsQuestComplete, luminaQuestRowId);

    public static bool IsQuestAccepted(uint luminaQuestRowId) =>
        InvokeBool(Ipc.IsQuestAccepted, luminaQuestRowId);

    public static bool IsReadyToAccept(uint luminaQuestRowId) =>
        InvokeBool(Ipc.IsReadyToAcceptQuest, luminaQuestRowId);

    public static bool IsQuestLocked(uint luminaQuestRowId) =>
        InvokeBool(Ipc.IsQuestLocked, luminaQuestRowId);

    public static bool IsQuestUnobtainable(uint luminaQuestRowId) =>
        InvokeBool(Ipc.IsQuestUnobtainable, luminaQuestRowId);

    public static bool TryStartSingleQuest(uint luminaQuestRowId)
    {
        Refresh();

        if (!IsInstalled || luminaQuestRowId == 0)
            return false;

        if (!Ipc.StartSingleQuest.TryInvoke(FormatQuestId(luminaQuestRowId), out var started))
            return false;

        if (!started)
            QuestsWithoutPath.Add(luminaQuestRowId);

        return started;
    }

    /// <summary>Matches Questionable.Model.Questing.QuestId.FromRowId.</summary>
    internal static string FormatQuestId(uint luminaQuestRowId) =>
        ((ushort)(luminaQuestRowId & 0xFFFF)).ToString(CultureInfo.InvariantCulture);

    private static void Unsubscribe()
    {
        if (_disposals == null)
            return;

        foreach (var token in _disposals)
            token.Dispose();

        _disposals = null;
    }

    private static bool InvokeBool(Func<string, bool> ipc, uint luminaQuestRowId)
    {
        Refresh();

        if (!IsInstalled || luminaQuestRowId == 0)
            return false;

        return ipc.TryInvoke(FormatQuestId(luminaQuestRowId), out var ret) && ret;
    }

    private static class Ipc
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
    }
}
