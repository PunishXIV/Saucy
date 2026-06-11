using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.GoldSaucer;
using PunishLib.ImGuiMethods;
using Saucy.Framework;
using System;
using System.Collections.Generic;
using System.Numerics;
using static ECommons.GenericHelpers;
namespace Saucy;

public unsafe partial class PluginUI : Window
{
    private const long DeltaVisibleMs = 30_000;

    private const uint MgpItemId = 29;
    private const string KagekazuKofiUrl = "https://ko-fi.com/kagekazu";

    private static readonly string[] SidebarLabels =
    [
        "Out on a Limb",
        "Cuff-a-Cur",
        "Slice is Right",
        "Wind Blows",
        "Triple Triad",
        "Mini-Cactpot",
        "Jumbo Cactpot",
        "Stats",
        "About",
        "Debug",
        "Saucy theme",
        "MACHINES",
        "GATES",
        "OTHER GAMES"
    ];

    private static int _lastMgp = -1;
    private static long _lastMgpIncreaseMs;
    private NavItem _selectedNav = NavItem.TripleTriad;
    private SaucyTheme.ThemeScope? _themeScope;
    private bool drewTitleBarVersion;

    public PluginUI() : base("Saucy###Saucy")
    {
        Size = new Vector2(310, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(280, 240), MaximumSize = new(float.MaxValue, float.MaxValue)
        };

        TitleBarButtons.Add(new()
        {
            ShowTooltip = () => ImGui.SetTooltip("♥ Ko-fi (to support my gacha addiction)"),
            Icon = FontAwesomeIcon.Heart,
            IconOffset = new(1, 1),
            Click = _ => ShellStart(KagekazuKofiUrl)
        });
    }

    public bool Enabled { get; set; } = false;

    public void OpenForTriad()
    {
        _selectedNav = NavItem.TripleTriad;
        IsOpen = true;
    }

    public void OpenForDebug()
    {
        _selectedNav = NavItem.Debug;
        IsOpen = true;
    }

    private static float CalcSidebarWidth()
    {
        var style = ImGui.GetStyle();
        var maxLabel = 0f;
        foreach (var s in SidebarLabels)
        {
            var w = ImGui.CalcTextSize(s).X;
            if (w > maxLabel)
            {
                maxLabel = w;
            }
        }
        var checkboxExtra = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
        return maxLabel + checkboxExtra + style.WindowPadding.X * 2f + style.FramePadding.X * 2f;
    }

    public override void PreDraw()
    {
        _themeScope?.Dispose();
        _themeScope = SaucyTheme.PushScope();

        var info = BuildBannerInfo();

        if (_lastMgp >= 0 && info.Mgp > _lastMgp)
        {
            _lastMgpIncreaseMs = Environment.TickCount64;
        }
        _lastMgp = info.Mgp;

        var showDelta = info.SessionDelta > 0
                        && Environment.TickCount64 - _lastMgpIncreaseMs < DeltaVisibleMs;
        var delta = showDelta ? $"  +{info.SessionDelta:N0}" : "";
        var status = info.ModuleStatus == "Idle" ? "Idle" : $"Enabled: {info.ModuleStatus}";
        WindowName = $"Saucy  \u2022  {status}  \u2022  MGP {info.Mgp:N0}{delta}###Saucy";
    }

    public override void OnClose()
    {
        TitleBarVersion.ClearCache();
        base.OnClose();
    }

    public override void PostDraw()
    {
        if (!drewTitleBarVersion)
        {
            TitleBarVersion.DrawFromWindowLookup(
                TitleBarButtons.Count,
                AllowPinning || AllowClickthrough,
                WindowName);
        }

        drewTitleBarVersion = false;
        _themeScope?.Dispose();
        _themeScope = null;
    }

    public override void Draw()
    {
        TitleBarVersion.DrawFromContext(
            TitleBarButtons.Count,
            AllowPinning || AllowClickthrough,
            WindowName);
        drewTitleBarVersion = true;

        var sidebarW = CalcSidebarWidth();
        var availY = ImGui.GetContentRegionAvail().Y;

        using (var sidebar = ImRaii.Child("##Sidebar", new(sidebarW, availY), true))
        {
            if (sidebar)
            {
                DrawSidebar();
            }
        }

        ImGui.SameLine();

        using (var panel = ImRaii.Child("##Panel", new(0, availY), false))
        {
            if (panel)
            {
                DrawPanel();
            }
        }
    }

