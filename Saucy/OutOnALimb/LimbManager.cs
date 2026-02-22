using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Memory;
using ECommons.Automation.UIInput;
using ECommons.EzEventManager;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Reflection;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using static ECommons.GenericHelpers;

namespace Saucy.OutOnALimb;
public unsafe class LimbManager : IDisposable
{
    private uint OldState = 0;
    private static readonly int[] StartingPoints = [20, 50, 80];
    private int RequestInput = 0;
    private int? Request = null;
    private bool OnlyRequest = false;
    private readonly List<HitResult> Results = [];
    private int? Next = null;
    private int MinIndex = 0;
    private bool RecordMinIndex = false;
    public int GamesToPlay = 0;
    public LimbConfig Cfg;
    private bool Exit = false;

    private static bool TidyChat => DalamudReflector.TryGetDalamudPlugin("TidyChat", out var _, false, true);

    public LimbManager(LimbConfig conf)
    {
        Cfg = conf;
        new EzFrameworkUpdate(Tick);
        Svc.Chat.ChatMessageHandled += Chat_ChatMessage;
        Svc.Chat.ChatMessageUnhandled += Chat_ChatMessage;
    }

    public void Dispose()
    {
        Svc.Chat.ChatMessageHandled -= Chat_ChatMessage;
        Svc.Chat.ChatMessageUnhandled -= Chat_ChatMessage;
    }

