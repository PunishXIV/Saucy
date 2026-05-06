using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using FFTriadBuddy;
using FFXIVClientStructs.FFXIV.Client.Game.GoldSaucer;
using PunishLib.ImGuiMethods;
using Saucy.CuffACur;
using Saucy.OtherGames;
using Saucy.TripleTriad;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using TriadBuddyPlugin;
using static Saucy.UiText;

namespace Saucy;

// It is good to have this be disposable in general, in case you ever need it
// to do any cleanup
public unsafe class PluginUI : Window
{
    public PluginUI() : base("Saucy##Saucy")
    {
        Size = new Vector2(520, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public GameNpcInfo CurrentNPC
    {
        get;
        set
        {
            if (field != value)
            {
                TriadAutomater.TempCardsWonList.Clear();
                field = value;
            }
        }
    }

    public bool Enabled { get; set; } = false;

    public override void Draw()
    {
        ImGuiEx.EzTabBar("###Games",
            (GameCuff, DrawCufTab, null, false),
            (GameTriad, DrawTriadTab, null, false),
            (GameLimb, () =>
            {
                ImGuiEx.EzTabBar(GameLimb,
                    (T("Main", "主要"), P.LimbManager.DrawSettings, null, false),
                    (T("Debug", "调试"), P.LimbManager.DrawDebug, null, false));
            }, null, false),
            (T("Other Games", "其他游戏"), DrawOtherGamesTab, null, false),
            (T("Stats", "统计"), DrawStatsTab, null, false),
            (T("About", "关于"), () => AboutTab.Draw("Saucy"), null, false)
#if DEBUG
            , (T("Debug", "调试"), DrawDebugTab, null, false)
#endif
            );
    }

    private void DrawOtherGamesTab()
    {
        //ImGui.Checkbox("Enable Air Force One Module", ref AirForceOneModule.ModuleEnabled);

        if (ImGui.Checkbox(T("Enable Slice is Right Module", $"启用{GameSlice}模块"), ref C.SliceIsRightModuleEnabled))
        {
            if (C.SliceIsRightModuleEnabled)
                C.EnabledModules.Add(ModuleManager.GetModule<SliceIsRight>().InternalName);
            else
                C.EnabledModules.Remove(ModuleManager.GetModule<SliceIsRight>().InternalName);
        }

        if (ImGui.Checkbox(T("Enable Auto Mini-Cactpot", $"启用自动{GameMiniCactpot}"), ref C.EnableAutoMiniCactpot))
        {
            if (C.EnableAutoMiniCactpot)
                C.EnabledModules.Add(ModuleManager.GetModule<MiniCactpot.MiniCactpot>().InternalName);
            else
                C.EnabledModules.Remove(ModuleManager.GetModule<MiniCactpot.MiniCactpot>().InternalName);
        }

        if (ImGui.Checkbox(T("Enable Any Way the Wind Blows Module", $"启用{GameWind}模块"), ref C.AnyWayTheWindowBlowsModuleEnabled))
        {
            if (C.AnyWayTheWindowBlowsModuleEnabled)
                C.EnabledModules.Add(ModuleManager.GetModule<AnyWayTheWindBlows>().InternalName);
            else
                C.EnabledModules.Remove(ModuleManager.GetModule<AnyWayTheWindBlows>().InternalName);
        }
    }

    private void DrawStatsTab()
    {
        if (ImGui.BeginTabBar(T("Stats", "统计")))
        {
            if (ImGui.BeginTabItem(T("Lifetime", "累计")))
            {
                DrawStatsTab(C.Stats, out var reset);

                if (reset)
                {
                    C.Stats = new();
                    C.Save();
                }

                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(T("Session", "本次会话")))
            {
                DrawStatsTab(C.SessionStats, out var reset);
                if (reset)
                    C.SessionStats = new();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawStatsTab(Stats stat, out bool reset)
    {
        if (ImGui.BeginTabBar(T("Games", "游戏")))
        {
            if (ImGui.BeginTabItem(GameCuff))
            {
                DrawCuffStats(stat);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(GameTriad))
            {
                DrawTTStats(stat);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(GameLimb))
            {
                DrawLimbStats(stat);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        reset = ImGui.Button(T("RESET STATS (Hold Ctrl)", "重置统计（按住Ctrl）"), new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y)) && ImGui.GetIO().KeyCtrl;
    }

    private void DrawLimbStats(Stats stat)
    {
        ImGui.BeginChild("Limb Stats", new Vector2(0, ImGui.GetContentRegionAvail().Y - 30f), true);
        ImGui.Columns(3, default, false);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(ImGuiColors.DalamudRed, GameLimb, true);
        ImGuiHelpers.ScaledDummy(10f);
        ImGui.Columns(2, default, false);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Games Played", "游戏次数"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("MGP Won", "获得的MGP"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.LimbGamesPlayed:N0}");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.LimbMGP:N0}");

        ImGui.EndChild();
    }

    private void DrawCuffStats(Stats stat)
    {
        ImGui.BeginChild("Cuff Stats", new Vector2(0, ImGui.GetContentRegionAvail().Y - 30f), true);
        ImGui.Columns(3, default, false);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(ImGuiColors.DalamudRed, GameCuff, true);
        ImGuiHelpers.ScaledDummy(10f);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Games Played", "游戏次数"), true);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.CuffGamesPlayed:N0}");
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.Spacing();
        ImGuiEx.CenterColumnText(T("BRUISING!", "击打！"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("PUNISHING!!", "痛击！！"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("BRUTAL!!!!", "暴揍！！！！"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.CuffBruisings:N0}");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.CuffPunishings:N0}");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.CuffBrutals:N0}");
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("MGP Won", "获得的MGP"), true);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.CuffMGP:N0}");

        ImGui.EndChild();
    }

    private void DrawTTStats(Stats stat)
    {
        ImGui.BeginChild("TT Stats", new Vector2(0, ImGui.GetContentRegionAvail().Y - 30f), true);
        ImGui.Columns(3, default, false);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(ImGuiColors.DalamudRed, GameTriad, true);
        ImGuiHelpers.ScaledDummy(10f);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Games Played", "游戏次数"), true);
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.GamesPlayedWithSaucy:N0}");
        ImGui.NextColumn();
        ImGui.NextColumn();
        ImGui.Spacing();
        ImGuiEx.CenterColumnText(T("Wins", "胜利"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Losses", "失败"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Draws", "平局"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.GamesWonWithSaucy:N0}");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.GamesLostWithSaucy:N0}");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.GamesDrawnWithSaucy:N0}");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Win Rate", "胜率"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Cards Won", "获得卡片"), true);
        ImGui.NextColumn();
        if (stat.NPCsPlayed.Count > 0)
        {
            ImGuiEx.CenterColumnText(T("Most Played NPC", "挑战次数最多的NPC"), true);
            ImGui.NextColumn();
        }
        else
        {
            ImGui.NextColumn();
        }

        if (stat.GamesPlayedWithSaucy > 0)
        {
            ImGuiEx.CenterColumnText($"{Math.Round((stat.GamesWonWithSaucy / (double)stat.GamesPlayedWithSaucy) * 100, 2)}%");
        }
        else
        {
            ImGuiEx.CenterColumnText("");
        }
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.CardsDroppedWithSaucy:N0}");
        ImGui.NextColumn();