    private void DrawSidebar()
    {
        DrawSidebarHeader("MACHINES");
        NavSelectable("Out on a Limb", NavItem.OutOnALimb);
        NavSelectable("Cuff-a-Cur", NavItem.CuffACur);

        ImGui.Dummy(new(0, 6));
        DrawSidebarHeader("GATES");
        NavSelectable("Slice is Right", NavItem.SliceIsRight);
        NavSelectable("Wind Blows", NavItem.AnyWayTheWindBlows);
        NavSelectable("Air Force One", NavItem.AirForceOne);

        ImGui.Dummy(new(0, 6));
        DrawSidebarHeader("OTHER GAMES");
        NavSelectable("Triple Triad", NavItem.TripleTriad);
        NavSelectable("Mini-Cactpot", NavItem.MiniCactpot);
        NavSelectable("Jumbo Cactpot", NavItem.JumboCactpot);

        ImGui.Dummy(new(0, 6));
        ImGui.Separator();
        NavSelectable("Stats", NavItem.Stats);
        NavSelectable("About", NavItem.About);
        NavSelectable("Debug", NavItem.Debug);

        var style = ImGui.GetStyle();
        var checkboxH = ImGui.GetFrameHeight();
        var creditH = ImGui.GetTextLineHeight();
        var bottomBlockH = style.ItemSpacing.Y + 1f + style.ItemSpacing.Y + checkboxH + style.ItemSpacing.Y + creditH;
        var targetY = ImGui.GetWindowHeight() - style.WindowPadding.Y - bottomBlockH;
        if (targetY > ImGui.GetCursorPosY())
        {
            ImGui.SetCursorPosY(targetY);
        }

        ImGui.Separator();
        var on = C.SaucyThemeEnabled;
        if (ImGui.Checkbox("Saucy theme", ref on))
        {
            C.SaucyThemeEnabled = on;
            C.Save();
        }
        ImGui.TextDisabled("Designed by Wah");
    }

    private void NavSelectable(string label, NavItem item)
    {
        if (ImGui.Selectable(label, _selectedNav == item))
        {
            _selectedNav = item;
        }
    }

    private static void DrawSidebarHeader(string label) => ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.TextDisabled), label);

    private void DrawPanel()
    {
        switch (_selectedNav)
        {
            case NavItem.TripleTriad: DrawTriadPanel(); break;
            case NavItem.CuffACur: DrawCuffPanel(); break;
            case NavItem.OutOnALimb: DrawLimbPanel(); break;
            case NavItem.SliceIsRight: DrawSliceIsRightPanel(); break;
            case NavItem.AnyWayTheWindBlows: DrawWindBlowsPanel(); break;
            case NavItem.AirForceOne: DrawAirForcePanel(); break;
            case NavItem.MiniCactpot: DrawMiniCactpotPanel(); break;
            case NavItem.JumboCactpot: DrawJumboCactpotPanel(); break;
            case NavItem.Stats: DrawStatsTab(); break;
            case NavItem.About: AboutTab.Draw("Saucy"); break;
            case NavItem.Debug: DrawDebugTab(); break;
        }
    }

    private static void DrawTriadPanel()
    {
        DrawPanelHeader("Triple Triad");
        ImGuiEx.EzTabBar("###Triad",
            ("Main", TriadSettingsUi.Draw, null, false),
            ("Cache", TriadCacheSettingsUi.Draw, null, false));
    }

    private static void DrawPanelHeader(string title, string? subtitle = null) =>
        SaucyTheme.DrawPanelHeader(title, subtitle);

    private void DrawDebugTab()
    {
        ImGuiLayout.DrawCollapsingSection("Gold Saucer gate", ImGuiTreeNodeFlags.DefaultOpen, () =>
        {
            if (GoldSaucerManager.Instance() != null && GoldSaucerManager.Instance()->CurrentGFateDirector != null)
            {
                var dir = GoldSaucerManager.Instance()->CurrentGFateDirector;
                ImGui.Text($"GateType: {dir->GateType}");
                ImGui.Text($"GatePositionType: {dir->GatePositionType}");
                ImGui.Text($"Flags: {dir->Flags}");
            }
            else
            {
                ImGui.TextDisabled("No active gate director.");
            }
        });

        ImGuiLayout.DrawCollapsingSection("Triple Triad NPC menu", ImGuiTreeNodeFlags.DefaultOpen, () =>
        {
            ImGui.Text($"Navigation active: {TriadMapNavigation.IsNavigationActive}");
            ImGui.Text($"Awaiting triad start: {TriadMapNavigation.IsAwaitingTriadStartDialog()}");

            var menuLines = new List<string>();
            SelectStringHelper.CollectTriadMenuDebugLines(menuLines);
            if (menuLines.Count == 0)
            {
                ImGui.TextDisabled("No select string menu open.");
            }
            else
            {
                var listHeight = Math.Clamp(menuLines.Count * ImGui.GetTextLineHeightWithSpacing() + 8f, 60f, 200f);
                using var scroll = ImRaii.Child("##TriadMenuDebug", new(0, listHeight), true);
                if (scroll)
                {
                    foreach (var line in menuLines)
                    {
                        ImGui.TextUnformatted(line);
                    }
                }
            }
        });
    }

    private enum NavItem
    {
        TripleTriad,
        CuffACur,
        OutOnALimb,
        SliceIsRight,
        AnyWayTheWindBlows,
        AirForceOne,
        MiniCactpot,
        JumboCactpot,
        Stats,
        About,
        Debug
    }
}
