using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Common.Math;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Collections.Generic;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
namespace Saucy.OtherGames;

public unsafe class SliceIsRight : Module
{
    private const float MaxDistance = 30f;

    // Bamboo telegraphs activate ~5s after the helper spawns, then resolve ~7s later (Boss Mod timing).
    private const double TelegraphDelaySeconds = 5;
    private const double TelegraphDurationSeconds = 7;

    // EventObj GimmickId values (original working detection via GameObject+0x80).
    private const uint GimmickSingleRect = 2010777;
    private const uint GimmickDoubleRect = 2010778;
    private const uint GimmickCircle = 2010779;

    // Boss Mod helper actor OIDs, exposed as BaseId on spawned helpers.
    private const uint HelperSingleRectOid = 0x1EAE99;
    private const uint HelperDoubleRectOid = 0x1EAE9A;
    private const uint HelperCircleOid = 0x1EAE9B;

    private static uint colourBlue;
    private static uint colourGreen;
    private static uint colourRed;
    private static bool coloursInitialized;
    private static readonly Dictionary<ulong, DateTime> ObjectsAndSpawnTime = [];
    private static readonly List<ulong> DespawnedIds = [];
    public override string Name => "Slice is Right";

    private static float HalfPi => MathF.PI / 2;

    public override void Enable()
    {
        Svc.Framework.Update += OnUpdate;
        Svc.PluginInterface.UiBuilder.Draw += Draw;
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        ClearTrackedObjects();
        BossMod.TryDisableGateAi();
    }

    private void OnUpdate(IFramework _)
    {
        if (!IsInGate(GateType.SliceIsRight))
        {
            ClearTrackedObjects();
            BossMod.TryDisableGateAi();
            return;
        }

        if (!C.GoldSaucerGates.SliceIsRightAutoMovement)
        {
            BossMod.TryDisableGateAi();
            return;
        }

        BossMod.TryEnableGateAi();
    }

