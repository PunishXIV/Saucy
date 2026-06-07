using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Linq;
namespace Saucy.TripleTriad;

internal static class TriadCacheSettingsUi
{
    public static void Draw()
    {
        ImGui.TextDisabled("Per character optimized decks");

        ImGui.Dummy(new(0, 4));

        var views = TriadOptimizedDeckCacheStore.GetCharacterCacheViews();
        if (views.Count == 0)
        {
            if (Svc.ClientState.IsLoggedIn)
            {
                ImGui.TextDisabled("No cached decks yet.");
            }
            else
            {
                ImGui.TextDisabled("Log in to view cached decks.");
            }
        }
        else
        {
            var listHeight = Math.Clamp(views.Count * 28 + views.Sum(v => v.Entries.Count * 18), 120f, 320f);
            using var scroll = ImRaii.Child("TriadCacheList", new(0, listHeight), true);
            if (scroll)
            {
                foreach (var character in views)
                {
                    DrawCharacterCache(character);
                    ImGui.Dummy(new(0, 4));
                }
            }
        }

        ImGui.Dummy(new(0, 4));
        DrawClearButton();
    }

    private static void DrawCharacterCache(TriadOptimizedDeckCacheCharacterView character)
    {
        var deckCount = character.Entries.Count;
        var deckCountLabel = deckCount switch
        {
            0 => "no cached decks",
            1 => "1 cached deck",
            var _ => $"{deckCount} cached decks"
        };

        var header = $"{character.DisplayName} — {deckCountLabel}";
        var flags = character.IsCurrentCharacter ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (ImGui.CollapsingHeader(header, flags))
        {
            DrawCharacterEntries(character);
        }
    }

    private static void DrawCharacterEntries(TriadOptimizedDeckCacheCharacterView character)
    {
        if (character.Entries.Count == 0)
        {
            ImGui.TextDisabled("No optimized decks saved for this character yet.");
            return;
        }

        using var indent = ImRaii.PushIndent();
        foreach (var entry in character.Entries)
        {
            ImGui.BulletText(FormatCacheEntryLine(entry));
        }
    }

    private static string FormatCacheEntryLine(TriadOptimizedDeckCacheEntry entry)
    {
        var npcLabel = string.IsNullOrWhiteSpace(entry.NpcName) ? $"NPC {entry.NpcId}" : entry.NpcName;
        var rulesLabel = FormatRulesLabel(entry.SessionKey);
        var builtLabel = entry.BuiltUtcTicks > 0
            ? new DateTime(entry.BuiltUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g")
            : "unknown time";
        var winLabel = entry.EstWinChance > 0f ? $" · {entry.EstWinChance * 100f:F0}% opening" : string.Empty;

        return string.IsNullOrEmpty(rulesLabel)
            ? $"{npcLabel}{winLabel} · {builtLabel}"
            : $"{npcLabel} ({rulesLabel}){winLabel} · {builtLabel}";
    }

    private static string FormatRulesLabel(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey))
        {
            return string.Empty;
        }

        var parts = sessionKey.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
        {
            return string.Empty;
        }

        return string.Join(", ", parts.Skip(1));
    }

    private static void DrawClearButton()
    {
        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        using (ImRaii.Disabled(!ctrlHeld))
        {
            if (ImGui.Button("Clear deck cache for this character"))
            {
                TriadOptimizedDeckCacheStore.ClearActiveCharacter();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                ctrlHeld
                    ? "Deletes OptimizedDeckCache.json for the logged-in character."
                    : "Hold Ctrl while clicking to clear the cache for this character.");
        }
    }
}
