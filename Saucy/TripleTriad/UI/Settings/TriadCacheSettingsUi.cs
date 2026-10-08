using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Linq;
namespace Saucy.TripleTriad;

internal static class TriadCacheSettingsUi
{
    public static void Draw()
    {
        var views = TriadOptimizedDeckCacheStore.GetCharacterCacheViews();
        if (views.Count == 0)
        {
            if (Svc.ClientState.IsLoggedIn)
            {
                ImGui.TextDisabled(Loc.T("No cached decks yet."));
            }
            else
            {
                ImGui.TextDisabled(Loc.T("Log in to view cached decks."));
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
            0 => Loc.T("no cached decks"),
            1 => Loc.T("1 cached deck"),
            var _ => Loc.T("{0} cached decks", deckCount)
        };

        // The visible label carries the deck count and the UI language; keep the persisted
        // open/closed state keyed on the character instead.
        var header = $"{character.DisplayName} — {deckCountLabel}###SaucyDeckCache{character.ContentId}";
        var flags = character.IsCurrentCharacter ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        using var characterHeader = ImRaii.Header(header, flags);
        if (characterHeader)
        {
            DrawCharacterEntries(character);
        }
    }

    private static void DrawCharacterEntries(TriadOptimizedDeckCacheCharacterView character)
    {
        if (character.Entries.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("No optimized decks saved for this character yet."));
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
        var npcLabel = string.IsNullOrWhiteSpace(entry.NpcName) ? Loc.T("NPC {0}", entry.NpcId) : entry.NpcName;
        var rulesLabel = FormatRulesLabel(entry.SessionKey);
        var builtLabel = entry.BuiltUtcTicks > 0
            ? new DateTime(entry.BuiltUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g")
            : Loc.T("unknown time");
        var winLabel = entry.EstWinChance > 0f
            ? Loc.T(" · {0}% opening", (entry.EstWinChance * 100f).ToString("F0"))
            : string.Empty;

        // One key per shape rather than an interpolated hull: the separator and the
        // parentheses are the translator's to pick, and CJK wants （） over ASCII ().
        return string.IsNullOrEmpty(rulesLabel)
            ? Loc.T("{0}{1} · {2}", npcLabel, winLabel, builtLabel)
            : Loc.T("{0} ({1}){2} · {3}", npcLabel, rulesLabel, winLabel, builtLabel);
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
        using var clearDisabled = ImRaii.Disabled(!ctrlHeld);
        if (ImGui.Button(Loc.T("Clear deck cache for this character")))
        {
            TriadOptimizedDeckCacheStore.ClearActiveCharacter();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(
                ctrlHeld
                    ? Loc.T("Deletes OptimizedDeckCache.json for the logged-in character.")
                    : Loc.T("Hold Ctrl while clicking to clear the cache for this character."));
        }
    }
}
