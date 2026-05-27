using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using FFTriadBuddy;
using FFXIVClientStructs.FFXIV.Client.Game;
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
namespace Saucy;

// It is good to have this be disposable in general, in case you ever need it
// to do any cleanup
public unsafe class PluginUI : Window
{
    public PluginUI() : base("Saucy###Saucy")
    {
        Size = new Vector2(310, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private static readonly string[] SidebarLabels =
    {
        "Out on a Limb", "Cuff-a-Cur",
        "Slice is Right", "Wind Blows",
        "Triple Triad", "Mini-Cactpot",
        "Stats", "About",
#if DEBUG
        "Debug",
#endif
        "Saucy theme",
        "MACHINES", "GATES", "OTHER GAMES",
    };

    private static float CalcSidebarWidth()
    {
        var style = ImGui.GetStyle();
        float maxLabel = 0f;
        foreach (var s in SidebarLabels)
        {
            var w = ImGui.CalcTextSize(s).X;
            if (w > maxLabel) maxLabel = w;
        }
        var checkboxExtra = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
        return maxLabel + checkboxExtra + style.WindowPadding.X * 2f + style.FramePadding.X * 2f;
    }

    private enum NavItem
    {
        TripleTriad,
        CuffACur,
        OutOnALimb,
        SliceIsRight,
        AnyWayTheWindBlows,
        MiniCactpot,
        Stats,
        About,
#if DEBUG
        Debug,
#endif
    }

    private NavItem _selectedNav = NavItem.TripleTriad;

    public GameNpcInfo? CurrentNPC
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

    private static int _lastMgp = -1;
    private static long _lastMgpIncreaseMs;
    private const long DeltaVisibleMs = 30_000;

    public override void PreDraw()
    {
        if (C.SaucyThemeEnabled)
            SaucyTheme.Push();

        var info = BuildBannerInfo();

        if (_lastMgp >= 0 && info.Mgp > _lastMgp)
            _lastMgpIncreaseMs = Environment.TickCount64;
        _lastMgp = info.Mgp;

        var showDelta = info.SessionDelta > 0
                     && Environment.TickCount64 - _lastMgpIncreaseMs < DeltaVisibleMs;
        var delta = showDelta ? $"  +{info.SessionDelta:N0}" : "";
        var status = info.ModuleStatus == "Idle" ? "Idle" : $"Enabled: {info.ModuleStatus}";
        WindowName = $"Saucy  \u2022  {status}  \u2022  MGP {info.Mgp:N0}{delta}###Saucy";
    }

    public override void PostDraw()
    {
        SaucyTheme.Pop();
    }

    private const uint MgpItemId = 29;

    public override void Draw()
    {
        var sidebarW = CalcSidebarWidth();
        var availY = ImGui.GetContentRegionAvail().Y;

        using (var sidebar = ImRaii.Child("##Sidebar", new Vector2(sidebarW, availY), true))
        {
            if (sidebar) DrawSidebar();
        }

        ImGui.SameLine();

        using (var panel = ImRaii.Child("##Panel", new Vector2(0, availY), false))
        {
            if (panel) DrawPanel();
        }
    }

    private void DrawSidebar()
    {
        DrawSidebarHeader("MACHINES");
        NavSelectable("Out on a Limb", NavItem.OutOnALimb);
        NavSelectable("Cuff-a-Cur", NavItem.CuffACur);

        ImGui.Dummy(new Vector2(0, 6));
        DrawSidebarHeader("GATES");
        NavSelectable("Slice is Right", NavItem.SliceIsRight);
        NavSelectable("Wind Blows", NavItem.AnyWayTheWindBlows);

        ImGui.Dummy(new Vector2(0, 6));
        DrawSidebarHeader("OTHER GAMES");
        NavSelectable("Triple Triad", NavItem.TripleTriad);
        NavSelectable("Mini-Cactpot", NavItem.MiniCactpot);

        ImGui.Dummy(new Vector2(0, 6));
        ImGui.Separator();
        NavSelectable("Stats", NavItem.Stats);
        NavSelectable("About", NavItem.About);
#if DEBUG
        NavSelectable("Debug", NavItem.Debug);
#endif

        var style = ImGui.GetStyle();
        var checkboxH = ImGui.GetFrameHeight();
        var bottomBlockH = style.ItemSpacing.Y + 1f + style.ItemSpacing.Y + checkboxH;
        var targetY = ImGui.GetWindowHeight() - style.WindowPadding.Y - bottomBlockH;
        if (targetY > ImGui.GetCursorPosY())
            ImGui.SetCursorPosY(targetY);

        ImGui.Separator();
        var on = C.SaucyThemeEnabled;
        if (ImGui.Checkbox("Saucy theme", ref on))
        {
            C.SaucyThemeEnabled = on;
            C.Save();
        }
    }

    private void NavSelectable(string label, NavItem item)
    {
        if (ImGui.Selectable(label, _selectedNav == item))
            _selectedNav = item;
    }

    private static void DrawSidebarHeader(string label)
    {
        ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.TextDisabled), label);
    }

