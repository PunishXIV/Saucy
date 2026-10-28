using Dalamud.Game.ClientState.Conditions;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.STD;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Saucy.KeyboundBrawler;

public sealed class KeyboundBrawlerModule : Module
{
    public override string InternalName => ModuleNames.KeyboundBrawler;
    public override string Name         => "Keybound Brawler";

    public override void Enable() => Svc.Framework.Update += OnFrameworkUpdate;

    public override void Disable()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        KeyboundBrawlerAutomation.Reset();
    }

    private static void OnFrameworkUpdate(IFramework _) => KeyboundBrawlerAutomation.OnUpdate();
}

internal static unsafe class KeyboundBrawlerAutomation
{
    private const  string UseWordActionSignature   = "48 89 5C 24 18 56 57 41 56 48 83 EC 40 48 8B 1D ?? ?? ?? ?? 4C 8B F2 48 8B F1";
    private const  uint   MgpItemId                = 29;
    private const  uint   MgcItemId                = 52322;
    private const  uint   QueueNpcBaseId           = 1058751;
    private const  uint   QueueAcceptButtonId      = 9;
    private const  uint   ResultCloseButtonId      = 48;
    private const  uint   CursedGazeActionId       = 49529;
    private const  uint   SensoryJammerActionId    = 49532;
    private const  uint   SensoryJammerAltActionId = 49702;
    private const  int    ForbiddenCastWindowMs    = 500;
    private const  int    PostCastDelayMs           = 3000;
    private static nint   useWordActionAddress;
    private static bool   scanAttempted;
    private static uint   pendingEntityId;
    private static string pendingWord      = "";
    private static string pendingAlternate = "";
    private static long   pendingReadyAt;
    private static long   pendingTypingDurationMs;
    private static long   wordEntryResumeAt = -1;
    private static long   forbiddenCastBlockUntil;
    private static long   lastInactivityResetAt;
    private static bool   awaitingQueueDialogue;
    private static bool   queueFlowActive, queueRequested, queueAcceptClicked;
    private static bool   ownsQueuePath;
    private static long   queueDialogueDeadline;
    private static long   queueRetryAt;
    private static long   nextQueueAt;
    private static long   nextQueueMoveAt;
    private static bool   wasInDuty,    roundTracked, resultRecorded;
    private static int    lastMgp = -1, lastMgc = -1;
    private static long   rewardUntil;

    internal static bool   ScanFailed  => scanAttempted && useWordActionAddress == 0;
    internal static string QueueStatus { get; private set; } = "Looking for entry NPC";

    internal static bool IsInActiveMinigame => GetDirector() != 0;
    internal static bool IsAutoQueueFlowActive => queueFlowActive;
    private static long RequeueDelayMs => (long)Math.Max(0, C.KeyboundBrawlerRequeueDelaySeconds) * 1000;

    internal static void OnUpdate()
    {
        TickRewards();
        TickAutoQueue();
        TickGameplay();
    }

    internal static void Reset()
    {
        pendingEntityId       = 0;
        pendingTypingDurationMs = 0;
        wordEntryResumeAt      = -1;
        forbiddenCastBlockUntil = 0;
        lastInactivityResetAt = 0;
        wasInDuty             = roundTracked = resultRecorded = false;
        lastMgp               = lastMgc      = -1;
        awaitingQueueDialogue = false;
        queueFlowActive = queueRequested = queueAcceptClicked = false;
        queueRetryAt          = nextQueueAt = nextQueueMoveAt = 0;
        StopQueuePath();
        QueueStatus = "Looking for entry NPC";
    }

