using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace Saucy.TripleTriad;

internal static class TriadSettingsUi
{
    private static int DraftMatchCount = 1;

    public static void Draw()
    {
        DraftMatchCount = Math.Max(1, C.TriadMatchCount);
        var runTargetNpc = TriadRunTarget.Resolve();

        var enabled = TriadRunSession.ModuleEnabled;
        if (ImGui.Checkbox("Enable automation", ref enabled))
        {
            if (enabled && !TriadNpcProximity.IsRelevantTriadNpcNearby())
            {
                var npcName = TriadNpcProximity.ResolveTriadNpcForProximityCheck()?.Name;
                DuoLog.Warning(string.IsNullOrEmpty(npcName)
                    ? "No Triple Triad NPC nearby (maybe get closer if in front of one)."
                    : $"No Triple Triad NPC nearby ({npcName}). Maybe get closer if you're in front of one.");
            }
            else
            {
                TriadRunSession.ModuleEnabled = enabled;
                if (enabled)
                {
                    CommitDraftMatchCount();
                    GoldSaucerArcadeMachineHelper.DisableConflictingModules();
                    TriadRunSession.BeginAutomationSession();
                    TriadCardFarmSession.SyncDisplay(runTargetNpc);
                    TriadAutomator.RunModule();
                }
                else
                {
                    TriadCardFarmSession.DeactivateSession(clearProgress: true);
                }
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Accepts match invites, selects a deck, and plays through rematches. Turn on before or during match prep.");

        var autoOpen = C.OpenAutomatically;
        if (ImGui.Checkbox("Open window when challenging an NPC", ref autoOpen))
        {
            C.OpenAutomatically = autoOpen;
            C.Save();
        }

        var collectionUi = C.CollectionUiEnabled;
        if (ImGui.Checkbox("Gold Saucer card search panels", ref collectionUi))
        {
            C.CollectionUiEnabled = collectionUi;
            C.Save();
        }

        ImGuiComponents.HelpMarker(
            "Shows a searchable card list beside the Gold Saucer card UI, including Edit Deck (TriadBuddy-style [No.1] ordering). " +
            "Also shows NPC search on the main card collection screen.");

        ImGui.Dummy(new(0, 4));

        SaucyTheme.DrawCard("Deck", null, DrawDeckBody);
        SaucyTheme.DrawCard("Run mode", null, DrawRunModeBody);
        SaucyTheme.DrawCard("Travel", "Map navigation", TriadTravelMountUi.Draw);
        SaucyTheme.DrawCard("Notifications", null, DrawNotificationsBody);
        SaucyTheme.DrawCard("Dependencies", "Optional integrations", TriadDependenciesUi.Draw);
    }

    private static void DrawDeckOptimizerSettings()
    {
        using var indent = ImRaii.PushIndent();
        var showOptimizerChatSpam = C.ShowOptimizerChatSpam;
        if (ImGui.Checkbox("Show deck automation chat spam", ref showOptimizerChatSpam))
        {
            C.ShowOptimizerChatSpam = showOptimizerChatSpam;
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Shows [Saucy] deck optimizer, deck selection, and profile-write messages in chat. " +
            "Does not hide the game's own lines (e.g. \"in play for the next match\").");

        DrawDeckOptimizerMaxThreadsSlider();
        DrawDeckOptimizerTimeoutSlider();

        TriadDeckOptimizerStatusUi.DrawInline();
    }

    private static void DrawDeckOptimizerMaxThreadsSlider()
    {
        var threads = Configuration.ClampDeckOptimizerMaxThreads(C.DeckOptimizerMaxThreads);
        var maxCores = Environment.ProcessorCount;
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Optimizer threads (0 = all)", ref threads, 0, maxCores, threads == 0 ? "All" : "%d"))
        {
            C.DeckOptimizerMaxThreads = Configuration.ClampDeckOptimizerMaxThreads(threads);
            C.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                $"Uses {SaucyParallelism.DeckOptimizerThreads} of {maxCores} logical cores for parallel deck tests.");
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Parallel threads while building an optimized deck (0 = all cores).");
    }

    private static void DrawDeckOptimizerTimeoutSlider()
    {
        var timeout = Math.Clamp(C.DeckOptimizerTimeoutMinutes, 1, 15);
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Optimizer timeout (min)", ref timeout, 1, 15, "%d min"))
        {
            C.DeckOptimizerTimeoutMinutes = Math.Clamp(timeout, 1, 15);
            C.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Cancels a background deck build after this long. If the build is not finished by deck select, Saucy falls back to your best profile deck. " +
            "Map navigation waits until a deck is ready.");
    }

    private static void DrawDeckBody()
    {
        if (TriadRun.profileGS.GetPlayerDecks()!.Count() == 0)
        {
            ImGui.TextWrapped("Challenge an NPC once to load your profile decks here.");
            return;
        }

        var useAutoDeck = C.UseSimmedDeck;
        if (ImGui.Checkbox("Auto-pick best deck", ref useAutoDeck))
        {
            C.UseSimmedDeck = useAutoDeck;
            C.Save();
            if (!useAutoDeck)
            {
                TriadRun.ResetDeckOptimizerState();
            }
        }

        var targetedNpc = TriadTargetNpc.FromWorldTarget();
        var autoPickNpc = targetedNpc ?? TriadRun.preGameNpc;
        if (C.UseSimmedDeck && autoPickNpc != null)
        {
            var autoPickSummary = TriadRun.GetAutoPickDeckSummary(autoPickNpc);
            if (!string.IsNullOrEmpty(autoPickSummary))
            {
                ImGui.SameLine();
                ImGui.TextDisabled(autoPickSummary);
            }
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Picks a deck at deck select. Default: highest opening win % among your profile decks.");

        if (C.UseSimmedDeck)
        {
            using var indent = ImRaii.PushIndent();

            if (autoPickNpc != null)
            {
                TriadRun.RefreshPrepRulesFromLive();
                TriadRun.EnsurePreviewEvalForNpc(autoPickNpc);
                if (TriadRun.ShouldBuildOptimizedDeck())
                {
                    TriadRun.EnsureOptimizedDeckPreviewEval(autoPickNpc);
                }
            }

            if (C.AlwaysBuildOptimizedDeck && C.UseCachedOptimizedDeckIfAvailable)
            {
                C.UseCachedOptimizedDeckIfAvailable = false;
                C.Save();
            }

            var useCachedDeck = C.UseCachedOptimizedDeckIfAvailable;
            if (ImGui.Checkbox("Use cached deck if available", ref useCachedDeck))
            {
                if (useCachedDeck)
                {
                    C.UseCachedOptimizedDeckIfAvailable = true;
                    C.AlwaysBuildOptimizedDeck = false;
                    if (!TriadCardFarmSession.IsModeActive())
                    {
                        TriadRun.CancelDeckOptimizerJob(userCancelled: true);
                    }
                }
                else
                {
                    C.UseCachedOptimizedDeckIfAvailable = false;
                    TriadRun.ResetDeckOptimizerState();
                }

                C.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "At match prep, loads a matching cached deck into profile slot 5 when one exists for this NPC and rules. " +
                "Auto-pick still sims your profile decks and picks the highest opening win %. Cannot be combined with Build optimized deck.");

            var alwaysBuild = C.AlwaysBuildOptimizedDeck;
            if (ImGui.Checkbox("Build optimized deck", ref alwaysBuild))
            {
                if (alwaysBuild)
                {
                    C.AlwaysBuildOptimizedDeck = true;
                    C.UseCachedOptimizedDeckIfAvailable = false;
                    TriadRun.ResetDeckOptimizerState();
                }
                else
                {
                    C.AlwaysBuildOptimizedDeck = false;
                    if (!TriadCardFarmSession.IsModeActive())
                    {
                        TriadRun.CancelDeckOptimizerJob(userCancelled: true);
                    }
                }

                C.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                "At match prep, builds a deck from your owned cards when no valid cache or existing \"NPC (Saucy)\" profile deck fits this NPC and rules. " +
                $"Rebuilds if you have gained {TriadOptimizedDeckCacheStore.RebuildAfterNewCardCount} or more new cards since the last build for that NPC. " +
                "Saves to profile slot 5 and selects it. Cannot be combined with Use cached deck.");

            if (C.AlwaysBuildOptimizedDeck)
            {
                DrawDeckOptimizerSettings();
            }

            return;
        }

        if (targetedNpc != null)
        {
            TriadRun.RefreshPrepRulesFromLive();
            TriadRun.EnsurePreviewEvalForNpc(targetedNpc);

            if (TriadRun.IsPreviewEvalPendingForNpc(targetedNpc))
            {
                ImGui.TextDisabled($"Target NPC: {targetedNpc.Name} (calculating win %…)");
            }
            else
            {
                ImGui.TextDisabled($"Target NPC: {targetedNpc.Name}");
            }

            ImGui.Spacing();
        }

        var selectedDeck = C.SelectedDeckIndex;
        var decks = TriadRun.profileGS.GetPlayerDecks()!;
        var previewName = "(none)";
        if (selectedDeck == Configuration.GameRecommendedDeckIndex)
        {
            previewName = "Game recommended";
        }
        else if (selectedDeck >= 0 && selectedDeck < decks.Count() && decks[selectedDeck] != null)
        {
            var rawName = decks[selectedDeck]!.name ?? string.Empty;
            var previewData = targetedNpc != null ? TriadRun.GetDeckPreviewData(targetedNpc, selectedDeck) : null;
            previewName = TriadDeckEvalDisplay.FormatDeckNameWithWinChance(rawName, previewData);
            if (string.IsNullOrEmpty(previewName))
            {
                previewName = "(none)";
            }
        }

        ImGui.TextUnformatted("Select deck");
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Game recommended uses FFXIV's built-in deck suggestion for the current match (not Saucy sims).");
        ImGui.SetNextItemWidth(300f * ImGuiHelpers.GlobalScale);
        using var deckCombo = ImRaii.Combo("##SaucyDeckSelect", previewName);
        if (deckCombo)
        {
            if (ImGui.Selectable("(none)##ClearDeckSelection", selectedDeck == -1))
            {
                C.SelectedDeckIndex = -1;
                C.Save();
            }

            if (ImGui.Selectable("Game recommended##GameRecommendedDeck",
                selectedDeck == Configuration.GameRecommendedDeckIndex))
            {
                C.SelectedDeckIndex = Configuration.GameRecommendedDeckIndex;
                C.Save();
            }

            foreach (var deck in decks)
            {
                if (deck is null)
                {
                    continue;
                }

                if (ImGui.Selectable(FormatDeckLabel(deck.id, deck.name, targetedNpc), deck.id == selectedDeck))
                {
                    C.SelectedDeckIndex = deck.id;
                    C.Save();
                }
            }
        }
    }

    private static void DrawRunModeBody()
    {
        ImGui.TextWrapped(
            "Choose when Saucy stops playing. On plugin load no option is selected and Saucy rematches until automation is disabled.");
        ImGui.Dummy(new(0, 4));

        if (ImGui.RadioButton("Fixed match count", TriadRunSession.PlayXTimes))
        {
            CommitDraftMatchCount();
            TriadRunSession.ApplyRunMode(TriadRunMode.PlayXTimes, matchCount: DraftMatchCount);
        }

        if (TriadRunSession.PlayXTimes)
        {
            using var subIndent = ImRaii.PushIndent();
            ImGui.Text("How many times:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(GoldSaucerRunSettingsUi.CompactCountInputWidth * ImGuiHelpers.GlobalScale);
            var count = Math.Max(1, C.TriadMatchCount);
            if (ImGui.InputInt("###TriadMatchCount", ref count) ||
                ImGui.IsItemDeactivatedAfterEdit())
            {
                ApplyMatchCount(count);
            }

            DraftMatchCount = Math.Max(1, count);

            var remaining = TriadRunSession.ModuleEnabled
                ? Math.Max(0, TriadRunSession.NumberOfTimes)
                : Math.Max(1, C.TriadMatchCount);
            ImGui.TextDisabled($"Matches left this session: {remaining}");
        }

        if (ImGui.RadioButton("Stop after first card drop", TriadRunSession.PlayUntilCardDrops))
        {
            TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAnyCard);
        }

        if (ImGui.RadioButton("Farm all NPC cards once", TriadRunSession.PlayUntilAllCardsDropOnce))
        {
            TriadRunSession.ApplyRunMode(TriadRunMode.PlayUntilAllCards, TriadRunTarget.Resolve());
        }

        if (TriadRunSession.NoRunModeSelected)
        {
            ImGui.TextDisabled("No stop condition — runs until automation is disabled.");
            ImGui.TextDisabled("Stops rematching while Duty Finder is ready.");
        }

        if (TriadRunSession.PlayUntilAllCardsDropOnce)
        {
            using var subIndent = ImRaii.PushIndent();

            TriadRunTarget.RefreshFromPrep();
            var runTargetNpc = TriadRunTarget.Resolve();
            var onMatchRegistration = uiReaderPrep.HasMatchRequestUI || TriadUiState.IsMatchRegistrationVisible();

            if (runTargetNpc != null)
            {
                ImGui.TextDisabled($"NPC: {TriadNpcDB.Get().FindByID(runTargetNpc.npcId).Name}");
                if (onMatchRegistration)
                {
                    ImGui.TextDisabled("(match registration open)");
                }
            }
            else if (onMatchRegistration)
            {
                ImGui.TextDisabled("NPC: reading match registration…");
            }
            else
            {
                ImGui.TextDisabled("NPC: open match registration to list missing cards.");
            }

            var onlyUnobtained = C.OnlyUnobtainedCards;
            if (ImGui.Checkbox("Missing cards only", ref onlyUnobtained))
            {
                C.OnlyUnobtainedCards = onlyUnobtained;
                C.Save();
                if (runTargetNpc != null)
                {
                    TriadCardFarmSession.StartTargets(runTargetNpc);
                }
            }

            if (runTargetNpc != null)
            {
                TriadCardFarmSession.SyncDisplay(runTargetNpc);
            }

            foreach (var entry in TriadCardFarmSession.TempCardsWonList)
            {
                var cardInfo = GameCardDB.Get().FindById((int)entry.Key);
                var cardName = cardInfo != null
                    ? TriadCardDB.Get().FindById(cardInfo.CardId)?.Name ?? $"Card #{entry.Key}"
                    : $"Card #{entry.Key}";
                ImGui.Text($"\u2022 {cardName} \u2014 {entry.Value}/1");
            }

            if (onlyUnobtained && runTargetNpc != null &&
                !TriadCardFarmSession.HasUnobtainedNpcRewards(runTargetNpc))
            {
                SaucyTheme.TextErrorWrapped("You already have every card from this NPC. Uncheck \"Missing cards only\" or choose a different NPC.");
            }
            else if (onlyUnobtained && TriadCardFarmSession.TempCardsWonList.Count == 0)
            {
                SaucyTheme.TextErrorWrapped("Start a match with an NPC to see which cards are still missing.");
            }
        }
    }

    private static void CommitDraftMatchCount()
    {
        if (!TriadRunSession.PlayXTimes)
        {
            return;
        }

        ApplyMatchCount(DraftMatchCount);
    }

    private static void ApplyMatchCount(int count) => TriadRunSession.SyncPlayXTimesSession(Math.Max(1, count), true);

    private static void DrawNotificationsBody()
    {
        var logOutAfterRun = C.LogOutAfterTriadRun;
        if (ImGui.Checkbox("Log out when run completes", ref logOutAfterRun))
        {
            C.LogOutAfterTriadRun = logOutAfterRun;
            C.Save();
        }
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Logs out of the game when a run finishes: fixed match count reaches zero, card drop mode triggers, or card farm completes.");

        var playSound = C.PlaySound;
        if (ImGui.Checkbox("Play sound when run completes", ref playSound))
        {
            C.PlaySound = playSound;
            C.Save();
        }

        if (playSound)
        {
            using var _ = ImRaii.PushIndent();
            DrawSoundPicker();
        }
    }

    private static void DrawSoundPicker()
    {
        ImGui.SetNextItemWidth(140f * ImGuiHelpers.GlobalScale);
        using var soundCombo = ImRaii.Combo("###SelectSound", C.SelectedSound);
        if (soundCombo)
        {
            var path = Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds");
            Directory.CreateDirectory(path);
            foreach (var file in new DirectoryInfo(path).GetFiles())
            {
                var name = Path.GetFileNameWithoutExtension(file.FullName);
                if (ImGui.Selectable(name, C.SelectedSound == name))
                {
                    C.SelectedSound = name;
                    C.Save();
                }
            }
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FolderOpen))
        {
            Process.Start("explorer.exe", Path.Combine(Svc.PluginInterface.AssemblyLocation.Directory!.FullName, "Sounds"));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open sound folder — drop MP3s here to add your own.");
        }
    }

    private static string FormatDeckLabel(int deckId, string deckName, TriadNpc? targetNpc)
    {
        if (string.IsNullOrWhiteSpace(deckName))
        {
            deckName = $"Deck {deckId + 1}";
        }

        if (targetNpc == null)
        {
            return deckName;
        }

        return TriadDeckEvalDisplay.FormatDeckNameWithWinChance(deckName, TriadRun.GetDeckPreviewData(targetNpc, deckId));
    }
}
