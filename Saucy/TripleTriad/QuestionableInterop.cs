using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons;
using System;

namespace Saucy.TripleTriad;

internal static class QuestionableInterop
{
    private const string StartSingleQuest = "Questionable.StartSingleQuest";
    private const string IsReadyToAccept = "Questionable.IsReadyToAcceptQuest";

    private static ICallGateSubscriber<string, bool>? _startSingleQuest;
    private static ICallGateSubscriber<string, bool>? _isReadyToAccept;

    public static bool IsInstalled => _startSingleQuest != null;

    public static void Init(IDalamudPluginInterface pluginInterface)
    {
        _startSingleQuest = TrySubscribe(pluginInterface, StartSingleQuest);
        _isReadyToAccept = TrySubscribe(pluginInterface, IsReadyToAccept);
    }

    public static void Dispose()
    {
        _startSingleQuest = null;
        _isReadyToAccept = null;
    }

    public static bool TryStartQuest(uint questId)
    {
        if (_startSingleQuest == null || questId == 0)
            return false;

        try
        {
            return _startSingleQuest.InvokeFunc(questId.ToString());
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[Saucy] Questionable.StartSingleQuest failed");
            return false;
        }
    }

    public static bool? TryGetReadyToAccept(uint questId)
    {
        if (_isReadyToAccept == null || questId == 0)
            return null;

        try
        {
            return _isReadyToAccept.InvokeFunc(questId.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static ICallGateSubscriber<string, bool>? TrySubscribe(IDalamudPluginInterface pluginInterface, string name)
    {
        try
        {
            return pluginInterface.GetIpcSubscriber<string, bool>(name);
        }
        catch
        {
            return null;
        }
    }
}
