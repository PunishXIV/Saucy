using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace Saucy;

public struct BannerInfo
{
    public int Mgp;
    public int SessionDelta;
    public string ModuleStatus;
}

internal static class SaucyTheme
{
    private static Vector4 Rgb(byte r, byte g, byte b, float a = 1f)
        => new(r / 255f, g / 255f, b / 255f, a);

    // Colors
    private static readonly Vector4 BgDarkest = Rgb(0x1A, 0x0B, 0x2E);     // main window
    private static readonly Vector4 BgPanel = Rgb(0x2A, 0x11, 0x45);       // inner boxes, tabs
    private static readonly Vector4 BgInput = Rgb(0x3D, 0x1B, 0x5C);       // text fields
    private static readonly Vector4 BgInputHover = Rgb(0x4D, 0x24, 0x70);
    private static readonly Vector4 BgInputActive = Rgb(0x5D, 0x2D, 0x85);

    private static readonly Vector4 Highlight = Rgb(0x7B, 0x2D, 0x8E);     // active tabs, buttons
    private static readonly Vector4 HighlightHover = Rgb(0x9B, 0x3F, 0xB0);
    private static readonly Vector4 HighlightActive = Rgb(0xB8, 0x5C, 0xCC);

    private static readonly Vector4 Accent = Rgb(0xE5, 0xB8, 0x4B);        // gold — borders
    private static readonly Vector4 AccentSoft = Rgb(0xE5, 0xB8, 0x4B, 0.5f);
    private static readonly Vector4 AccentBright = Rgb(0xFF, 0xD9, 0x68);  // gold — titles

    private static readonly Vector4 Signal = Rgb(0xFF, 0x4B, 0x9E);        // pink — checkmarks, jackpot

    private static readonly Vector4 TextPrimary = Rgb(0xF4, 0xE4, 0xBC);
    private static readonly Vector4 TextDim = Rgb(0xB8, 0xA5, 0x80);
    private static readonly Vector4 None = new(0, 0, 0, 0);

    // Names other files use
    public static Vector4 CardBorder { get; } = Accent;
    public static Vector4 CardSeparator { get; } = AccentSoft;
    public static Vector4 SectionTitle { get; } = AccentBright;
    public static Vector4 ColumnHeader { get; } = Accent;
    public static Vector4 BodyText { get; } = TextPrimary;
    public static Vector4 BodyTextAccent { get; } = AccentBright;

    // Corner roundness (px)
    private const float WindowRound = 4f;
    private const float ChildRound = 3f;
    private const float FrameRound = 2f;
    private const float PopupRound = 3f;
    private const float ScrollbarRound = 2f;
    private const float GrabRound = 2f;
    private const float TabRound = 3f;
    private const float BorderSize = 1f;

    // Card spacing (px)
    private const float CardPad = 8f;
    private const float CardGapBetween = 6f;
    private const float CardSepGapAbove = 3f;
    private const float CardSepGapBelow = 4f;
    private const float CardBorderRound = 3f;
    private const float CardBorderWeight = 1f;

    public static bool Enabled => C.SaucyThemeEnabled;

    public static Vector4 ColorOr(Vector4 themeColor, ImGuiCol fallback)
        => Enabled ? themeColor : ImGui.GetStyle().Colors[(int)fallback];

    public static uint ColorU32Or(Vector4 themeColor, ImGuiCol fallback)
        => Enabled ? ImGui.GetColorU32(themeColor) : ImGui.GetColorU32(fallback);

    private static int _colorPushes;
    private static int _varPushes;

    private static void PushColor(ImGuiCol c, Vector4 v)
    {
        ImGui.PushStyleColor(c, v);
        _colorPushes++;
    }

    private static void PushVar(ImGuiStyleVar v, float x)
    {
        ImGui.PushStyleVar(v, x);
        _varPushes++;
    }