        if (stat.NPCsPlayed.Count > 0)
        {
            ImGuiEx.CenterColumnText($"{stat.NPCsPlayed.OrderByDescending(x => x.Value).First().Key}");
            ImGuiEx.CenterColumnText($"{stat.NPCsPlayed.OrderByDescending(x => x.Value).First().Value:N0} {T("times", "次")}");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
        }

        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("MGP Won", "获得的MGP"), true);
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText(T("Total Card Drop Value", "卡片掉落总价值"), true);
        ImGui.NextColumn();
        if (stat.CardsWon.Count > 0)
        {
            ImGuiEx.CenterColumnText(T("Most Won Card", "获得最多的卡片"), true);
        }
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{stat.MGPWon:N0} MGP");
        ImGui.NextColumn();
        ImGuiEx.CenterColumnText($"{GetDroppedCardValues(stat):N0} MGP");
        ImGui.NextColumn();
        if (stat.CardsWon.Count > 0)
        {
            ImGuiEx.CenterColumnText($"{TriadCardDB.Get().FindById((int)stat.CardsWon.OrderByDescending(x => x.Value).First().Key).Name.GetLocalized()}");
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGui.NextColumn();
            ImGuiEx.CenterColumnText($"{stat.CardsWon.OrderByDescending(x => x.Value).First().Value:N0} {T("times", "次")}");
        }

        ImGui.Columns(1);
        ImGui.EndChild();
    }

    private int GetDroppedCardValues(Stats stat)
    {
        var output = 0;
        foreach (var card in stat.CardsWon)
            output += GameCardDB.Get().FindById((int)card.Key).SaleValue * stat.CardsWon[card.Key];

        return output;
    }

    public void DrawTriadTab()
    {
        var enabled = TriadAutomater.ModuleEnabled;

        ImGui.TextWrapped(T(
            @"How to use: Challenge an NPC you wish to play cards with. Once you have initiated the challenge, click ""Enable Triad Module"".",
            $"使用方法：向想要对战的NPC发起{GameTriad}挑战。开始挑战后，点击“启用{GameTriad}模块”。"));
        ImGui.Separator();

        if (ImGui.Checkbox(T("Enable Triad Module", $"启用{GameTriad}模块"), ref enabled))
        {
            TriadAutomater.ModuleEnabled = enabled;

            if (enabled)
                CufModule.ModuleEnabled = false;
        }

        var autoOpen = C.OpenAutomatically;

        if (ImGui.Checkbox(T("Open Saucy When Challenging an NPC", "挑战NPC时自动打开Saucy"), ref autoOpen))
        {
            C.OpenAutomatically = autoOpen;
            C.Save();
        }

        var selectedDeck = C.SelectedDeckIndex;

        if (Saucy.TTSolver.profileGS.GetPlayerDecks().Count() > 0)
        {
            var useAutoDeck = C.UseRecommendedDeck;
            if (ImGui.Checkbox(T("Automatically choose your deck with the best win chance", "自动选择胜率最高的卡组"), ref useAutoDeck))
            {
                C.UseRecommendedDeck = useAutoDeck;
                C.Save();
            }

            if (!C.UseRecommendedDeck)
            {
                ImGui.PushItemWidth(200);
                string preview;
                if (selectedDeck == -1 || Saucy.TTSolver.profileGS.GetPlayerDecks()[selectedDeck] is null)
                {
                    preview = "";
                }
                else
                {
                    preview = selectedDeck >= 0 ? Saucy.TTSolver.profileGS.GetPlayerDecks()[selectedDeck].name : string.Empty;
                }

                if (ImGui.BeginCombo(T("Select Deck", "选择卡组"), preview))
                {
                    if (ImGui.Selectable(""))
                    {
                        C.SelectedDeckIndex = -1;
                    }

                    foreach (var deck in Saucy.TTSolver.profileGS.GetPlayerDecks())
                    {
                        if (deck is null) continue;
                        var index = deck.id;
                        //var index = Saucy.TTSolver.preGameDecks.Where(x => x.Value == deck).First().Key;
                        if (ImGui.Selectable(deck.name, index == selectedDeck))
                        {
                            C.SelectedDeckIndex = index;
                            C.Save();
                        }
                    }

                    ImGui.EndCombo();
                }
            }
        }
        else
        {
            ImGui.TextWrapped(T("Please initiate a challenge with an NPC to populate your deck list.", "请先与NPC发起对战，以加载你的卡组列表。"));
        }

        if (ImGui.Checkbox(T("Play X Amount of Times", "游玩指定次数"), ref TriadAutomater.PlayXTimes) && (TriadAutomater.NumberOfTimes <= 0 || TriadAutomater.PlayUntilCardDrops || TriadAutomater.PlayUntilAllCardsDropOnce))
        {
            TriadAutomater.NumberOfTimes = 1;
            TriadAutomater.PlayUntilCardDrops = false;
            TriadAutomater.PlayUntilAllCardsDropOnce = false;
        }

        if (ImGui.Checkbox(T("Play Until Any Cards Drop", "直到掉落任意卡片"), ref TriadAutomater.PlayUntilCardDrops) && (TriadAutomater.NumberOfTimes <= 0 || TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilAllCardsDropOnce))
        {
            TriadAutomater.NumberOfTimes = 1;
            TriadAutomater.PlayXTimes = false;
            TriadAutomater.PlayUntilAllCardsDropOnce = false;
        }

        if (GameNpcDB.Get().mapNpcs.TryGetValue(Saucy.TTSolver.preGameNpc?.Id ?? -1, out var npcInfo))
        {
            CurrentNPC = npcInfo;
        }
        else
        {
            CurrentNPC = null;
        }

        var npcName = CurrentNPC is null ? string.Empty : TriadNpcDB.Get().FindByID(CurrentNPC.npcId).Name.GetLocalized();
        var npcSuffix = string.IsNullOrEmpty(npcName) ? string.Empty : T($" ({npcName})", $"（{npcName}）");
        var allCardsLabel = T($"Play Until All Cards Drop from NPC at Least X Times{npcSuffix}", $"直到NPC的所有卡片各掉落至少X次{npcSuffix}");

        if (ImGui.Checkbox(allCardsLabel, ref TriadAutomater.PlayUntilAllCardsDropOnce))
        {
            TriadAutomater.TempCardsWonList.Clear();
            TriadAutomater.PlayUntilCardDrops = false;
            TriadAutomater.PlayXTimes = false;
            TriadAutomater.NumberOfTimes = 1;
        }

        var onlyUnobtained = C.OnlyUnobtainedCards;

        if (TriadAutomater.PlayUntilAllCardsDropOnce)
        {
            ImGui.SameLine();
            if (ImGui.Checkbox(T("Only Unobtained Cards", "仅未获得的卡片"), ref onlyUnobtained))
            {
                TriadAutomater.TempCardsWonList.Clear();
                C.OnlyUnobtainedCards = onlyUnobtained;
                C.Save();
            }
        }

        if (TriadAutomater.PlayUntilAllCardsDropOnce && CurrentNPC != null)
        {
            ImGui.Indent();
            GameCardDB.Get().Refresh();
            foreach (var card in CurrentNPC.rewardCards)
            {
                if ((C.OnlyUnobtainedCards && !GameCardDB.Get().FindById(card).IsOwned) || !C.OnlyUnobtainedCards)
                {
                    TriadAutomater.TempCardsWonList.TryAdd((uint)card, 0);
                    ImGui.Text($"- {TriadCardDB.Get().FindById(GameCardDB.Get().FindById(card).CardId).Name.GetLocalized()} {TriadAutomater.TempCardsWonList[(uint)card]}/{TriadAutomater.NumberOfTimes}");
                }
            }

            if (C.OnlyUnobtainedCards && TriadAutomater.TempCardsWonList.Count == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                ImGui.TextWrapped(T(
                    @"You already have all cards from this NPC. This feature will not work until you untick ""Only Unobtained Cards"" or choose a different NPC.",
                    "你已经拥有该NPC的全部卡片。除非取消勾选“仅未获得的卡片”或选择其他NPC，否则此功能不会生效。"));
                ImGui.PopStyleColor();
            }
            ImGui.Unindent();
        }

        if (TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops || TriadAutomater.PlayUntilAllCardsDropOnce)
        {
            ImGui.PushItemWidth(150f);
            ImGui.Text(T("How many times:", "次数："));
            ImGui.SameLine();

            if (ImGui.InputInt("###NumberOfTimes", ref TriadAutomater.NumberOfTimes))
            {
                if (TriadAutomater.NumberOfTimes <= 0)
                    TriadAutomater.NumberOfTimes = 1;
            }

            ImGui.Checkbox(T("Log out after finishing", "完成后自动登出"), ref TriadAutomater.LogOutAfterCompletion);

            var playSound = C.PlaySound;

            ImGui.Columns(2, default, false);
            if (ImGui.Checkbox(T("Play sound upon completion", "完成时播放音效"), ref playSound))
            {
                C.PlaySound = playSound;
                C.Save();
            }

            if (playSound)
            {
                ImGui.NextColumn();
                ImGui.Text(T("Select Sound", "选择音效"));
                if (ImGui.BeginCombo("###SelectSound", C.SelectedSound))
                {
                    var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds");
                    foreach (var file in new DirectoryInfo(path).GetFiles())
                    {
                        if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(file.FullName)}", C.SelectedSound == Path.GetFileNameWithoutExtension(file.FullName)))
                        {
                            C.SelectedSound = Path.GetFileNameWithoutExtension(file.FullName);
                            C.Save();
                        }
                    }

                    ImGui.EndCombo();
                }

                if (ImGui.Button(T("Open Sound Folder", "打开音效文件夹")))
                {
                    Process.Start("explorer.exe", @$"{Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds")}");
                }
                ImGuiComponents.HelpMarker(T("Drop any MP3 files into the sound folder to add your own custom sounds.", "将MP3文件放入音效文件夹即可添加自定义音效。"));
            }
            ImGui.Columns(1);
        }
    }

    public unsafe void DrawCufTab()
    {
        var enabled = CufModule.ModuleEnabled;

        ImGui.TextWrapped(T(
            @"How to use: Click ""Enable Cuff Module"" then walk up to a Cuff-a-cur machine.",
            $"使用方法：点击“启用{GameCuff}模块”，然后走到{GameCuff}机器前。"));
        ImGui.Separator();

        if (ImGui.Checkbox(T("Enable Cuff Module", $"启用{GameCuff}模块"), ref enabled))
        {
            CufModule.ModuleEnabled = enabled;
            if (enabled && TriadAutomater.ModuleEnabled)
                TriadAutomater.ModuleEnabled = false;
        }

        if (ImGui.Checkbox(T("Play X Amount of Times", "游玩指定次数"), ref TriadAutomater.PlayXTimes) && TriadAutomater.NumberOfTimes <= 0)
        {
            TriadAutomater.NumberOfTimes = 1;
        }

        if (TriadAutomater.PlayXTimes)
        {
            ImGui.PushItemWidth(150f);
            ImGui.Text(T("How many times:", "次数："));
            ImGui.SameLine();

            if (ImGui.InputInt("###NumberOfTimes", ref TriadAutomater.NumberOfTimes))
            {
                if (TriadAutomater.NumberOfTimes <= 0)
                    TriadAutomater.NumberOfTimes = 1;
            }

            ImGui.Checkbox(T("Log out after finishing", "完成后自动登出"), ref TriadAutomater.LogOutAfterCompletion);

            var playSound = C.PlaySound;

            ImGui.Columns(2, default, false);
            if (ImGui.Checkbox(T("Play sound upon completion", "完成时播放音效"), ref playSound))
            {
                C.PlaySound = playSound;
                C.Save();
            }

            if (playSound)
            {
                ImGui.NextColumn();
                ImGui.Text(T("Select Sound", "选择音效"));
                if (ImGui.BeginCombo("###SelectSound", C.SelectedSound))
                {
                    var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds");
                    foreach (var file in new DirectoryInfo(path).GetFiles())
                    {
                        if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(file.FullName)}", C.SelectedSound == Path.GetFileNameWithoutExtension(file.FullName)))
                        {
                            C.SelectedSound = Path.GetFileNameWithoutExtension(file.FullName);
                            C.Save();
                        }
                    }

                    ImGui.EndCombo();
                }

                if (ImGui.Button(T("Open Sound Folder", "打开音效文件夹")))
                {
                    Process.Start("explorer.exe", @$"{Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory.FullName, "Sounds")}");
                }
                ImGuiComponents.HelpMarker(T("Drop any MP3 files into the sound folder to add your own custom sounds.", "将MP3文件放入音效文件夹即可添加自定义音效。"));
            }
            ImGui.Columns(1);
        }
    }

    private void DrawDebugTab()
    {
        if (GoldSaucerManager.Instance() != null && GoldSaucerManager.Instance()->CurrentGFateDirector != null)
        {
            var dir = GoldSaucerManager.Instance()->CurrentGFateDirector;
            ImGui.Text($"{T("GateType", "GATE类型")}: {dir->GateType}");
            ImGui.Text($"{T("GatePositionType", "GATE位置类型")}: {dir->GatePositionType}");
            ImGui.Text($"{T("Flags", "标志位")}: {dir->Flags}");
            ImGui.Text($"{T("IsRunningGate", "是否运行中")}: {dir->IsRunningGate()}");
            ImGui.Text($"{T("IsAcceptingGate", "是否可进入")}: {dir->IsAcceptingGate()}");
        }
    }
}