    private void DrawPanel()
    {
        switch (_selectedNav)
        {
            case NavItem.TripleTriad:        DrawTriadTab(); break;
            case NavItem.CuffACur:           DrawCuffPanel(); break;
            case NavItem.OutOnALimb:         DrawLimbPanel(); break;
            case NavItem.SliceIsRight:       DrawSliceIsRightPanel(); break;
            case NavItem.AnyWayTheWindBlows: DrawWindBlowsPanel(); break;
            case NavItem.MiniCactpot:        DrawMiniCactpotPanel(); break;
            case NavItem.Stats:              DrawStatsTab(); break;
            case NavItem.About:              AboutTab.Draw("Saucy"); break;
#if DEBUG
            case NavItem.Debug:              DrawDebugTab(); break;
#endif
        }
    }

    private static void DrawPanelHeader(string title, string? subtitle = null)
    {
        ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.Text), title);
        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(" \u2014 " + subtitle);
        }
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 2));
    }

    private void DrawCuffPanel()
    {
        DrawPanelHeader("Cuff-a-Cur", "punch the cactuar");

        var enabled = CufModule.ModuleEnabled;
        if (ImGui.Checkbox("Enable##Cuff", ref enabled))
        {
            CufModule.ModuleEnabled = enabled;
            C.EnableCuffModule = enabled;
            ToggleEnabledModule(Saucy.ModuleManager.GetModule<CuffACur.CuffACurModule>()!.InternalName, enabled);
            if (enabled && TriadAutomater.ModuleEnabled)
                TriadAutomater.ModuleEnabled = false;
        }

        ImGui.Dummy(new Vector2(0, 4));
        DrawCuffBody();
    }

    private void DrawLimbPanel()
    {
        DrawPanelHeader("Out on a Limb", "swing the hatchet");
        ImGuiEx.EzTabBar("###Limb",
            ("Main", P.LimbManager.DrawSettings, null, false),
            ("Debug", P.LimbManager.DrawDebug, null, false));
    }

    private static void DrawSliceIsRightPanel()
    {
        DrawPanelHeader("Slice is Right", "dodge the falling slices");
        var enabled = C.SliceIsRightModuleEnabled;
        if (ImGui.Checkbox("Enable##Slice", ref enabled))
        {
            C.SliceIsRightModuleEnabled = enabled;
            ToggleEnabledModule(ModuleManager.GetModule<SliceIsRight>()!.InternalName, enabled);
        }
    }

    private static void DrawWindBlowsPanel()
    {
        DrawPanelHeader("Any Way the Wind Blows", "chocobo wind reader");
        var enabled = C.AnyWayTheWindBlowsModuleEnabled;
        if (ImGui.Checkbox("Enable##Wind", ref enabled))
        {
            C.AnyWayTheWindBlowsModuleEnabled = enabled;
            ToggleEnabledModule(ModuleManager.GetModule<AnyWayTheWindBlows>()!.InternalName, enabled);
        }
    }

    private static void DrawMiniCactpotPanel()
    {
        DrawPanelHeader("Mini-Cactpot", "daily 3\u00d73 scratcher");
        var enabled = C.EnableAutoMiniCactpot;
        if (ImGui.Checkbox("Enable##Mini", ref enabled))
        {
            C.EnableAutoMiniCactpot = enabled;
            ToggleEnabledModule(ModuleManager.GetModule<MiniCactpot.MiniCactpot>()!.InternalName, enabled);
        }
    }

    private static BannerInfo BuildBannerInfo()
    {
        var im  = InventoryManager.Instance();
        var mgp = im != null ? im->GetInventoryItemCount(MgpItemId, false, false, false, 0) : 0;

        string status;
        if (TriadAutomater.ModuleEnabled) status = "Triple Triad";
        else if (CufModule.ModuleEnabled) status = "Cuff-a-Cur";
        else if (P?.LimbManager?.Cfg?.EnableLimb == true) status = "Out on a Limb";
        else if (C.SliceIsRightModuleEnabled) status = "Slice is Right";
        else if (C.AnyWayTheWindBlowsModuleEnabled) status = "Any Way the Wind Blows";
        else if (C.EnableAutoMiniCactpot) status = "Mini-Cactpot";
        else status = "Idle";

        var sessionDelta = C.SessionStats.MGPWon + C.SessionStats.CuffMGP + C.SessionStats.LimbMGP;

        return new BannerInfo
        {
            Mgp = mgp,
            SessionDelta = sessionDelta,
            ModuleStatus = status,
        };
    }

    private static void ToggleEnabledModule(string internalName, bool enabled)
    {
        if (enabled) C.EnabledModules.Add(internalName);
        else C.EnabledModules.Remove(internalName);
        C.Save();
    }

    private static void DrawCuffBody()
    {
        if (ImGui.Checkbox("Play X Amount of Times", ref TriadAutomater.PlayXTimes) && TriadAutomater.NumberOfTimes <= 0)
            TriadAutomater.NumberOfTimes = 1;

        if (!TriadAutomater.PlayXTimes) return;

        ImGui.PushItemWidth(150f * ImGuiHelpers.GlobalScale);
        ImGui.Text("How many times:");
        ImGui.SameLine();
        if (ImGui.InputInt("###NumberOfTimes", ref TriadAutomater.NumberOfTimes))
        {
            if (TriadAutomater.NumberOfTimes <= 0)
                TriadAutomater.NumberOfTimes = 1;
        }

        ImGui.Checkbox("Log out after finishing", ref TriadAutomater.LogOutAfterCompletion);

        var playSound = C.PlaySound;
        ImGui.Columns(2, default, false);
        if (ImGui.Checkbox("Play sound upon completion", ref playSound))
        {
            C.PlaySound = playSound;
            C.Save();
        }
        if (playSound)
        {
            ImGui.NextColumn();
            DrawSoundPicker();
        }
        ImGui.Columns();
    }

    private void DrawStatsTab()
    {
        DrawStatsToolbar();

        var (life, sess) = (C.Stats, C.SessionStats);

        DrawStatsCard("Triple Triad", TriadHeadline(life), () => DrawTriadRows(life, sess));
        DrawStatsCard("Cuff-a-Cur", CuffHeadline(life), () => DrawCuffRows(life, sess));
        DrawStatsCard("Out on a Limb", LimbHeadline(life), () => DrawLimbRows(life, sess));
    }

    private static void DrawStatsToolbar()
    {
        ImGui.TextDisabled("Hold Ctrl to confirm resets.");
        ImGui.SameLine();
        const string lifeLbl = "Reset Lifetime";
        const string sessLbl = "Reset Session";
        var pad = ImGui.GetStyle().FramePadding.X * 2f;
        var lifeW = ImGui.CalcTextSize(lifeLbl).X + pad;
        var sessW = ImGui.CalcTextSize(sessLbl).X + pad;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rightX = ImGui.GetWindowContentRegionMax().X - lifeW - sessW - spacing;
        if (rightX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(rightX);
        if (ImGui.Button(lifeLbl) && ImGui.GetIO().KeyCtrl)
        {
            C.Stats = new();
            C.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(sessLbl) && ImGui.GetIO().KeyCtrl)
            C.SessionStats = new();
        ImGui.Dummy(new Vector2(0, 2));
    }

    private static string TriadHeadline(Stats s)
    {
        if (s.GamesPlayedWithSaucy == 0) return "no games played";
        var pct = Math.Round(s.GamesWonWithSaucy / (double)s.GamesPlayedWithSaucy * 100, 1);
        return $"{s.GamesPlayedWithSaucy:N0} games \u00b7 {pct}% win";
    }

    private static string CuffHeadline(Stats s)
        => s.CuffGamesPlayed == 0 ? "no games played" : $"{s.CuffGamesPlayed:N0} games";

    private static string LimbHeadline(Stats s)
        => s.LimbGamesPlayed == 0 ? "no games played" : $"{s.LimbGamesPlayed:N0} games";

    private static void DrawTriadRows(Stats life, Stats sess)
    {
        if (!BeginStatsTable("triad")) return;
        StatsHeader();
        StatsRow("Games", life.GamesPlayedWithSaucy, sess.GamesPlayedWithSaucy);
        StatsRow("Wins", life.GamesWonWithSaucy, sess.GamesWonWithSaucy);
        StatsRow("Losses", life.GamesLostWithSaucy, sess.GamesLostWithSaucy);
        StatsRow("Draws", life.GamesDrawnWithSaucy, sess.GamesDrawnWithSaucy);
        StatsRow("Cards won", life.CardsDroppedWithSaucy, sess.CardsDroppedWithSaucy);
        StatsRow("Card drop value", $"{GetDroppedCardValues(life):N0}", $"{GetDroppedCardValues(sess):N0}");
        StatsRow("MGP won", $"{life.MGPWon:N0}", $"{sess.MGPWon:N0}", accent: true);

        var (lifeNpcCount, lifeNpcName) = TopNpcCell(life);
        var (sessNpcCount, sessNpcName) = TopNpcCell(sess);
        StatsRow("Most played NPC", lifeNpcCount, sessNpcCount, tooltipLife: lifeNpcName, tooltipSess: sessNpcName);

        var (lifeCardCount, lifeCardName) = TopCardCell(life);
        var (sessCardCount, sessCardName) = TopCardCell(sess);
        StatsRow("Most won card", lifeCardCount, sessCardCount, tooltipLife: lifeCardName, tooltipSess: sessCardName);

        ImGui.EndTable();
    }

    private static void DrawCuffRows(Stats life, Stats sess)
    {
        if (!BeginStatsTable("cuff")) return;
        StatsHeader();
        StatsRow("Games", life.CuffGamesPlayed, sess.CuffGamesPlayed);
        StatsRow("Bruisings", life.CuffBruisings, sess.CuffBruisings);
        StatsRow("Punishings", life.CuffPunishings, sess.CuffPunishings);
        StatsRow("Brutals", life.CuffBrutals, sess.CuffBrutals);
        StatsRow("MGP won", $"{life.CuffMGP:N0}", $"{sess.CuffMGP:N0}", accent: true);
        ImGui.EndTable();
    }

    private static void DrawLimbRows(Stats life, Stats sess)
    {
        if (!BeginStatsTable("limb")) return;
        StatsHeader();
        StatsRow("Games", life.LimbGamesPlayed, sess.LimbGamesPlayed);
        StatsRow("MGP won", $"{life.LimbMGP:N0}", $"{sess.LimbMGP:N0}", accent: true);
        ImGui.EndTable();
    }

    private static bool BeginStatsTable(string id)
        => ImGui.BeginTable($"##stats_{id}", 3, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.SizingStretchProp);

    private static (string count, string? name) TopNpcCell(Stats s)
    {
        if (s.NPCsPlayed.Count == 0) return ("\u2014", null);
        var top = s.NPCsPlayed.OrderByDescending(x => x.Value).First();
        return ($"{top.Value:N0}", top.Key);
    }

    private static (string count, string? name) TopCardCell(Stats s)
    {
        if (s.CardsWon.Count == 0) return ("\u2014", null);
        var top = s.CardsWon.OrderByDescending(x => x.Value).First();
        return ($"{top.Value:N0}", TriadCardDB.Get().FindById((int)top.Key)!.Name.GetLocalized());
    }

    private static void StatsHeader()
    {
        ImGui.TableSetupColumn("Metric", ImGuiTableColumnFlags.WidthStretch, 0.32f);
        ImGui.TableSetupColumn("Lifetime", ImGuiTableColumnFlags.WidthStretch, 0.34f);
        ImGui.TableSetupColumn("Session", ImGuiTableColumnFlags.WidthStretch, 0.34f);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        RightAlignCellText("Lifetime", SaucyTheme.ColorOr(SaucyTheme.ColumnHeader, ImGuiCol.Text));
        ImGui.TableNextColumn();
        RightAlignCellText("Session", SaucyTheme.ColorOr(SaucyTheme.ColumnHeader, ImGuiCol.Text));
    }

    private static void StatsRow(string label, int life, int sess, bool accent = false)
        => StatsRow(label, life.ToString("N0"), sess.ToString("N0"), accent);

    private static void StatsRow(string label, string life, string sess, bool accent = false,
                                 string? tooltipLife = null, string? tooltipSess = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);

        var col = accent
            ? SaucyTheme.ColorOr(SaucyTheme.BodyTextAccent, ImGuiCol.Text)
            : SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.Text);

        ImGui.TableNextColumn();
        RightAlignCellText(life, col);
        if (tooltipLife != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltipLife);

        ImGui.TableNextColumn();
        RightAlignCellText(sess, col);
        if (tooltipSess != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltipSess);
    }

    private static void RightAlignCellText(string text, Vector4 color)
    {
        const float cellRightMargin = 6f;
        var avail = ImGui.GetContentRegionAvail().X;
        var tw = ImGui.CalcTextSize(text).X;
        var ind = avail - tw - cellRightMargin;
        if (ind > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ind);
        ImGui.TextColored(color, text);
    }

    private static void DrawStatsCard(string name, string subtitle, Action body)
        => SaucyTheme.DrawCard(name, subtitle, body);

    private static int GetDroppedCardValues(Stats stat)
    {
        var output = 0;
        foreach (var card in stat.CardsWon)
        {
            output += GameCardDB.Get().FindById((int)card.Key)!.SaleValue * stat.CardsWon[card.Key];
        }
        return output;
    }

    public void DrawTriadTab()
    {
        if (GameNpcDB.Get().mapNpcs.TryGetValue(TTSolver.preGameNpc?.Id ?? -1, out var npcInfo))
            CurrentNPC = npcInfo;
        else
            CurrentNPC = null;

        var enabled = TriadAutomater.ModuleEnabled;
        if (ImGui.Checkbox("Enable Triad Module", ref enabled))
        {
            TriadAutomater.ModuleEnabled = enabled;
            if (enabled)
                CufModule.ModuleEnabled = false;
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Challenge an NPC first, then enable the module here.");

        var autoOpen = C.OpenAutomatically;
        if (ImGui.Checkbox("Open Saucy when challenging an NPC", ref autoOpen))
        {
            C.OpenAutomatically = autoOpen;
            C.Save();
        }

        ImGui.Dummy(new Vector2(0, 4));

        SaucyTheme.DrawCard("Deck", null, DrawTriadDeckBody);
        SaucyTheme.DrawCard("Run mode", null, DrawTriadRunModeBody);
        SaucyTheme.DrawCard("Notifications", null, DrawTriadNotificationsBody);
    }

    private static void DrawTriadDeckBody()
    {
        if (TTSolver.profileGS.GetPlayerDecks()!.Count() == 0)
        {
            ImGui.TextWrapped("Initiate a challenge with an NPC to populate your deck list.");
            return;
        }

        var useAutoDeck = C.UseRecommendedDeck;
        if (ImGui.Checkbox("Auto-pick deck with best win chance", ref useAutoDeck))
        {
            C.UseRecommendedDeck = useAutoDeck;
            C.Save();
        }

        if (C.UseRecommendedDeck) return;

        var selectedDeck = C.SelectedDeckIndex;
        var decks = TTSolver.profileGS.GetPlayerDecks()!;
        string preview = (selectedDeck >= 0 && selectedDeck < decks.Count() && decks[selectedDeck] != null)
            ? decks[selectedDeck]!.name : "";

        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Select deck", preview))
        {
            if (ImGui.Selectable(""))
                C.SelectedDeckIndex = -1;

            foreach (var deck in decks)
            {
                if (deck is null) continue;
                if (ImGui.Selectable(deck.name, deck.id == selectedDeck))
                {
                    C.SelectedDeckIndex = deck.id;
                    C.Save();
                }
            }
            ImGui.EndCombo();
        }
    }

    private void DrawTriadRunModeBody()
    {
        if (ImGui.RadioButton("Play a set number of times", TriadAutomater.PlayXTimes))
        {
            TriadAutomater.PlayXTimes = true;
            TriadAutomater.PlayUntilCardDrops = false;
            TriadAutomater.PlayUntilAllCardsDropOnce = false;
            if (TriadAutomater.NumberOfTimes <= 0) TriadAutomater.NumberOfTimes = 1;
        }

        if (ImGui.RadioButton("Play until any cards drop", TriadAutomater.PlayUntilCardDrops))
        {
            TriadAutomater.PlayUntilCardDrops = true;
            TriadAutomater.PlayXTimes = false;
            TriadAutomater.PlayUntilAllCardsDropOnce = false;
            if (TriadAutomater.NumberOfTimes <= 0) TriadAutomater.NumberOfTimes = 1;
        }

        if (ImGui.RadioButton("Play until all cards drop", TriadAutomater.PlayUntilAllCardsDropOnce))
        {
            TriadAutomater.PlayUntilAllCardsDropOnce = true;
            TriadAutomater.PlayUntilCardDrops = false;
            TriadAutomater.PlayXTimes = false;
            TriadAutomater.NumberOfTimes = 1;
            TriadAutomater.TempCardsWonList.Clear();
        }

        if (TriadAutomater.PlayUntilAllCardsDropOnce)
        {
            using var subIndent = ImRaii.PushIndent();

            if (CurrentNPC != null)
                ImGui.TextDisabled($"NPC: {TriadNpcDB.Get().FindByID(CurrentNPC.npcId).Name.GetLocalized()}");

            var onlyUnobtained = C.OnlyUnobtainedCards;
            if (ImGui.Checkbox("Only unobtained cards", ref onlyUnobtained))
            {
                TriadAutomater.TempCardsWonList.Clear();
                C.OnlyUnobtainedCards = onlyUnobtained;
                C.Save();
            }

            if (CurrentNPC != null)
            {
                GameCardDB.Get().Refresh();
                foreach (var card in CurrentNPC.rewardCards)
                {
                    if ((C.OnlyUnobtainedCards && !GameCardDB.Get().FindById(card)!.IsOwned) || !C.OnlyUnobtainedCards)
                    {
                        TriadAutomater.TempCardsWonList.TryAdd((uint)card, 0);
                        ImGui.Text($"\u2022 {TriadCardDB.Get().FindById(GameCardDB.Get().FindById(card)!.CardId)!.Name.GetLocalized()} \u2014 {TriadAutomater.TempCardsWonList[(uint)card]}/{TriadAutomater.NumberOfTimes}");
                    }
                }

                if (C.OnlyUnobtainedCards && TriadAutomater.TempCardsWonList.Count == 0)
                {
                    using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
                    ImGui.TextWrapped("You already have every card from this NPC. Untick \"Only unobtained cards\" or pick a different NPC.");
                }
            }
        }

        if (TriadAutomater.PlayXTimes || TriadAutomater.PlayUntilCardDrops || TriadAutomater.PlayUntilAllCardsDropOnce)
        {
            ImGui.SetNextItemWidth(120f * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("How many times", ref TriadAutomater.NumberOfTimes))
            {
                if (TriadAutomater.NumberOfTimes <= 0)
                    TriadAutomater.NumberOfTimes = 1;
            }
        }
    }

    private static void DrawTriadNotificationsBody()
    {
        ImGui.Checkbox("Log out after finishing", ref TriadAutomater.LogOutAfterCompletion);

        var playSound = C.PlaySound;
        if (ImGui.Checkbox("Play sound on completion", ref playSound))
        {
            C.PlaySound = playSound;
            C.Save();
        }

        if (playSound)
        {
            using var _ = ImRaii.PushIndent();
            DrawSoundPicker();
        }
    }

    private static void DrawSoundPicker()
    {
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("###SelectSound", C.SelectedSound))
        {
            var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds");
            Directory.CreateDirectory(path);
            foreach (var file in new DirectoryInfo(path).GetFiles())
            {
                var name = Path.GetFileNameWithoutExtension(file.FullName);
                if (ImGui.Selectable(name, C.SelectedSound == name))
                {
                    C.SelectedSound = name;
                    C.Save();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            Process.Start("explorer.exe", Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds"));
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open sound folder — drop MP3s here to add your own.");
    }

    private void DrawDebugTab()
    {
        if (GoldSaucerManager.Instance() != null && GoldSaucerManager.Instance()->CurrentGFateDirector != null)
        {
            var dir = GoldSaucerManager.Instance()->CurrentGFateDirector;
            ImGui.Text($"GateType: {dir->GateType}");
            ImGui.Text($"GatePositionType: {dir->GatePositionType}");
            ImGui.Text($"Flags: {dir->Flags}");
        }
    }
}
