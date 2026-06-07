using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using Saucy.Framework;
using System.Numerics;
namespace Saucy.OtherGames;

public class AnyWayTheWindBlows : Module
{
    public override string Name => "Any Way the Wind Blows";

    public override void Enable()
    {
        Svc.Framework.Update += OnUpdate;
        Svc.PluginInterface.UiBuilder.Draw += Draw;
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        WindBlowsGateMovement.ReleaseIfOwned();
    }

    private void OnUpdate(IFramework _)
    {
        if (!InSaucer || !PlayerOnStage || CurrentGate is not GateType.AnyWayTheWindBlows)
        {
            WindBlowsGateMovement.ReleaseIfOwned();
            return;
        }

        if (Stage.SafeSpot.On)
        {
            WindBlowsGateMovement.ReleaseIfOwned();
            return;
        }

        if (!C.GoldSaucerGates.WindBlowsAutoMovement)
        {
            WindBlowsGateMovement.ReleaseIfOwned();
            return;
        }

        WindBlowsGateMovement.TryMoveTo(Stage.SafeSpot.Position);
    }

    public void Draw()
    {
        if (!InSaucer || !PlayerOnStage || CurrentGate is not GateType.AnyWayTheWindBlows)
        {
            return;
        }

        if (Svc.GameGui.WorldToScreen(Stage.SafeSpot.Position, out var pos))
        {
            ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new(pos.X - 15, pos.Y - 15));
            ImGui.SetNextWindowSize(new Vector2(90, 50) * ImGuiHelpers.GlobalScale);
            if (ImGui.Begin("Pointer", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoInputs))
            {
                ImGui.GetWindowDrawList().AddCircleFilled(pos, 5f, Stage.SafeSpot.On ? EzColor.Green : EzColor.Red);
                if (!Stage.SafeSpot.On && Stage.SafeSpot.Near)
                {
                    ImGui.SetCursorPosY(24f);
                    using var child = ImRaii.Child("GuideText", new Vector2(80f, 20f) * ImGuiHelpers.GlobalScale);
                    using var _ = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0.8f));
                    ImGui.SetCursorPosX(4f * ImGuiHelpers.GlobalScale);

                    if (Player.Position.X - Stage.SafeSpot.Position.X > 0.015)
                    {
                        ImGui.Text("move left");
                    }
                    else if (Stage.SafeSpot.Position.X - Player.Position.X > 0.015)
                    {
                        ImGui.Text("move right");
                    }
                    else if (Player.Position.Z < Stage.SafeSpot.Position.Z)
                    {
                        ImGui.Text("move down");
                    }
                    else if (Player.Position.Z > Stage.SafeSpot.Position.Z)
                    {
                        ImGui.Text("move up");
                    }
                }

                ImGui.End();
            }
        }
    }

    public class Stage
    {
        public static SafeSpotWrapper SafeSpot => new(66.96f, -4.48f, -24.69f);

        public class SafeSpotWrapper
        {
            public SafeSpotWrapper(Vector3 position) => Position = position;
            public SafeSpotWrapper(float x, float y, float z) => Position = new(x, y, z);
            public Vector3 Position { get; }
            public bool On => Player.DistanceTo(Position) < 0.00025;
            public bool Near => Player.DistanceTo(Position) < 0.05;
        }
    }
}