    private static void TickRewards()
    {
        var inDuty    = GetDirector() != 0;
        var inventory = InventoryManager.Instance();
        if (inDuty && !wasInDuty)
        {
            roundTracked   = true;
            resultRecorded = false;
            lastMgp        = lastMgc = -1;
        }

        wasInDuty = inDuty;
        if (!roundTracked)
        {
            return;
        }

        if (inDuty && lastMgp < 0 && inventory != null)
        {
            lastMgp = inventory->GetInventoryItemCount(MgpItemId, false, false, false);
            lastMgc = inventory->GetInventoryItemCount(MgcItemId, false, false, false);
        }

        if (!resultRecorded)
        {
            var result = (AtkUnitBase*)Svc.GameGui.GetAddonByName("KTGResult")
                                          .Address;
            if (result == null || !result->IsReady || !result->IsVisible || !AgentHelper.IsAddonOwnedBy(result, AgentId.KTGResult))
            {
                return;
            }

            resultRecorded = true;
            rewardUntil    = Environment.TickCount64 + 15000;
            nextQueueAt    = Environment.TickCount64 + RequeueDelayMs;
            StatsSessionClock.MarkBrawlerActive();
            C.UpdateStats(stats => stats.BrawlerGamesPlayed++);
            C.Save();
        }

        if (Environment.TickCount64 > rewardUntil || inventory == null)
        {
            return;
        }

        var mgp = inventory->GetInventoryItemCount(MgpItemId, false, false, false);
        var mgc = inventory->GetInventoryItemCount(MgcItemId, false, false, false);
        var gainedMgp = lastMgp < 0
                                ? 0
                                : Math.Max(0, mgp - lastMgp);
        var gainedMgc = lastMgc < 0
                                ? 0
                                : Math.Max(0, mgc - lastMgc);
        lastMgp = mgp;
        lastMgc = mgc;
        if (gainedMgp == 0 && gainedMgc == 0)
        {
            return;
        }

        C.UpdateStats(stats =>
                      {
                          stats.BrawlerMGP += gainedMgp;
                          stats.BrawlerMGC += gainedMgc;
                      });
        C.Save();
    }

