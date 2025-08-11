//using Dalamud.Bindings.ImGui;
//using Dalamud.Interface.Utility.Raii;
//using Dalamud.Interface.Windowing;
//using ECommons.GameHelpers;
//using ECommons.ImGuiMethods;
//using ECommons.SimpleGui;
//using FFXIVClientStructs.FFXIV.Client.Game.GoldSaucer;
//using Saucy.Framework;
//using System.Linq;
//using System.Numerics;

//namespace Saucy.OtherGames;
//public class AnyWayTheWindBlows : Module
//{
//    // from https://github.com/img02/Fungah-Totally-Safe-Spot/
//    public override string Name => "Any Way the Wind Blows";
//    public override bool IsEnabled => C.AnyWayTheWindowBlowsModuleEnabled;

//    public override void Enable() => EzConfigGui.WindowSystem.AddWindow(new Dot());
//    public override void Disable() => EzConfigGui.RemoveWindow<Dot>();

//    public class Stage
//    {
//        public const float North = -50.76f;
//        public const float South = -21f;
//        public const float East = 85.45f;
//        public const float West = 55.6f;
//        public static SafeSpotWrapper SafeSpot => new(66.96f, -4.48f, -24.69f);

//        public class SafeSpotWrapper
//        {
//            public SafeSpotWrapper(Vector3 position) => Position = position;
//            public SafeSpotWrapper(float x, float y, float z) => Position = new(x, y, z);
//            public Vector3 Position { get; private set; }
//            public bool On => Player.DistanceTo(Position) < 0.00025;
//            public bool Near => Player.DistanceTo(Position) < 0.05;
//        }

//        public static bool PlayerOnStage => Player.Position.X is > West and < East && Player.Position.Z is < South and > North;
//        public static bool FungahPresent => Svc.Objects.Any(o => o.DataId == 1010476);
//    }

//    public class Dot : Window
//    {
//        public Dot() : base($"{nameof(AnyWayTheWindBlows)}.{nameof(Dot)}")
//            => Flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoInputs;

//        public override unsafe bool DrawConditions()
//        {
//            var mgr = GoldSaucerManager.Instance();
//            if (mgr is null) return false;
//            var dir = mgr->CurrentGFateDirector;
//            return dir is not null && dir->GateType is 5 && dir->Flags.HasFlag(GFateDirectorFlag.IsJoined) && !dir->Flags.HasFlag(GFateDirectorFlag.IsFinished);
//        }

//        public override void Draw()
//        {
//            if (Svc.GameGui.WorldToScreen(Stage.SafeSpot.Position, out var pos))
//            {
//                Position = new Vector2(pos.X - 15, pos.Y - 15);
//                ImGui.GetWindowDrawList().AddCircleFilled(pos, 5f, Stage.SafeSpot.On ? EzColor.Green : EzColor.Red);
//                if (!Stage.SafeSpot.On && Stage.SafeSpot.Near)
//                {
//                    ImGui.SetCursorPosY(24f);
//                    using var child = ImRaii.Child("GuideText", new Vector2(80f, 20f) * ImGuiHelpers.GlobalScale);
//                    using var _ = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0.8f));
//                    ImGui.SetCursorPosX(4f * ImGuiHelpers.GlobalScale);

//                    if (Player.Position.X - Stage.SafeSpot.Position.X > 0.015) ImGui.Text("move left");
//                    else if (Stage.SafeSpot.Position.X - Player.Position.X > 0.015) ImGui.Text("move right");
//                    else if (Player.Position.Z < Stage.SafeSpot.Position.Z) ImGui.Text("move down");
//                    else if (Player.Position.Z > Stage.SafeSpot.Position.Z) ImGui.Text("move up");
//                }
//            }
//        }
//    }
//}
