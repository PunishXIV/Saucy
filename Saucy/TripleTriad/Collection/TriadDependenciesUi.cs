using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using ECommons;
using ECommons.Reflection;
using Saucy.IPC;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Saucy.TripleTriad;

internal static class TriadDependenciesUi
{
    private static readonly DependencyEntry[] Dependencies =
    [
        new(
            "vnavmesh",
            "vnavmesh",
            "Walk to Triple Triad NPCs from Saucy map links after you arrive in the zone.",
            "https://puni.sh/api/repository/veyn",
            [],
            VnavmeshInterop.Refresh,
            () => VnavmeshInterop.IsInstalled),
        new(
            "Lifestream",
            "Lifestream",
            "Teleport to the nearest aetheryte before vnavmesh when the NPC is far away or in another zone.",
            "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json",
            [
                "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json",
            ],
            LifestreamInterop.Refresh,
            () => LifestreamInterop.IsInstalled),
        new(
            "Questionable",
            "Questionable",
            "Start unlock quests for Triple Triad NPCs directly from Saucy card and NPC search.",
            "https://love.puni.sh/ment.json",
            [],
            QuestionableInterop.Refresh,
            () => QuestionableInterop.IsInstalled),
    ];

    public static void Draw()
    {
        IpcSubscriptions.Refresh();

        ImGui.TextWrapped(
            "Optional plugins for TriadBuddy in Saucy: path to NPCs on the map, teleport when needed, and start unlock quests.");
        ImGui.Dummy(new Vector2(0, 4));

        foreach (var entry in Dependencies)
        {
            DrawDependency(entry);
            ImGui.Dummy(new Vector2(0, 6));
        }
    }

    private static void DrawDependency(DependencyEntry entry)
    {
        entry.Refresh();

        using var id = ImRaii.PushId(entry.InternalName);
        var state = GetState(entry.InternalName, entry.IsReady);

        ImGui.TextColored(SaucyTheme.ColorOr(SaucyTheme.SectionTitle, ImGuiCol.Text), entry.DisplayName);
        ImGui.TextWrapped(entry.Description);

        ImGui.Spacing();
        DrawStatus(state);

        if (state == DependencyState.Ready)
            return;

        var repoAdded = IsRepositoryAdded(entry);
        var showAddRepo = !repoAdded && state == DependencyState.NotInstalled;
        var showInstall = state == DependencyState.NotInstalled;

        if (!showAddRepo && !showInstall)
            return;

        ImGui.Spacing();
        var firstButton = true;

        if (showAddRepo)
        {
            if (ImGui.Button("Add repository"))
                TryAddRepository(entry);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Add {entry.PrimaryRepositoryUrl} to Custom Plugin Repositories.");

            firstButton = false;
        }

        if (showInstall)
        {
            if (!firstButton)
                ImGui.SameLine();

            if (ImGui.Button("Install plugin"))
                TryInstallPlugin(entry);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Install {entry.DisplayName} from its plugin repository.");
        }
    }

    private static bool IsRepositoryAdded(DependencyEntry entry)
    {
        foreach (var url in entry.RepositoryUrls)
        {
            if (DalamudReflector.HasRepo(url))
                return true;
        }

        return false;
    }

    private static void TryAddRepository(DependencyEntry entry)
    {
        if (DalamudReflector.HasRepo(entry.PrimaryRepositoryUrl))
        {
            Svc.Chat.Print($"[Saucy] {entry.DisplayName} repository is already added.");
            return;
        }

        DalamudReflector.AddRepo(entry.PrimaryRepositoryUrl, true);
        DalamudReflector.SaveDalamudConfig();
        DalamudReflector.ReloadPluginMasters();
        Svc.Chat.Print($"[Saucy] Added {entry.DisplayName} repository.");
    }

    private static void TryInstallPlugin(DependencyEntry entry)
    {
        var repoUrl = entry.RepositoryUrls.FirstOrDefault(DalamudReflector.HasRepo) ?? entry.PrimaryRepositoryUrl;
        _ = InstallPluginAsync(entry, repoUrl);
    }

    private static async Task InstallPluginAsync(DependencyEntry entry, string repoUrl)
    {
        if (await DalamudReflector.AddPlugin(repoUrl, entry.InternalName))
            Svc.Chat.Print($"[Saucy] Installed {entry.DisplayName}.");
        else
            Svc.Chat.PrintError($"[Saucy] Could not install {entry.DisplayName}. Check the plugin installer for details.");
    }

    private static void DrawStatus(DependencyState state)
    {
        switch (state)
        {
            case DependencyState.Ready:
                DrawStatusLine(FontAwesomeIcon.Check, ImGuiColors.HealerGreen, "Installed");
                break;
            case DependencyState.InstalledNotLoaded:
                DrawStatusLine(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudYellow, "Installed but not loaded");
                ImGui.SameLine();
                if (ImGui.Button("Open installer"))
                    Svc.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, string.Empty);
                break;
            default:
                DrawStatusLine(FontAwesomeIcon.Times, ImGuiColors.DalamudRed, "Not installed");
                break;
        }
    }

    private static void DrawStatusLine(FontAwesomeIcon icon, Vector4 color, string text)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(color, icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(color, text);
    }

    private static DependencyState GetState(string internalName, Func<bool> isReady)
    {
        if (isReady())
            return DependencyState.Ready;

        var plugin = Svc.PluginInterface.InstalledPlugins.FirstOrDefault(x => x.InternalName == internalName);
        if (plugin != null)
            return DependencyState.InstalledNotLoaded;

        return DependencyState.NotInstalled;
    }

    private enum DependencyState
    {
        NotInstalled,
        InstalledNotLoaded,
        Ready,
    }

    private sealed record DependencyEntry(
        string DisplayName,
        string InternalName,
        string Description,
        string PrimaryRepositoryUrl,
        string[] AlternateRepositoryUrls,
        Action Refresh,
        Func<bool> IsReady)
    {
        public string InstallerSearch => DisplayName;

        public string[] RepositoryUrls =>
            AlternateRepositoryUrls.Length == 0
                ? [PrimaryRepositoryUrl]
                : [PrimaryRepositoryUrl, ..AlternateRepositoryUrls];
    }
}