    private static void TickGameplay()
    {
        var director = GetDirector();
        if (director == 0 || *((byte*)director + 0x1FC0) == 0)
        {
            wordEntryResumeAt = director == 0 ? -1 : 0;
            pendingEntityId       = 0;
            if (director == 0) forbiddenCastBlockUntil = 0;
            lastInactivityResetAt = 0;
            return;
        }

        var now = Environment.TickCount64;
        if (wordEntryResumeAt == 0) wordEntryResumeAt = now + 2000;
        if (now < wordEntryResumeAt) return;

        if (!scanAttempted)
        {
            scanAttempted = true;
            if (Svc.SigScanner.TryScanText(UseWordActionSignature, out var address))
            {
                useWordActionAddress = address;
            }
            else
            {
                Svc.Log.Warning("[KeyboundBrawler] Word action signature was not found.");
            }
        }

        if (useWordActionAddress == 0)
        {
            return;
        }

        var agentModule = AgentModule.Instance();
        var hud = agentModule == null
                          ? null
                          : agentModule->GetAgentByInternalId((AgentId)397);
        if (hud == null)
        {
            pendingEntityId = 0;
            return;
        }

        if (lastInactivityResetAt == 0 || now - lastInactivityResetAt >= 20000)
        {
            lastInactivityResetAt = now;
            var input = agentModule->GetAgentByInternalId((AgentId)398);
            if (input != null && *(nint*)((byte*)input + 0x28) == (nint)hud)
            {
                *(ulong*)((byte*)input + 0x50) = 0;
            }
            else
            {
                Svc.Log.Warning("[KeyboundBrawler] Input agent layout was not recognized; inactivity timer was not reset.");
            }
        }

        var words = (StdMap<uint, HudTarget>*)((byte*)hud + 0x28);
        if (*(nint*)words == 0 || words->Count == 0)
        {
            pendingEntityId = 0;
            return;
        }

        var         bestPriority        = -1;
        var         bestLength          = int.MaxValue;
        var         bestDistance        = float.MaxValue;
        uint        bestEntityId        = 0;
        GameObject* bestObject          = null;
        var         bestWord            = "";
        var         bestAlternate       = "";
        GameObject* pendingObject       = null;
        var         pendingPriority     = -1;
        var         player              = Svc.Objects.LocalPlayer;
        var         objectManager       = GameObjectManager.Instance();
        if (objectManager == null)
        {
            pendingEntityId = 0;
            return;
        }

        foreach (var entityId in words->Keys)
        {
            var gameObject = objectManager->Objects.GetObjectByEntityId(entityId);
            if (gameObject             == null                 ||
                gameObject->ObjectKind != ObjectKind.BattleNpc ||
                !words->TryGetValuePointer(in entityId, out var entry))
            {
                continue;
            }

            var character = gameObject->GetAsCharacter();
            var cast = character == null
                               ? null
                               : character->GetCastInfo();
            if (cast != null    &&
                cast->IsCasting &&
                cast->ActionId is CursedGazeActionId or SensoryJammerActionId or SensoryJammerAltActionId)
            {
                var remainingMs = (cast->TotalCastTime - cast->CurrentCastTime) * 1000f;
                if (remainingMs <= ForbiddenCastWindowMs)
                {
                    var blockUntil = now + (long)Math.Max(remainingMs, 0) + PostCastDelayMs;
                    if (blockUntil > forbiddenCastBlockUntil)
                    {
                        forbiddenCastBlockUntil = blockUntil;
                        if (pendingEntityId != 0)
                        {
                            pendingReadyAt = Math.Max(pendingReadyAt, blockUntil + pendingTypingDurationMs);
                        }
                    }
                }
            }

            if (gameObject->IsDead() && entry->BubbleType != 2)
            {
                continue;
            }

            var word      = entry->Word.ToString();
            var alternate = entry->AlternateWord.ToString();
            var length    = word.Length == 0 ? alternate.Length : alternate.Length == 0 ? word.Length : Math.Min(word.Length, alternate.Length);
            if (length == 0 || entry->BubbleType == 3)
            {
                continue;
            }

            var priority = BubblePriority(entry);
            if (entityId == pendingEntityId && word == pendingWord && alternate == pendingAlternate)
            {
                pendingObject   = gameObject;
                pendingPriority = priority;
            }

            var distance = player == null
                                   ? float.MaxValue
                                   : Vector3.DistanceSquared(player.Position, gameObject->Position);
            if (priority < bestPriority ||
                priority == bestPriority &&
                (distance > bestDistance || distance == bestDistance && length >= bestLength))
            {
                continue;
            }

            bestPriority  = priority;
            bestLength    = length;
            bestDistance  = distance;
            bestEntityId  = entityId;
            bestObject    = gameObject;
            bestWord      = word;
            bestAlternate = alternate;
        }

        if (pendingObject != null && pendingPriority == bestPriority)
        {
            bestEntityId  = pendingEntityId;
            bestObject    = pendingObject;
            bestWord      = pendingWord;
            bestAlternate = pendingAlternate;
        }

        if (bestObject == null)
        {
            pendingEntityId = 0;
            return;
        }

        if (pendingEntityId != bestEntityId || pendingWord != bestWord || pendingAlternate != bestAlternate)
        {
            pendingEntityId  = bestEntityId;
            pendingWord      = bestWord;
            pendingAlternate = bestAlternate;
            var min = Math.Clamp(C.KeyboundBrawlerMinMsPerCharacter, 100, 2000);
            var max = Math.Clamp(C.KeyboundBrawlerMaxMsPerCharacter, min, 2000);
            pendingTypingDurationMs = bestLength * Random.Shared.Next(min, max + 1);
            pendingReadyAt = Math.Max(now, forbiddenCastBlockUntil) + pendingTypingDurationMs;
            return;
        }

        if (now < pendingReadyAt || now < forbiddenCastBlockUntil)
        {
            return;
        }

        if (!words->TryGetValuePointer(in bestEntityId, out var currentEntry) ||
            bestObject->IsDead() && currentEntry->BubbleType != 2)
        {
            pendingEntityId = 0;
            return;
        }

        var useWordAction = (delegate* unmanaged<nint, GameObject*, byte, byte>)useWordActionAddress;
        if (useWordAction(director, bestObject, 1) != 0)
        {
            pendingEntityId = 0;
        }
        else
        {
            pendingReadyAt = now + 250;
        }
    }