    private void InteractWithClosestLimb()
    {
        if (Svc.Condition[ConditionFlag.WaitingForDutyFinder])
        {
            Exit = true;
        }
        if (Exit)
        {
            GamesToPlay = 0;
        }
        if (IsOccupied())
        {
            EzThrottler.Throttle("InteractPause", 1000, true);
        }
        if (!EzThrottler.Check("InteractPause")) return;

        //2005423	Out on a Limb	0	Out on a Limb machines	0	1	1	0	0
        var machineNameGS = Svc.Data.GetExcelSheet<EObjName>().GetRow(2005423).Singular.GetText().RemoveSpaces();
        //30425	Out on a Limb machine	0	Out on a Limb machines	0	1	1	0	0	Experience the heart-exploding excitement of the Gold Saucer in your own home with this authentic Out on a Limb machine.	Out on a Limb Machine	ui/icon/052000/052680.tex	1	1	14	Out on a Limb Machine	Furnishing		EquipSlotCategory#0	125	18740	1	False	True	False	False	2	0	False	False	False	ItemAction#0	2	0	adventurer	ItemRepairResource#0		0	False	False	0	1	0	0		None		0	0, 0, 0, 0	0, 0, 0, 0	adventurer	0	0	0	0	0	0	0	0	0		0		0		0		0		0		0		0		0		0		0		0		0		0	0	0	False	False	0	False
        var machineNameHousing = Svc.Data.GetExcelSheet<Item>().GetRow(30425).Singular.GetText().RemoveSpaces();

        var found = false;
        foreach (var x in Svc.Objects)
        {
            if (x.ObjectKind.EqualsAny(ObjectKind.EventObj, ObjectKind.Housing) && x.Name.GetText().RemoveSpaces().EqualsIgnoreCaseAny(machineNameGS, machineNameHousing) && Vector3.Distance(Player.Object.Position, x.Position) < 4)
            {
                found = true;
                if (EzThrottler.Throttle("TargetAndInteract"))
                {
                    if (Svc.Targets.Target?.Address == x.Address)
                    {
                        TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)x.Address, false);
                        EzThrottler.Throttle("TargetAndInteract", 10000, true);
                        GamesToPlay--;
                    }
                    else
                    {
                        Svc.Targets.Target = x;
                    }
                }
            }
        }
        if (!found)
        {
            GamesToPlay = 0;
        }
    }

    private readonly Dictionary<string, HitPower> HitPowerText = new()
    {
        [Svc.Data.GetExcelSheet<Addon>().GetRow(9710).Text.GetText().RemoveSpaces()] = HitPower.Nothing,
        [Svc.Data.GetExcelSheet<Addon>().GetRow(9711).Text.GetText().RemoveSpaces()] = HitPower.Weak,
        [Svc.Data.GetExcelSheet<Addon>().GetRow(9712).Text.GetText().RemoveSpaces()] = HitPower.Strong,
        [Svc.Data.GetExcelSheet<Addon>().GetRow(9713).Text.GetText().RemoveSpaces()] = HitPower.Maximum,
    };

    private void Chat_ChatMessage(XivChatType type, int senderId, SeString sender, SeString message)
    {
        if (!Cfg.EnableLimb) return;
        if (!Svc.Condition[ConditionFlag.OccupiedInQuestEvent]) return;
        PluginLog.Information($"{type}/{message.GetText().RemoveSpaces()}");
        if ((int)type == 2105)
        {
            var s = message.GetText().RemoveSpaces();
            if (HitPowerText.TryGetValue(s, out var hitPower))
            {
                Record(hitPower);
            }
        }
    }

    private void Reset()
    {
        Results.Clear();
        for (var i = 0; i <= 100; i += Cfg.Step)
        {
            Results.Add(new(i, HitPower.Unobserved));
        }
        Next = null;
        MinIndex = 0;
        RecordMinIndex = false;
    }

    private int GetCursor()
    {
        const float Min = -0.733f;
        const float Max = 0.733f;
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var cursorFloat = addon->GetNodeById(17)->Rotation;
            cursorFloat -= Min;
            cursorFloat /= Max - Min;
            cursorFloat *= 100;
            return (int)Math.Round(cursorFloat);
        }
        return 0;
    }

    private bool SafeClickButtonAimg()
    {
        var ret = false;
        if (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var addon) && IsAddonReady(addon))
        {
            var reader = new ReaderMiniGameBotanist(addon);
            var button = addon->GetComponentButtonById(37);
            if (button->IsEnabled)
            {
                if (EzThrottler.Throttle("ClickAimgGameButton", 20000))
                {
                    ret = true;
                    button->ClickAddonButton(addon);
                }
            }
        }
        return ret;
    }

    private bool SafeClickButtonBotanist()
    {
        var ret = false;
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var reader = new ReaderMiniGameBotanist(addon);
            var button = addon->GetComponentButtonById(24);
            if (button->IsEnabled && reader.State == 3)
            {
                if (EzThrottler.Throttle("ClickBtnGameButton", 2000))
                {
                    ret = true;
                    button->ClickAddonButton(addon);
                }
            }
        }
        return ret;
    }

    private void Tick()
    {
        if (!Cfg.EnableLimb) return;
        if (!Player.Available) return;
        if (!IsScreenReady()) return;
        if (GamesToPlay > 0)
        {
            InteractWithClosestLimb();
        }
        if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            {
                if (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var addon) && IsAddonReady(addon))
                {
                    if (TryGetAddonByName<AddonSelectString>("SelectString", out var ss) && IsAddonReady(&ss->AtkUnitBase))
                    {
                        var text = MemoryHelper.ReadSeString(&ss->AtkUnitBase.GetTextNodeById(2)->NodeText).GetText().RemoveSpaces();
                        if (text.Contains(Svc.Data.GetExcelSheet<Addon>().GetRow(9994).Text.GetText().RemoveSpaces(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (EzThrottler.Throttle("ConfirmPlay"))
                            {
                                new AddonMaster.SelectString(ss).Entries[0].Select();
                            }
                        }
                    }

                    var reference = addon->GetNodeById(NodeIDs[Cfg.LimbDifficulty]);
                    var cursor = addon->GetNodeById(39);
                    var iCursor = 400 - cursor->Height;
                    if (iCursor > reference->Y && iCursor < reference->Y + Heights[Cfg.LimbDifficulty])
                    {
                        SafeClickButtonAimg();
                    }
                }
            }
            HandleYesno();
            {
                if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
                {
                    var reader = new ReaderMiniGameBotanist(addon);
                    var button = addon->GetComponentButtonById(24);
                    var cursor = GetCursor();

                    if (reader.State == 3)
                    {
                        if (OldState != 3)
                        {
                            if (reader.SwingsLeft == 10)
                            {
                                PluginLog.Debug($"Out on a limb - GAME RESET");
                                Reset();
                            }
                            PluginLog.Debug($"Out on a limb - turn start event");
                            Next = GetNextTargetCursorPos();
                        }
                        if (OnlyRequest)
                        {
                            if (Request != null)
                            {
                                if (Math.Abs(cursor - Request.Value) < Cfg.Tolerance)
                                {
                                    if (SafeClickButtonBotanist()) Request = null;
                                }
                            }
                        }
                        else
                        {
                            if (Next != null)
                            {
                                if (Math.Abs(cursor - Next.Value) < Cfg.Tolerance)
                                {
                                    if (SafeClickButtonBotanist()) Next = null;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (OldState == 3)
                        {
                            PluginLog.Debug($"Out on a limb - turn finish event");
                        }
                    }
                    OldState = reader.State;
                }
                else
                {
                    Exit = false;
                }
            }
        }
    }

    private void HandleYesno()
    {
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var reader = new ReaderMiniGameBotanist(addon);
            if (TryGetAddonByName<AddonSelectYesno>("SelectYesno", out var ss) && IsAddonReady(&ss->AtkUnitBase))
            {
                var text = MemoryHelper.ReadSeString(&ss->PromptText->NodeText).GetText();
                var matches = new Regex(Svc.ClientState.ClientLanguage switch
                {
                    ClientLanguage.English => @"Current payout: ([0-9]+)",
                    ClientLanguage.French => @"Gain de PGS en cas de réussite : ([0-9]+)",
                    ClientLanguage.German => @"Momentaner Gewinn: ([0-9]+)",
                    ClientLanguage.Japanese => @"MGP.([0-9]+)",
                    _ => throw new ArgumentOutOfRangeException(nameof(Svc.ClientState.ClientLanguage))
                }).Match(text);
                if (matches.Success)
                {
                    var mgp = int.Parse(matches.Groups[1].Value);
                    if (Exit)
                    {
                        if (EzThrottler.Throttle("Yesno", 2000)) new AddonMaster.SelectYesno(ss).No();
                    }
                    else
                    {
                        if (mgp >= 400)
                        {
                            if (reader.SecondsRemaining > Cfg.StopAt)
                            {
                                if (EzThrottler.Throttle("Yesno", 2000)) new AddonMaster.SelectYesno(ss).Yes();
                            }
                            else
                            {
                                if (EzThrottler.Throttle("Yesno", 2000)) new AddonMaster.SelectYesno(ss).No();
                            }
                        }
                        else
                        {
                            if (reader.SecondsRemaining > Cfg.HardStopAt)
                            {
                                if (EzThrottler.Throttle("Yesno", 2000)) new AddonMaster.SelectYesno(ss).Yes();
                            }
                            else
                            {
                                if (EzThrottler.Throttle("Yesno", 2000)) new AddonMaster.SelectYesno(ss).No();
                            }
                        }
                    }
                }
            }
        }
    }

    private List<HitResult> GetNext(int index, uint num)
    {
        var ret = new List<HitResult>();
        for (var i = 0; i < num; i++)
        {
            var r = Results.SafeSelect(index + i);
            if (r != null) ret.Add(r);
        }
        return ret;
    }

    private List<HitResult> GetPrev(int index, uint num)
    {
        var ret = new List<HitResult>();
        for (var i = 0; i < num; i++)
        {
            var r = Results.SafeSelect(index - i);
            if (r != null) ret.Add(r);
        }
        return ret;
    }

    private int GetNextTargetCursorPos()
    {
        for (var i = MinIndex; i < Results.Count; i++)
        {
            var current = Results[i];
            var prev = Results.SafeSelect(i - 1);
            var next = Results.SafeSelect(i + 1);
            if (current.Power == HitPower.Strong)
            {
                return current.Position;
            }
        }

        for (var i = MinIndex; i < Results.Count; i++)
        {
            var current = Results[i];
            var prev = Results.SafeSelect(i - 1);
            var next = Results.SafeSelect(i + 1);
            if (current.Power == HitPower.Weak)
            {
                if (prev?.Power == HitPower.Unobserved && i - 1 >= MinIndex) return prev.Position;
                if (next?.Power == HitPower.Unobserved) return next.Position;
            }
        }
        foreach (var x in StartingPoints)
        {
            int[] adjustedPoints = [.. StartingPoints.Where(z => !IsStartingPointChecked(z))];
            if (adjustedPoints.Length == 0) break;
            var transformedPoints = adjustedPoints.Select(z => GetClosestResultPoint(z).Position).ToArray();
            var index = 0;// Random.Shared.Next(transformedPoints.Length);
            PluginLog.Debug($"Returning starting point {adjustedPoints[index]}->{transformedPoints[index]}");
            if (StartingPoints.Length != transformedPoints.Length) RecordMinIndex = true;
            return transformedPoints[index];
        }
        MinIndex = 0;
        var unobserveds = Results.Where(x => x.Power == HitPower.Unobserved).ToArray();
        if (unobserveds.Length == 0)
        {
            PluginLog.Error("No more results");
            return -100;
        }
        var res = unobserveds[Random.Shared.Next(unobserveds.Length)].Position;
        PluginLog.Debug($"Returning random unobserved point {res}");
        return res;
    }

    private HitResult GetClosestResultPoint(int point)
    {
        return Results.OrderBy(x => Math.Abs(point - x.Position)).First();
    }

    private bool IsStartingPointChecked(int position)
    {
        var item = GetClosestResultPoint(position);
        return item.Power != HitPower.Unobserved;
    }

    private void Record(HitPower result)
    {
        if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
        {
            var reader = new ReaderMiniGameBotanist(addon);
            var cursor = GetCursor();
            var item = Results.OrderBy(x => Math.Abs(x.Position - cursor)).First();
            if (RecordMinIndex)
            {
                RecordMinIndex = false;
                MinIndex = Results.IndexOf(item);
            }
            if (result < item.Power)
            {
                MinIndex = 0;
                RecordMinIndex = false;
            }
            item.Power = result;
            PluginLog.Debug($"{result}");
        }
    }

    private readonly Dictionary<LimbDifficulty, int[]> FPSRequirements = new()
    {
        [LimbDifficulty.Titan] = [480, 240, 120, 90, 60],
        [LimbDifficulty.Morbol] = [240, 120, 90, 60, 30],
        [LimbDifficulty.Cactuar] = [120, 90, 60, 30, 15],
    };
    private int CalcRequiredFPS()
    {
        return FPSRequirements.SafeSelect(Cfg.LimbDifficulty)?.SafeSelect(Cfg.Tolerance) ?? -1;
    }

    public void DrawSettings()
    {
        var save = false;
        ImGuiEx.TextWrapped($"How to use: enable module, walk up to the Out on a Limb machine in Gold Saucer, input number of games you want to play to play automatically or access the machine manually to play one game.");
        if (TidyChat)
            ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, $@"Tidychat Warning: Please ensure you do not have ""You sense something..."" messages (Advanced -> System messages) hidden or this will not work");
        ImGui.Separator();
        save |= ImGui.Checkbox($"Enable", ref Cfg.EnableLimb);
        ImGui.SetNextItemWidth(100f);
        ImGui.InputInt("Games to play", ref GamesToPlay.ValidateRange(0, 9999));
        ImGui.SameLine();
        if (ImGui.Button("Max")) GamesToPlay = 9999;
        ImGui.Checkbox($"Stop at next double down", ref Exit);

        ImGui.Separator();
        ImGui.SetNextItemWidth(100f);
        save |= ImGuiEx.EnumCombo("Difficulty", ref Cfg.LimbDifficulty);
        ImGui.SetNextItemWidth(100f);
        save |= ImGuiEx.SliderInt($"Tolerance", ref Cfg.Tolerance.ValidateRange(1, 4), 1, 4);
        ImGui.SameLine();
        if (ImGui.Button("Default##1")) Cfg.Tolerance = new LimbConfig().Tolerance;
        var req = CalcRequiredFPS();
        var current = ImGui.GetIO().Framerate;
        var delta = current - req;
        ImGuiEx.TextWrapped(delta > -1 ? ImGuiColors.ParsedGreen : (delta > -(req * 0.15f) ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed), $"Required framerate: {req}\nYour framerate: {(int)current}");
        ImGuiEx.TextWrapped($"Reducing tolerance or difficulty will reduce required framerate.");
        ImGui.SetNextItemWidth(100f);
        save |= ImGui.DragInt($"Step", ref Cfg.Step, 0.05f);
        ImGui.SameLine();
        if (ImGui.Button("Default##2")) Cfg.Step = new LimbConfig().Step;
        ImGui.SetNextItemWidth(100f);
        save |= ImGui.DragInt($"Stop at remaining time with big win", ref Cfg.StopAt, 0.5f);
        ImGui.SetNextItemWidth(100f);
        save |= ImGui.DragInt($"Stop at remaining time with little win", ref Cfg.HardStopAt, 0.5f);

        if (save) C.Save();
    }

    private static readonly Dictionary<LimbDifficulty, int> Heights = new()
    {
        [LimbDifficulty.Titan] = 20,
        [LimbDifficulty.Morbol] = 40,
        [LimbDifficulty.Cactuar] = 340,
    };
    private static readonly Dictionary<LimbDifficulty, uint> NodeIDs = new()
    {
        [LimbDifficulty.Titan] = 41,
        [LimbDifficulty.Morbol] = 44,
        [LimbDifficulty.Cactuar] = 47,
    };
    public void DrawDebug()
    {
        {
            if (TryGetAddonByName<AtkUnitBase>("MiniGameAimg", out var addon) && IsAddonReady(addon))
            {
                //41 titan, 44 morbol, 47 cactuar
                var reference = addon->GetNodeById(NodeIDs[Cfg.LimbDifficulty]);
                var cursor = addon->GetNodeById(39);
                var iCursor = 400 - cursor->Height;
                if (iCursor > reference->Y && iCursor < reference->Y + Heights[Cfg.LimbDifficulty]) ImGuiEx.Text($"Yes");
                ImGuiEx.Text($"Reference: {reference->Y}");
                ImGuiEx.Text($"Cursor: {cursor->Height}");
            }
        }
        {
            if (TryGetAddonByName<AtkUnitBase>("MiniGameBotanist", out var addon) && IsAddonReady(addon))
            {
                var reader = new ReaderMiniGameBotanist(addon);
                var button = addon->GetComponentButtonById(24);
                var cursor = GetCursor();
                ImGuiEx.Text($"Cursor: {cursor}");
                ImGui.Checkbox("Only request", ref OnlyRequest);
                ImGui.SetNextItemWidth(100f);
                ImGui.InputInt("Request input", ref RequestInput);
                ImGui.SameLine();
                if (ImGui.Button("Request")) Request = RequestInput;
                ImGui.SameLine();
                if (ImGui.Button("Reset")) Request = null;
                ImGuiEx.Text($"Button enabled: {button->IsEnabled}");
                ImGuiEx.Text($"Seconds remaining: {reader.SecondsRemaining}");
                if (ImGui.Button("Click"))
                {
                    if (button->IsEnabled)
                    {
                        button->ClickAddonButton(addon);
                    }
                }
                ImGuiEx.Text($"Next: {Next}, MinIndex: {MinIndex}, rec={RecordMinIndex}");
                ImGuiEx.Text($"Starting points:\n{StartingPoints.Print(", ")}");
                ImGuiEx.Text($"Results:\n{Results.Select(x => $"{x.Position}={x.Power}").Print("\n")}");
            }
        }
    }
}
