using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Common.Math;
using Saucy.Framework;
using Saucy.IPC;
using System;
using System.Collections.Generic;
using System.Globalization;
namespace Saucy.OtherGames;

public class SliceIsRight : Module
{
    private const float MaxDistance = 30f;

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
        BossMod.TryDisableGateAi();
    }

    private void OnUpdate(IFramework _)
    {
        if (!InSaucer || !PlayerOnStage || CurrentGate is not GateType.SliceIsRight)
        {
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
        if (!InSaucer || CurrentGate is not GateType.SliceIsRight)
        {
            return;
        }

        EnsureColours();
        PruneDespawnedObjects();

        foreach (var gameObject in Svc.Objects)
        {
            if (!(Player.DistanceTo(gameObject) <= MaxDistance))
            {
                continue;
            }
            if (gameObject.ObjectKind == ObjectKind.EventObj && gameObject.BaseId is >= 2010777 and <= 2010779)
            {
                RenderObject(gameObject, gameObject.BaseId);
            }
        }
    }

    private void RenderObject(IGameObject gameObject, uint model, float? radius = null)
    {
        if (ObjectsAndSpawnTime.TryGetValue(gameObject.GameObjectId, out var dateTime))
        {
            if (dateTime.AddSeconds(5) > DateTime.Now)
            {
                return;
            }
            float length;
            switch (model)
            {
                case 2010777:
                    length = (float)((double?)radius ?? 25.0);
                    DrawRectWorld(gameObject, gameObject.Rotation + HalfPi, length, 5f,
                        colourBlue);
                    break;
                case 2010778:
                    length = (float)((double?)radius ?? 25.0);
                    var rotation1 = gameObject.Rotation + HalfPi;
                    var rotation2 = gameObject.Rotation - HalfPi;
                    DrawRectWorld(gameObject, rotation1, length, 5f, colourGreen);
                    DrawRectWorld(gameObject, rotation2, length, 5f, colourGreen);
                    break;
                case 2010779:
                    length = (float)((double?)radius ?? 11.0);
                    DrawFilledCircleWorld(gameObject, length, colourRed);
                    break;
            }
        }
        else
        {
            ObjectsAndSpawnTime.Add(gameObject.GameObjectId, DateTime.Now);
        }
    }

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
        foreach (var id in ObjectsAndSpawnTime.Keys)
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

            if (!found)
            {
                DespawnedIds.Add(id);
            }
        }

        foreach (var id in DespawnedIds)
        {
            ObjectsAndSpawnTime.Remove(id);
        }
    }

    private void DrawRectWorld(IGameObject gameObject, float rotation, float length, float width, uint colour) =>
        DrawInOverlay(gameObject.Address + gameObject.Rotation.ToString(CultureInfo.InvariantCulture), () =>
        {
            var position = gameObject.Position;
            var io = ImGui.GetIO();
            var vector21 = io.DisplaySize;
            var vector31 = new Vector3(position.X + width / 2f * (float)Math.Sin(HalfPi + rotation), position.Y, position.Z + width / 2f * (float)Math.Cos(HalfPi + rotation));
            var vector32 = new Vector3(position.X + width / 2f * (float)Math.Sin(rotation - HalfPi), position.Y, position.Z + width / 2f * (float)Math.Cos(rotation - HalfPi));
            var vector33 = new Vector3(position.X, position.Y, position.Z);
            const int num1 = 20;
            var num2 = length / num1;
            var windowDrawList = ImGui.GetWindowDrawList();
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
                        windowDrawList.PathLineTo(vector22);
                    }
                }

                if (flag)
                {
                    windowDrawList.PathFillConvex(colour);
                }
                else
                {
                    windowDrawList.PathClear();
                }

                vector31 = vector34;
                vector32 = vector35;
                vector33 = vector36;
            }
        });

    private void DrawFilledCircleWorld(IGameObject gameObject, float radius, uint colour) =>
        DrawInOverlay(gameObject.Address.ToString(), () =>
        {
            var position = gameObject.Position;
            const int num = 100;
            var flag = false;
            for (var index = 0; index <= 2 * num; ++index)
            {
                flag |= Svc.GameGui.WorldToScreen(new Vector3(position.X + radius * (float)Math.Sin(Math.PI / num * index), position.Y, position.Z + radius * (float)Math.Cos(Math.PI / num * index)), out var vector2);
                var windowDrawList = ImGui.GetWindowDrawList();
                windowDrawList.PathLineTo(vector2);
            }

            if (flag)
            {
                var windowDrawList = ImGui.GetWindowDrawList();
                windowDrawList.PathFillConvex(colour);
            }
            else
            {
                var windowDrawList = ImGui.GetWindowDrawList();
                windowDrawList.PathClear();
            }
        });

    private static void DrawInOverlay(string name, Action draw)
    {
        using var overlay = new ImGuiLayout.FullscreenOverlayScope("slice_" + name, (ImGuiWindowFlags)787337);
        if (!overlay.Success)
        {
            return;
        }

        draw();
    }
}
