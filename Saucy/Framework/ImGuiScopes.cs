using Dalamud.Bindings.ImGui;
using System;
namespace Saucy.Framework;

/// <summary>
///     RAII helpers for ImGui scopes not exposed on Dalamud's ImRaii (e.g. root overlay windows).
/// </summary>
internal static class ImGuiScopes
{
    public static WindowScope Window(string name, ImGuiWindowFlags flags) => new(name, flags);

    public readonly struct WindowScope : IDisposable
    {
        public bool Success { get; }

        public WindowScope(string name, ImGuiWindowFlags flags) => Success = ImGui.Begin(name, flags);

        public void Dispose()
        {
            if (Success)
            {
                ImGui.End();
            }
        }
    }
}
