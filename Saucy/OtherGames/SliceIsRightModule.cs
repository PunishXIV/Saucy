using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Common.Math;
using Dalamud.Bindings.ImGui;

namespace Saucy.OtherGames;

internal static class SliceIsRightModule
{
    public static bool ModuleEnabled
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                RunModule();
            }
            else
            {
                field = value;
            }

            Saucy.Config.SliceIsRightModuleEnabled = field;
        }
    }

    private static bool IsInGoldSaucer => Svc.ClientState.TerritoryType == 144;

    private const float HalfPi = 1.57079637f;
    private const float MaxDistance = 30f;

    private static readonly uint ColourBlue = ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.0f, 1f, 0.15f)));
    private static readonly uint ColourGreen = ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 1f, 0.0f, 0.15f)));
    private static readonly uint ColourRed = ImGui.GetColorU32(ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.0f, 0.0f, 0.4f)));
    private static readonly IDictionary<ulong, DateTime> ObjectsAndSpawnTime = new Dictionary<ulong, DateTime>();

    public static void Initialize()
    {
        ModuleEnabled = Saucy.Config.SliceIsRightModuleEnabled;
    }

    private static void RunModule()
    {
        if (ModuleEnabled)
            Svc.PluginInterface.UiBuilder.Draw += DrawUI;
        else
            Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
    }

    private static void DrawUI()
    {
        if (!Svc.ClientState.IsLoggedIn || !IsInGoldSaucer) return;

        foreach (var gameObject in Svc.Objects)
        {
            if (!(DistanceToPlayer(gameObject.Position) <= MaxDistance)) continue;

            var model = Marshal.ReadInt32(gameObject.Address + 128);
            if (gameObject.ObjectKind == ObjectKind.EventObj && model is >= 2010777 and <= 2010779)
            {
                RenderObject(gameObject, model);
            }
        }
    }

    private static float DistanceToPlayer(Vector3 center)
    {
        var localPlayer = Svc.ClientState.LocalPlayer;
        return Vector3.Distance(localPlayer != null ? localPlayer.Position : Vector3.Zero, center);
    }

    private static void RenderObject(IGameObject gameObject, int model, float? radius = null)
    {
        if (ObjectsAndSpawnTime.TryGetValue(gameObject.GameObjectId, out var dateTime))
        {
            if (dateTime.AddSeconds(5) > DateTime.Now) return;

            float length;
            switch (model)
            {
                case 2010777:
                    length = (float)((double?)radius ?? 25.0);
                    DrawRectWorld(gameObject, gameObject.Rotation + HalfPi, length, 5f,
                        ColourBlue);
                    break;
                case 2010778:
                    length = (float)((double?)radius ?? 25.0);
                    var rotation1 = (float)(gameObject.Rotation + 1.5707963705062866);
                    var rotation2 = (float)(gameObject.Rotation - 1.5707963705062866);
                    DrawRectWorld(gameObject, rotation1, length, 5f, ColourGreen);
                    DrawRectWorld(gameObject, rotation2, length, 5f, ColourGreen);
                    break;
                case 2010779:
                    length = (float)((double?)radius ?? 11.0);
                    DrawFilledCircleWorld(gameObject, length, ColourRed);
                    break;
            }
        }
        else
        {
            ObjectsAndSpawnTime.Add(gameObject.GameObjectId, DateTime.Now);
        }
    }

    private static void DrawRectWorld(IGameObject gameObject, float rotation, float length, float width, uint colour)
    {
        BeginRender(gameObject.Address + gameObject.Rotation.ToString(CultureInfo.InvariantCulture));
        var position = gameObject.Position;
        var io = ImGui.GetIO();
        var vector21 = io.DisplaySize;
        var vector31 = new Vector3(position.X + width / 2f * (float)Math.Sin(1.5707963705062866 + rotation), position.Y, position.Z + width / 2f * (float)Math.Cos(1.5707963705062866 + rotation));
        var vector32 = new Vector3(position.X + width / 2f * (float)Math.Sin(rotation - 1.5707963705062866), position.Y, position.Z + width / 2f * (float)Math.Cos(rotation - 1.5707963705062866));
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
                vector35,
                vector36,
                vector34,
                vector31,
                vector33,
                vector32
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

        EndRender();
    }

    private static void DrawFilledCircleWorld(IGameObject gameObject, float radius, uint colour)
    {
        BeginRender(gameObject.Address.ToString());
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

        EndRender();
    }

    private static void BeginRender(string name)
    {
        ImGui.PushID("sliceWindowI" + name);
        ImGui.PushStyleVar((ImGuiStyleVar)1, Vector2.Zero);
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(Vector2.Zero, ImGuiCond.None, Vector2.Zero);
        ImGui.Begin("sliceWindow" + name, (ImGuiWindowFlags)787337);
        var io = ImGui.GetIO();
        ImGui.SetWindowSize(io.DisplaySize);
    }

    private static void EndRender()
    {
        ImGui.End();
        ImGui.PopStyleVar();
        ImGui.PopID();
    }
}
