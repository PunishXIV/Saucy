using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using System.Reflection;
namespace Saucy;

internal static class TitleBarVersion
{
    public static void DrawFromContext(int customTitleBarButtonCount, bool showAdditionalOptionsButton)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
        {
            return;
        }

        DrawAt(windowPos, windowSize, customTitleBarButtonCount, showAdditionalOptionsButton);
    }

    private static void DrawAt(
        Vector2 windowPos,
        Vector2 windowSize,
        int customTitleBarButtonCount,
        bool showAdditionalOptionsButton)
    {
        var text = GetVersionLabel();
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

    private static string GetVersionLabel()
    {
        var manifestVersion = Svc.PluginInterface.Manifest.AssemblyVersion;
        if (manifestVersion != null)
        {
            return "v" + FormatVersion(manifestVersion);
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion != null ? "v" + FormatVersion(assemblyVersion) : "v?.?.?.?";
    }

    private static string FormatVersion(Version version) =>
        version.Revision >= 0 ? version.ToString(4) : version.ToString(3);
}