    public static void Push()
    {
        _colorPushes = 0;
        _varPushes = 0;

        // Text
        PushColor(ImGuiCol.Text, TextPrimary);
        PushColor(ImGuiCol.TextDisabled, TextDim);
        PushColor(ImGuiCol.TextSelectedBg, Signal);

        // Backgrounds and window border
        PushColor(ImGuiCol.WindowBg, BgDarkest);
        PushColor(ImGuiCol.ChildBg, BgPanel);
        PushColor(ImGuiCol.PopupBg, BgDarkest);
        PushColor(ImGuiCol.Border, Accent);
        PushColor(ImGuiCol.BorderShadow, None);

        // Inputs
        PushColor(ImGuiCol.FrameBg, BgInput);
        PushColor(ImGuiCol.FrameBgHovered, BgInputHover);
        PushColor(ImGuiCol.FrameBgActive, BgInputActive);
        PushColor(ImGuiCol.CheckMark, Signal);
        PushColor(ImGuiCol.SliderGrab, Accent);
        PushColor(ImGuiCol.SliderGrabActive, AccentBright);

        // Title bar
        PushColor(ImGuiCol.TitleBg, BgPanel);
        PushColor(ImGuiCol.TitleBgActive, Highlight);
        PushColor(ImGuiCol.TitleBgCollapsed, BgPanel);
        PushColor(ImGuiCol.MenuBarBg, BgPanel);

        // Scrollbar
        PushColor(ImGuiCol.ScrollbarBg, BgDarkest);
        PushColor(ImGuiCol.ScrollbarGrab, Highlight);
        PushColor(ImGuiCol.ScrollbarGrabHovered, HighlightHover);
        PushColor(ImGuiCol.ScrollbarGrabActive, HighlightActive);

        // Buttons
        PushColor(ImGuiCol.Button, BgPanel);
        PushColor(ImGuiCol.ButtonHovered, Highlight);
        PushColor(ImGuiCol.ButtonActive, HighlightHover);

        // Collapsing headers
        PushColor(ImGuiCol.Header, Highlight);
        PushColor(ImGuiCol.HeaderHovered, HighlightHover);
        PushColor(ImGuiCol.HeaderActive, HighlightActive);

        // Separators
        PushColor(ImGuiCol.Separator, AccentSoft);
        PushColor(ImGuiCol.SeparatorHovered, Accent);
        PushColor(ImGuiCol.SeparatorActive, AccentBright);

        // Resize handle
        PushColor(ImGuiCol.ResizeGrip, Highlight);
        PushColor(ImGuiCol.ResizeGripHovered, HighlightHover);
        PushColor(ImGuiCol.ResizeGripActive, HighlightActive);

        // Tabs
        PushColor(ImGuiCol.Tab, BgPanel);
        PushColor(ImGuiCol.TabHovered, Highlight);
        PushColor(ImGuiCol.TabActive, Highlight);
        PushColor(ImGuiCol.TabUnfocused, BgPanel);
        PushColor(ImGuiCol.TabUnfocusedActive, BgInput);

        // Roundness and border thickness
        PushVar(ImGuiStyleVar.WindowRounding, WindowRound);
        PushVar(ImGuiStyleVar.ChildRounding, ChildRound);
        PushVar(ImGuiStyleVar.FrameRounding, FrameRound);
        PushVar(ImGuiStyleVar.PopupRounding, PopupRound);
        PushVar(ImGuiStyleVar.ScrollbarRounding, ScrollbarRound);
        PushVar(ImGuiStyleVar.GrabRounding, GrabRound);
        PushVar(ImGuiStyleVar.TabRounding, TabRound);
        PushVar(ImGuiStyleVar.WindowBorderSize, BorderSize);
        PushVar(ImGuiStyleVar.FrameBorderSize, BorderSize);
    }

    public static void Pop()
    {
        if (_varPushes > 0)
        {
            ImGui.PopStyleVar(_varPushes);
            _varPushes = 0;
        }
        if (_colorPushes > 0)
        {
            ImGui.PopStyleColor(_colorPushes);
            _colorPushes = 0;
        }
    }

    // Bordered box
    public static void DrawCard(string name, string? subtitle, Action body)
    {
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail().X;
        var startScreen = ImGui.GetCursorScreenPos();

        ImGui.Dummy(new Vector2(0, CardPad));
        using var indent = ImRaii.PushIndent(CardPad);

        ImGui.TextColored(ColorOr(SectionTitle, ImGuiCol.Text), name);
        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(" \u2014 " + subtitle);
        }

        var sepY = ImGui.GetCursorScreenPos().Y + CardSepGapAbove;
        drawList.AddLine(
            new Vector2(startScreen.X + CardPad, sepY),
            new Vector2(startScreen.X + avail - CardPad, sepY),
            ColorU32Or(CardSeparator, ImGuiCol.Separator), CardBorderWeight);
        ImGui.Dummy(new Vector2(0, CardSepGapBelow));

        body();

        indent.Dispose();
        ImGui.Dummy(new Vector2(0, CardPad));

        var endY = ImGui.GetCursorScreenPos().Y;
        drawList.AddRect(
            new Vector2(startScreen.X, startScreen.Y),
            new Vector2(startScreen.X + avail, endY),
            ColorU32Or(CardBorder, ImGuiCol.Border), CardBorderRound);

        ImGui.Dummy(new Vector2(0, CardGapBetween));
    }
}
