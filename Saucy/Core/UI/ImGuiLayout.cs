using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using System;
using System.Numerics;
using static Saucy.Framework.ImGuiScopes;
namespace Saucy;

internal static class ImGuiLayout
{
    public static void DrawCollapsingSection(string title, ImGuiTreeNodeFlags flags, Action body)
    {
        using var header = ImRaii.Header(title, flags);
        if (!header)
        {
            return;
        }

        using var indent = ImRaii.PushIndent();
        body();
    }

    public static void DrawIconTextRow(FontAwesomeIcon icon, string? tooltip, Action onIconClick, Action drawText)
    {
        ImGui.AlignTextToFramePadding();
        var rowY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(rowY - ImGui.GetStyle().FramePadding.Y);
        if (ImGuiComponents.IconButton(icon))
        {
            onIconClick();
        }

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SetCursorPosY(rowY);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        drawText();
    }

    public static void DrawIconTextRow(FontAwesomeIcon icon, string? tooltip, Action drawText) =>
        DrawIconTextRow(icon, tooltip, static () => { }, drawText);

    public static void DrawStatusIconText(FontAwesomeIcon icon, Vector4 color, string text)
    {
        using (var iconFont = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(color, icon.ToIconString());
        }

        ImGui.SameLine();
        ImGui.TextColored(color, text);
    }

    public static PlainTextLinkScope PlainTextLink(Vector4? textColor = null) => new(textColor);

    public readonly struct PlainTextLinkScope : IDisposable
    {
        private readonly IDisposable _header;
        private readonly IDisposable _headerHovered;
        private readonly IDisposable _headerActive;
        private readonly IDisposable? _text;

        public PlainTextLinkScope(Vector4? textColor)
        {
            _header = ImRaii.PushColor(ImGuiCol.Header, 0);
            _headerHovered = ImRaii.PushColor(ImGuiCol.HeaderHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered));
            _headerActive = ImRaii.PushColor(ImGuiCol.HeaderActive, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            _text = textColor.HasValue ? ImRaii.PushColor(ImGuiCol.Text, textColor.Value) : null;
        }

        public void Dispose()
        {
            _text?.Dispose();
            _headerActive.Dispose();
            _headerHovered.Dispose();
            _header.Dispose();
        }
    }

    public readonly struct FullscreenOverlayScope : IDisposable
    {
        public bool Success { get; }

        private readonly IDisposable _pushId;
        private readonly IDisposable _padding;
        private readonly WindowScope _window;

        public FullscreenOverlayScope(string id, ImGuiWindowFlags flags)
        {
            _pushId = ImRaii.PushId(id);
            _padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGuiHelpers.SetNextWindowPosRelativeMainViewport(Vector2.Zero, ImGuiCond.None, Vector2.Zero);
            _window = Window($"overlay_{id}", flags);
            Success = _window.Success;
            if (Success)
            {
                ImGui.SetWindowSize(ImGui.GetIO().DisplaySize);
            }
        }

        public void Dispose()
        {
            _window.Dispose();
            _padding.Dispose();
            _pushId.Dispose();
        }
    }
}
