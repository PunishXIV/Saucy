using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Saucy;

internal static unsafe partial class TitleBarVersion
{
    private const int DefaultPosOffset = 0x68;
    private const int DefaultSizeOffset = 0x70;

    private static int posOffset = DefaultPosOffset;
    private static int sizeOffset = DefaultSizeOffset;
    private static bool offsetsCalibrated;

    public static void DrawFromContext(int customTitleBarButtonCount, bool showAdditionalOptionsButton, string windowName)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
        {
            return;
        }

        TryCalibrateOffsets(windowName, windowPos, windowSize);
        DrawAt(windowPos, windowSize, customTitleBarButtonCount, showAdditionalOptionsButton);
    }

    public static void DrawFromWindowLookup(int customTitleBarButtonCount, bool showAdditionalOptionsButton, string windowName)
    {
        if (!TryResolveWindowRect(windowName, out var windowPos, out var windowSize))
        {
            return;
        }

        DrawAt(windowPos, windowSize, customTitleBarButtonCount, showAdditionalOptionsButton);
    }

    public static void ClearCache()
    {
        offsetsCalibrated = false;
        posOffset = DefaultPosOffset;
        sizeOffset = DefaultSizeOffset;
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
        var padRight = style.FramePadding.X + buttonSize + style.ItemInnerSpacing.X;

        if (style.WindowMenuButtonPosition == ImGuiDir.Right)
        {
            padRight += buttonSize + style.ItemInnerSpacing.X;
        }

        var extraButtons = customTitleBarButtonCount + (showAdditionalOptionsButton ? 1 : 0);
        padRight += extraButtons * (buttonSize + style.ItemInnerSpacing.X);
        padRight += style.ItemInnerSpacing.X;

        var position = new Vector2(
            windowPos.X + windowSize.X - padRight - textSize.X,
            windowPos.Y + style.FramePadding.Y);

        var color = ImGui.ColorConvertFloat4ToU32(
            SaucyTheme.Enabled
                ? SaucyTheme.ColorOr(SaucyTheme.BodyText, ImGuiCol.TextDisabled) with { W = 0.72f }
                : style.Colors[(int)ImGuiCol.TextDisabled]);

        ImGui.GetForegroundDrawList().AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            position,
            color,
            text);
    }

    private static void TryCalibrateOffsets(string windowName, Vector2 expectedPos, Vector2 expectedSize)
    {
        if (offsetsCalibrated || string.IsNullOrWhiteSpace(windowName))
        {
            return;
        }

        var window = FindWindowByName(windowName);
        if (window == null)
        {
            return;
        }

        var basePtr = (byte*)window;
        for (var offset = 0; offset < 512; offset += 4)
        {
            var candidatePos = ReadVector2(basePtr + offset);
            if (Vector2.Distance(candidatePos, expectedPos) > 1f)
            {
                continue;
            }

            var candidateSize = ReadVector2(basePtr + offset + 8);
            if (MathF.Abs(candidateSize.X - expectedSize.X) > 2f ||
                MathF.Abs(candidateSize.Y - expectedSize.Y) > 2f)
            {
                continue;
            }

            posOffset = offset;
            sizeOffset = offset + 8;
            offsetsCalibrated = true;
            return;
        }
    }

    private static bool TryResolveWindowRect(string windowName, out Vector2 windowPos, out Vector2 windowSize)
    {
        windowPos = default;
        windowSize = default;

        if (string.IsNullOrWhiteSpace(windowName))
        {
            return false;
        }

        var window = FindWindowByName(windowName);
        if (window == null)
        {
            return false;
        }

        var basePtr = (byte*)window;
        windowPos = ReadVector2(basePtr + posOffset);
        windowSize = ReadVector2(basePtr + sizeOffset);
        return windowSize.X > 0f && windowSize.Y > 0f;
    }

    private static Vector2 ReadVector2(byte* ptr) =>
        new(*(float*)ptr, *(float*)(ptr + 4));

    private static ImGuiWindow* FindWindowByName(string windowName)
    {
        var namePtr = Marshal.StringToCoTaskMemUTF8(windowName);
        try
        {
            return (ImGuiWindow*)igFindWindowByName((byte*)namePtr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(namePtr);
        }
    }

    [LibraryImport("cimgui")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial nint igFindWindowByName(byte* name);

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

    private struct ImGuiWindow;
}
