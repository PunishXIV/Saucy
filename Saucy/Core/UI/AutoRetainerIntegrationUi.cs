using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Saucy.IPC;
namespace Saucy;

internal static class AutoRetainerIntegrationUi
{
    public static void Draw()
    {
        var pause = C.PauseForAutoRetainer;
        if (ImGui.Checkbox("Pause when retainers are ready (bell nearby)", ref pause))
        {
            C.PauseForAutoRetainer = pause;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Cuff-a-Cur and Out on a Limb only. When a summoning bell is in range and AutoRetainer reports " +
            "retainers ready, Saucy finishes the current game, interacts with the bell, waits for AutoRetainer, then resumes.");

        if (!C.PauseForAutoRetainer)
        {
            return;
        }

        using var indent = ImRaii.PushIndent();

        if (AutoRetainerPause.IsHandling)
        {
            ImGui.TextDisabled("Waiting for AutoRetainer…");
        }
        else if (AutoRetainerPause.IsBlocking)
        {
            ImGui.TextDisabled("Retainers ready — finishing current game…");
        }
        else if (!AutoRetainerPause.HasBellInRange())
        {
            ImGui.TextDisabled("No summoning bell in range.");
        }

        ImGui.Dummy(new(0, 4));
        PluginDependenciesUi.Draw(
            "Optional plugin for retainer venture automation.",
            [AutoRetainerDependency()]);
    }

    private static PluginDependenciesUi.DependencyEntry AutoRetainerDependency() =>
        new(
            "AutoRetainer",
            IPCNames.AutoRetainer,
            "Collects and reassigns retainer ventures. Saucy waits for it to finish before resuming.",
            "https://love.puni.sh/ment.json",
            [],
            () => AutoRetainerIpc.IsInstalled);
}