    private static void TickAutoQueue()
    {
        var result = (AtkUnitBase*)Svc.GameGui.GetAddonByName("KTGResult")
                                      .Address;
        if (result != null && result->IsReady && result->IsVisible && AgentHelper.IsAddonOwnedBy(result, AgentId.KTGResult))
        {
            if (C.KeyboundBrawlerAutoQueue)
            {
                queueFlowActive = true;
                YesAlready.SyncForGameActivity(true);
            }

            QueueStatus = AddonButton.TryClick(result, ResultCloseButtonId)
                                  ? "Closing Brawler result"
                                  : "Waiting for result close button";
            return;
        }

        if (!C.KeyboundBrawlerAutoQueue)
        {
            queueFlowActive = queueRequested = queueAcceptClicked = false;
            awaitingQueueDialogue = false;
            queueRetryAt          = 0;
            StopQueuePath();
            return;
        }

        if (GetDirector() != 0 ||
            Svc.Condition[ConditionFlag.BetweenAreas] ||
            Svc.Condition[ConditionFlag.BoundByDuty95])
        {
            queueFlowActive = queueRequested = queueAcceptClicked = false;
            awaitingQueueDialogue = false;
            queueRetryAt = 0;
            StopQueuePath();
            QueueStatus = "Queued or entering Brawler";
            return;
        }

        var now = Environment.TickCount64;
        if (queueRequested)
        {
            var confirm = (AddonContentsFinderConfirm*)Svc.GameGui.GetAddonByName("ContentsFinderConfirm").Address;
            if (confirm != null && confirm->AtkUnitBase.IsReady && confirm->AtkUnitBase.IsVisible)
            {
                queueRetryAt = now + 5000;
                if (!queueAcceptClicked)
                {
                    queueAcceptClicked = AddonButton.TryClick(&confirm->AtkUnitBase, confirm->CommenceButton);
                }

                QueueStatus = queueAcceptClicked ? "Duty accepted - waiting to enter" : "Waiting for duty confirmation";
                return;
            }

            queueAcceptClicked = false;
            var finder = ContentsFinder.Instance();
            var state = finder == null ? ContentsFinderQueueState.None : finder->QueueInfo.QueueState;
            if (state != ContentsFinderQueueState.None ||
                Svc.Condition[ConditionFlag.WaitingForDuty] ||
                Svc.Condition[ConditionFlag.WaitingForDutyFinder])
            {
                queueRetryAt = now + 5000;
            }

            if (state == ContentsFinderQueueState.Ready)
            {
                QueueStatus = "Waiting for duty confirmation";
                return;
            }

            if (state == ContentsFinderQueueState.None && now >= queueRetryAt)
            {
                queueFlowActive = queueRequested = queueAcceptClicked = false;
                queueRetryAt = now + RequeueDelayMs;
                QueueStatus = "Queue ended - retrying";
                return;
            }

            QueueStatus = "Queued or entering Brawler";
            return;
        }

        if (Svc.Condition[ConditionFlag.WaitingForDuty] ||
            Svc.Condition[ConditionFlag.WaitingForDutyFinder])
        {
            queueFlowActive = false;
            awaitingQueueDialogue = false;
            StopQueuePath();
            QueueStatus = "Queued or entering Brawler";
            return;
        }
        if (awaitingQueueDialogue)
        {
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("KTGEnterDialogue")
                                         .Address;
            if (addon != null    &&
                addon->IsVisible &&
                AgentHelper.IsAddonOwnedBy(addon, AgentId.KTGEnterDialogue))
            {
                if (AddonButton.TryClick(addon, QueueAcceptButtonId))
                {
                    awaitingQueueDialogue = false;
                    queueRequested = true;
                    queueAcceptClicked = false;
                    queueRetryAt          = now + 30000;
                    QueueStatus           = "Entry accepted - waiting for queue";
                }
                else
                {
                    QueueStatus = "Entry button unavailable";
                }

                return;
            }

            if (now < queueDialogueDeadline)
            {
                QueueStatus = "Waiting for entry dialogue";
                return;
            }

            awaitingQueueDialogue = false;
            queueRetryAt          = now + RequeueDelayMs;
        }

        if (now < queueRetryAt)
        {
            QueueStatus = "Waiting before queue retry";
            return;
        }

        if (now < nextQueueAt)
        {
            queueFlowActive = false;
            QueueStatus = "Waiting before next Brawler queue";
            return;
        }

        var npc = ObjectHelper.FindNearestByBaseId(QueueNpcBaseId);
        if (npc == null)
        {
            queueFlowActive = false;
            StopQueuePath();
            QueueStatus = "Entry NPC is not loaded";
            return;
        }

        queueFlowActive = true;
        YesAlready.SyncForGameActivity(true);
        if (!Player.Interactable)
        {
            QueueStatus = "Waiting until player can interact";
            return;
        }

        if (Svc.Targets.Target?.Address != npc.Address)
        {
            if (EzThrottler.Throttle("Saucy.KeyboundBrawler.Queue.Target", 400))
            {
                Svc.Targets.Target = npc;
            }

            QueueStatus = "Targeting entry NPC";
            return;
        }

        if (ObjectHelper.GetHorizontalEdgeDistance(npc) > 3f)
        {
            if (!Vnavmesh.IsInstalled)
            {
                QueueStatus = "Move closer to entry NPC (vnavmesh unavailable)";
                return;
            }

            if (Vnavmesh.IsMoving())
            {
                QueueStatus = ownsQueuePath
                                      ? "Moving to entry NPC"
                                      : "Waiting for current navigation";
                return;
            }

            if (now < nextQueueMoveAt)
            {
                return;
            }

            nextQueueMoveAt = now + 4000;
            if (!Vnavmesh.CanStartPathfind())
            {
                Vnavmesh.TryEnsureNavMeshLoading();
                QueueStatus = "Waiting for navigation mesh";
                return;
            }

            ownsQueuePath = Vnavmesh.TryMoveTo(npc.Position, false, 1f);
            QueueStatus = ownsQueuePath
                                  ? "Moving to entry NPC"
                                  : "Could not path to entry NPC";
            return;
        }

        StopQueuePath();
        if (ObjectHelper.TryInteractWithObject(npc, "Saucy.KeyboundBrawler.Queue"))
        {
            awaitingQueueDialogue = true;
            queueDialogueDeadline = now + 8000;
            QueueStatus           = "Waiting for entry dialogue";
        }
        else
        {
            QueueStatus = "Interacting with entry NPC";
        }
    }

    private static void StopQueuePath()
    {
        if (!ownsQueuePath)
        {
            return;
        }

        if (Vnavmesh.IsMoving())
        {
            Vnavmesh.StopPath();
        }

        ownsQueuePath = false;
    }

    private static int BubblePriority(HudTarget* entry) => entry->BubbleType == 2 || entry->BubbleType == 4 || entry->CastProgress > 0
                                                                   ? 1
                                                                   : 0;

    private static nint GetDirector()
    {
        var framework = EventFramework.Instance();
        var director = framework == null
                               ? null
                               : framework->GetInstanceContentDirector();
        return director != null && *((byte*)director + 0xDCE) == 23
                       ? (nint)director
                       : 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x180)]
    private struct HudTarget
    {
        [FieldOffset(0x04)] public uint       BubbleType;
        [FieldOffset(0x08)] public Utf8String Word;
        [FieldOffset(0x70)] public Utf8String AlternateWord;
        [FieldOffset(0xF8)] public float      CastProgress;
    }
}
