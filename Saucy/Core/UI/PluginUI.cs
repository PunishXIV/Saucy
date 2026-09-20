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
using System.Reflection;
using static ECommons.GenericHelpers;
namespace Saucy;

public unsafe partial class PluginUI : Window
{
    private const long DeltaVisibleMs = 30_000;

    private const uint MgpItemId = 29;
    private const string KagekazuKofiUrl = "https://ko-fi.com/kagekazu";

    private static readonly string[] SidebarLabels =
    [
        ModuleDisplayNames.OutOnALimb,
        ModuleDisplayNames.CuffACur,
        ModuleDisplayNames.SliceIsRight,
        ModuleDisplayNames.WindBlows,
        ModuleDisplayNames.AirForceOne,
        ModuleDisplayNames.TripleTriad,
        ModuleDisplayNames.MiniCactpot,
        ModuleDisplayNames.JumboCactpot,
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
            ShowTooltip = () => ImGui.SetTooltip(Loc.T("♥ Ko-fi (to support my gacha addiction)")),
            Icon = FontAwesomeIcon.Heart,
            IconOffset = new(1, 1),
            Click = _ => ShellStart(KagekazuKofiUrl)
        });
    }

    public void OpenForTriad()
    {
        _selectedNav = NavItem.TripleTriad;
        IsOpen = true;
    }

    public void ToggleDebug()
    {
        C.ShowDebugUi = !C.ShowDebugUi;
        if (C.ShowDebugUi)
        {
            _selectedNav = NavItem.Debug;
            IsOpen = true;
        }
        else if (_selectedNav == NavItem.Debug)
        {
            _selectedNav = NavItem.TripleTriad;
        }
    }

    private static float CalcSidebarWidth()
    {
        var style = ImGui.GetStyle();
        var maxLabel = 0f;
        foreach (var s in SidebarLabels)
        {
            var w = ImGui.CalcTextSize(Loc.T(s)).X;
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
        var status = info.ModuleStatus == ModuleDisplayNames.Idle
            ? Loc.T(ModuleDisplayNames.Idle)
            : Loc.T("Enabled: {0}", Loc.T(info.ModuleStatus));
        WindowName = $"Saucy  \u2022  {status}  \u2022  MGP {info.Mgp:N0}{delta}###Saucy";
    }

    public override void PostDraw()
    {
        _themeScope?.Dispose();
        _themeScope = null;
    }

    public override void Draw()
    {
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

        DrawTitleBarVersion(TitleBarButtons.Count, AllowPinning || AllowClickthrough);
    }

    private void DrawSidebar()
    {
        DrawSidebarHeader(Loc.T("MACHINES"));
        NavSelectable(Loc.T("Out on a Limb"), NavItem.OutOnALimb);
        NavSelectable(Loc.T("Cuff-a-Cur"), NavItem.CuffACur);

        ImGui.Dummy(new(0, 6));
        DrawSidebarHeader(Loc.T("GATES"));
        NavSelectable(Loc.T("Slice is Right"), NavItem.SliceIsRight);
        NavSelectable(Loc.T("Wind Blows"), NavItem.AnyWayTheWindBlows);
        NavSelectable(Loc.T("Air Force One"), NavItem.AirForceOne);

        ImGui.Dummy(new(0, 6));
        DrawSidebarHeader(Loc.T("OTHER GAMES"));
        NavSelectable(Loc.T("Triple Triad"), NavItem.TripleTriad);
        NavSelectable(Loc.T("Mini-Cactpot"), NavItem.MiniCactpot);
        NavSelectable(Loc.T("Jumbo Cactpot"), NavItem.JumboCactpot);

        ImGui.Dummy(new(0, 6));
        ImGui.Separator();
        NavSelectable(Loc.T("Stats"), NavItem.Stats);
        NavSelectable(Loc.T("About"), NavItem.About);
        if (C.ShowDebugUi)
        {
            NavSelectable(Loc.T("Debug"), NavItem.Debug);
        }

        var style = ImGui.GetStyle();
        var checkboxH = ImGui.GetFrameHeight();
        var creditH = ImGui.GetTextLineHeight();
        var bottomBlockH = style.ItemSpacing.Y + 1f + style.ItemSpacing.Y + checkboxH + style.ItemSpacing.Y
                           + checkboxH + style.ItemSpacing.Y + creditH;
        var targetY = ImGui.GetWindowHeight() - style.WindowPadding.Y - bottomBlockH;
        if (targetY > ImGui.GetCursorPosY())
        {
            ImGui.SetCursorPosY(targetY);
        }

        ImGui.Separator();
        DrawLanguageSelector();
        var on = C.SaucyThemeEnabled;
        if (ImGui.Checkbox($"{Loc.T("Saucy theme")}###SaucyTheme", ref on))
        {
            C.SaucyThemeEnabled = on;
            C.Save();
        }
        ImGui.TextDisabled(Loc.T("Designed by Wah"));
    }

    private static void DrawLanguageSelector()
    {
        string[] labels = [Loc.T("Automatic"), "English", "简体中文"];
        var index = C.UiLanguage switch
        {
            null => 0,
            UiLanguage.English => 1,
            _ => 2
        };

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##SaucyLanguage", ref index, labels, labels.Length))
        {
            C.UiLanguage = index switch
            {
                0 => null,
                1 => UiLanguage.English,
                _ => UiLanguage.ChineseSimplified
            };
            Loc.Apply(C.UiLanguage);
            Saucy.RefreshCommandHelp();
            C.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T("Language"));
        }
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
        DrawPanelHeader(Loc.T("Triple Triad"));
        ImGuiEx.EzTabBar("###Triad",
            ($"{Loc.T("Main")}###SaucyTriadMain", TriadSettingsUi.Draw, null, false),
            ($"{Loc.T("Cache")}###SaucyTriadCache", TriadCacheSettingsUi.Draw, null, false));
    }

    private static void DrawPanelHeader(string title, string? subtitle = null) =>
        SaucyTheme.DrawPanelHeader(title, subtitle);

    private void DrawDebugTab()
    {
        ImGuiLayout.DrawCollapsingSection(Loc.T("Gold Saucer gate"), "SaucyDebugGate", ImGuiTreeNodeFlags.DefaultOpen, () =>
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
                ImGui.TextDisabled(Loc.T("No active gate director."));
            }
        });

        ImGuiLayout.DrawCollapsingSection(Loc.T("Triple Triad NPC menu"), "SaucyDebugTriadMenu", ImGuiTreeNodeFlags.DefaultOpen, () =>
        {
            ImGui.Text($"Navigation active: {TriadMapNavigation.IsNavigationActive}");
            ImGui.Text($"Awaiting triad start: {TriadMapNavigation.IsAwaitingTriadStartDialog()}");

            var menuLines = new List<string>();
            SelectStringHelper.CollectTriadMenuDebugLines(menuLines);
            if (menuLines.Count == 0)
            {
                ImGui.TextDisabled(Loc.T("No select string menu open."));
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

        DrawJumboCactpotDebugPanel();
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

    private static void DrawTitleBarVersion(int customTitleBarButtonCount, bool showAdditionalOptionsButton)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
        {
            return;
        }

        var text = GetTitleBarVersionLabel();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var textSize = ImGui.CalcTextSize(text);
        var style = ImGui.GetStyle();
        var buttonSize = ImGui.GetFontSize();
        var spacing = style.ItemInnerSpacing.X;

        // Match Dalamud WindowHost.DrawTitleBarButtons: native close (+ collapse when menu is right),
        // then custom title bar buttons laid out from the right edge inward.
        var numNativeButtons = 1;
        if (style.WindowMenuButtonPosition == ImGuiDir.Right)
        {
            numNativeButtons++;
        }

        var numCustomButtons = customTitleBarButtonCount + (showAdditionalOptionsButton ? 1 : 0);
        var padRight = (numNativeButtons + numCustomButtons) * (buttonSize + spacing);

        var titleBarMaxX = windowPos.X + windowSize.X;
        var position = new Vector2(
            titleBarMaxX - padRight - textSize.X,
            windowPos.Y + style.FramePadding.Y);

        var color = ImGui.ColorConvertFloat4ToU32(
            SaucyTheme.Enabled
                ? SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled) with { W = 0.72f }
                : style.Colors[(int)ImGuiCol.TextDisabled]);

        // Window draw list keeps Saucy's z-order. Expand clip to the full window so title-bar
        // coordinates are not culled by the content-area clip active during Draw().
        var drawList = ImGui.GetWindowDrawList();
        var clipMax = windowPos + windowSize;
        drawList.PushClipRect(windowPos, clipMax, false);
        drawList.AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            position,
            color,
            text);
        drawList.PopClipRect();
    }

    private static string GetTitleBarVersionLabel()
    {
        var manifestVersion = Svc.PluginInterface.Manifest.AssemblyVersion;
        if (manifestVersion != null)
        {
            return "v" + FormatTitleBarVersion(manifestVersion);
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion != null ? "v" + FormatTitleBarVersion(assemblyVersion) : "v?.?.?.?";
    }

    private static string FormatTitleBarVersion(Version version) =>
        version.Revision >= 0 ? version.ToString(4) : version.ToString(3);
}