    public void Draw()
    {
        if (!IsInGate(GateType.SliceIsRight))
        {
            ClearTrackedObjects();
            return;
        }

        EnsureColours();
        PruneDespawnedObjects();

        // Draw into a fullscreen overlay window's draw list, the same pattern the other gate
        // overlays use. ImGui.GetForegroundDrawList() produced no visible output here under Dalamud.
        using var overlay = new ImGuiLayout.FullscreenOverlayScope("slice", (ImGuiWindowFlags)787337);
        if (!overlay.Success)
        {
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        foreach (var gameObject in Svc.Objects)
        {
            if (!(Player.DistanceTo(gameObject) <= MaxDistance))
            {
                continue;
            }

            if (TryGetSliceHelperType(gameObject, out var helperType))
            {
                RenderObject(drawList, gameObject, helperType);
            }
        }
    }

    private static bool TryGetSliceHelperType(IGameObject gameObject, out uint helperType)
    {
        helperType = 0;
        if (!gameObject.IsValid())
        {
            return false;
        }

        // Telegraph shape is BaseId on EventObj (2010777–2010779). GimmickId is unused on current clients.
        if (gameObject.ObjectKind == ObjectKind.EventObj)
        {
            if (gameObject.BaseId is >= GimmickSingleRect and <= GimmickCircle)
            {
                helperType = gameObject.BaseId;
                return true;
            }

            var gimmickId = ((GameObject*)gameObject.Address)->GimmickId;
            if (gimmickId is >= GimmickSingleRect and <= GimmickCircle)
            {
                helperType = gimmickId;
                return true;
            }
        }

        helperType = gameObject.BaseId switch
        {
            HelperSingleRectOid or GimmickSingleRect => GimmickSingleRect,
            HelperDoubleRectOid or GimmickDoubleRect => GimmickDoubleRect,
            HelperCircleOid or GimmickCircle => GimmickCircle,
            var _ => 0
        };
        return helperType != 0;
    }

    private static void RenderObject(ImDrawListPtr drawList, IGameObject gameObject, uint helperType, float? radius = null)
    {
        if (ObjectsAndSpawnTime.TryGetValue(gameObject.GameObjectId, out var firstSeen))
        {
            if (!IsTelegraphVisible(firstSeen))
            {
                return;
            }

            float length;
            switch (helperType)
            {
                case GimmickSingleRect:
                    length = (float)((double?)radius ?? 25.0);
                    DrawRectWorld(drawList, gameObject, gameObject.Rotation + HalfPi, length, 5f, colourBlue);
                    break;
                case GimmickDoubleRect:
                    length = (float)((double?)radius ?? 25.0);
                    var rotation1 = gameObject.Rotation + HalfPi;
                    var rotation2 = gameObject.Rotation - HalfPi;
                    DrawRectWorld(drawList, gameObject, rotation1, length, 5f, colourGreen);
                    DrawRectWorld(drawList, gameObject, rotation2, length, 5f, colourGreen);
                    break;
                case GimmickCircle:
                    length = (float)((double?)radius ?? 11.0);
                    DrawFilledCircleWorld(drawList, gameObject, length, colourRed);
                    break;
            }
        }
        else
        {
            ObjectsAndSpawnTime.Add(gameObject.GameObjectId, DateTime.Now);
        }
    }

    private static bool IsTelegraphVisible(DateTime firstSeen)
    {
        var now = DateTime.Now;
        var visibleFrom = firstSeen.AddSeconds(TelegraphDelaySeconds);
        var visibleUntil = visibleFrom.AddSeconds(TelegraphDurationSeconds);
        return now >= visibleFrom && now < visibleUntil;
    }

    private static bool IsTelegraphExpired(DateTime firstSeen) =>
        DateTime.Now >= firstSeen.AddSeconds(TelegraphDelaySeconds + TelegraphDurationSeconds);

    private static void ClearTrackedObjects() => ObjectsAndSpawnTime.Clear();

    private static void EnsureColours()
    {
        if (coloursInitialized)
        {
            return;
        }

        colourBlue = ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.0f, 1f, 0.15f)));
        colourGreen = ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 1f, 0.0f, 0.15f)));
        colourRed = ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.0f, 0.0f, 0.4f)));
        coloursInitialized = true;
    }

    private static void PruneDespawnedObjects()
    {
        DespawnedIds.Clear();
        foreach ((var id, var firstSeen) in ObjectsAndSpawnTime)
        {
            var found = false;
            foreach (var obj in Svc.Objects)
            {
                if (obj.GameObjectId != id)
                {
                    continue;
                }

                found = true;
                break;
            }

            if (!found || IsTelegraphExpired(firstSeen))
            {
                DespawnedIds.Add(id);
            }
        }

        foreach (var id in DespawnedIds)
        {
            ObjectsAndSpawnTime.Remove(id);
        }
    }

    private static void DrawRectWorld(ImDrawListPtr drawList, IGameObject gameObject, float rotation, float length, float width, uint colour)
    {
        var position = gameObject.Position;
        var io = ImGui.GetIO();
        var vector21 = io.DisplaySize;
        var vector31 = new Vector3(position.X + width / 2f * (float)Math.Sin(HalfPi + rotation), position.Y, position.Z + width / 2f * (float)Math.Cos(HalfPi + rotation));
        var vector32 = new Vector3(position.X + width / 2f * (float)Math.Sin(rotation - HalfPi), position.Y, position.Z + width / 2f * (float)Math.Cos(rotation - HalfPi));
        var vector33 = new Vector3(position.X, position.Y, position.Z);
        const int num1 = 20;
        var num2 = length / num1;
        for (var index = 1; index <= num1; ++index)
        {
            var vector34 = new Vector3(vector31.X + num2 * (float)Math.Sin(rotation), vector31.Y, vector31.Z + num2 * (float)Math.Cos(rotation));
            var vector35 = new Vector3(vector32.X + num2 * (float)Math.Sin(rotation), vector32.Y, vector32.Z + num2 * (float)Math.Cos(rotation));
            var vector36 = new Vector3(vector33.X + num2 * (float)Math.Sin(rotation), vector33.Y, vector33.Z + num2 * (float)Math.Cos(rotation));
            var flag = false;
            var vector3Array = new[]
            {
                vector35, vector36, vector34, vector31, vector33, vector32
            };
            foreach (var vector37 in vector3Array)
            {
                flag |= Svc.GameGui.WorldToScreen(vector37, out var vector22);
                if (vector22.X > 0.0 & (double)vector22.X < vector21.X || vector22.Y > 0.0 & (double)vector22.Y < vector21.Y)
                {
                    drawList.PathLineTo(vector22);
                }
            }

            if (flag)
            {
                drawList.PathFillConvex(colour);
            }
            else
            {
                drawList.PathClear();
            }

            vector31 = vector34;
            vector32 = vector35;
            vector33 = vector36;
        }
    }

    private static void DrawFilledCircleWorld(ImDrawListPtr drawList, IGameObject gameObject, float radius, uint colour)
    {
        var position = gameObject.Position;
        const int num = 100;
        var flag = false;
        for (var index = 0; index <= 2 * num; ++index)
        {
            flag |= Svc.GameGui.WorldToScreen(new Vector3(position.X + radius * (float)Math.Sin(Math.PI / num * index), position.Y, position.Z + radius * (float)Math.Cos(Math.PI / num * index)), out var vector2);
            drawList.PathLineTo(vector2);
        }

        if (flag)
        {
            drawList.PathFillConvex(colour);
        }
        else
        {
            drawList.PathClear();
        }
    }
}
