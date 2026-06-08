#nullable disable
using Saucy.IPC;
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    public void OnNpcSelected(TriadNpc npc) => OnNpcSelected(npc, []);

    public void OnNpcSelected(
        TriadNpc npc,
        List<TriadGameModifier> regionMods,
        bool startOptimizer = false,
        bool forNavigation = false)
    {
        if (npc == null)
        {
            return;
        }

        if (forNavigation && TriadNpcUnlockHelper.TryReject(npc, out var _))
        {
            TriadMapNavigation.CancelActiveNavigation();
            return;
        }

        preGameNpc = npc;
        preGameMods = regionMods ?? [];
        lastGameNpc = npc;
        ApplyRunTargetNpc(npc, startOptimizer, forNavigation);
    }

    public void ResetRunTargetNpcSession() => lastAppliedRunTargetNpcId = -1;

    public void EnsureRunTargetNpcSynced(bool deckSelectScreen = false)
    {
        var onMatchRegistration = uiReaderPrep.HasMatchRequestUI || TriadUiState.IsMatchRegistrationVisible();
        var onDeckSelect = deckSelectScreen || uiReaderPrep.HasDeckSelectionUI || TriadUiState.IsPrepDeckSelectVisible();

        if (onMatchRegistration || onDeckSelect)
        {
            if (TrySyncNpcFromPrepState(uiReaderPrep.cachedState))
            {
                ApplyRunTargetNpc(preGameNpc!);
                return;
            }
        }

        if (!onMatchRegistration && !onDeckSelect)
        {
            var worldNpc = TriadTargetNpc.FromWorldTarget();
            if (worldNpc != null && TriadNpcProximity.IsPlayerNear(worldNpc))
            {
                ApplyRunTargetNpc(worldNpc);
                return;
            }
        }

        if (!deckSelectScreen && !onMatchRegistration)
        {
            uiReaderGame.RefreshFromVisibleAddon();

            if (uiReaderGame.currentState != null)
            {
                var npc = ResolveNpcForGame(uiReaderGame.currentState);
                if (npc != null)
                {
                    ApplyRunTargetNpc(npc);
                    return;
                }
            }
        }

        if (TrySyncNpcFromPrepState(uiReaderPrep.cachedState))
        {
            ApplyRunTargetNpc(preGameNpc!);
        }
    }

    private void ApplyRunTargetNpc(TriadNpc npc, bool startOptimizer = false, bool forNavigation = false)
    {
        if (npc == null)
        {
            return;
        }

        var npcChanged = lastAppliedRunTargetNpcId != npc.Id;
        var shouldManageDeck = C.UseSimmedDeck || TriadCardFarmSession.IsModeActive();

        preGameNpc = npc;
        lastGameNpc = npc;

        if (GameNpcDB.Get().mapNpcs.TryGetValue(npc.Id, out var npcInfo))
        {
            TriadCardFarmSession.SyncDisplay(npcInfo);
        }

        if (npcChanged)
        {
            lastAppliedRunTargetNpcId = npc.Id;
            preGameBestId = -1;
            var sessionKey = BuildOptimizerSessionKey(npc, preGameMods);
            if (!OptimizerInProgress ||
                !string.Equals(optimizerSessionKey, sessionKey, StringComparison.Ordinal))
            {
                ResetDeckOptimizer();
            }
        }

        var deferHeavyWork =
            (TriadMapNavigation.IsNavigationActive && !TriadMapNavigation.IsInNavigationTargetTerritory()) ||
            Vnavmesh.ShouldDeferDeckOptimizerWork();

        if (shouldManageDeck &&
            startOptimizer &&
            ShouldBuildOptimizedDeck() &&
            !TriadUiState.IsBoardVisible() &&
            !deferHeavyWork)
        {
            StartDeckOptimizer(npc, preGameMods, navigationRequest: forNavigation);
        }

        if (shouldManageDeck && C.UseSimmedDeck && !TriadUiState.IsBoardVisible() && !deferHeavyWork)
        {
            if (!ShouldBuildOptimizedDeck())
            {
                EnsurePreviewEvalForNpc(npc);
            }
            else
            {
                EnsureOptimizedDeckPreviewEval(npc);
            }
        }
    }

    public void RefreshPrepRulesFromLive()
    {
        if (TriadUiState.IsMatchRegistrationVisible())
        {
            uiReaderPrep.SyncMatchRegistrationFromLiveAddon();
            SyncPrepStateFromCachedUi();
        }
        else if (TriadUiState.IsPrepDeckSelectVisible())
        {
            uiReaderPrep.SyncDeckSelectFromLiveAddon();
            SyncPrepStateFromCachedUi();
        }
    }

    private void SyncPrepStateFromCachedUi()
    {
        if (string.IsNullOrWhiteSpace(uiReaderPrep.cachedState.npc))
        {
            return;
        }

        TrySyncNpcFromPrepState(uiReaderPrep.cachedState, out var modsChanged);
        if (modsChanged && preGameNpc != null && C.UseSimmedDeck)
        {
            OnPrepRulesUpdated(preGameNpc);
        }
    }

    public bool SyncPrepRulesFromState(UIStateTriadPrep state) =>
        TrySyncNpcFromPrepState(state, out var modsChanged) && modsChanged;

    private bool TrySyncNpcFromPrepState(UIStateTriadPrep state) =>
        TrySyncNpcFromPrepState(state, out var _);

    private bool TrySyncNpcFromPrepState(UIStateTriadPrep state, out bool regionModsChanged)
    {
        regionModsChanged = false;
        if (state == null || string.IsNullOrWhiteSpace(state.npc))
        {
            return false;
        }

        var parseCtx = new GameUIParser();
        var npc = parseCtx.ParseNpc(state.npc, false) ?? parseCtx.ParseNpcNameStart(state.npc, false);
        if (npc == null)
        {
            return false;
        }

        var newMods = ParsePrepRegionMods(state, parseCtx);
        regionModsChanged = preGameNpc == null ||
                            preGameNpc.Id != npc.Id ||
                            !RegionModsEqual(preGameMods, newMods);

        preGameNpc = npc;
        preGameMods = newMods;
        lastGameNpc = npc;

        if (regionModsChanged)
        {
            InvalidateDeckPreviewCacheLocked(npc);
        }

        return true;
    }

    private static bool RegionModsEqual(List<TriadGameModifier> left, List<TriadGameModifier> right) =>
        TriadOptimizerSessionKey.RegionModsEqual(left, right);

    private void InvalidateDeckPreviewCacheLocked(TriadNpc npc)
    {
        if (npc == null)
        {
            return;
        }

        lock (preGameLock)
        {
            var prefix = npc.Name + "|";
            var keysToRemove = new List<string>();
            foreach (var key in npcEvalSnapshots.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal) ||
                    string.Equals(key, npc.Name, StringComparison.Ordinal))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                npcEvalSnapshots.Remove(key);
            }

            previewEvalGeneration++;
            var staleFlights = new List<string>();
            foreach (var flightKey in previewEvalInFlight)
            {
                if (flightKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    staleFlights.Add(flightKey);
                }
            }

            foreach (var flightKey in staleFlights)
            {
                previewEvalInFlight.Remove(flightKey);
            }

            foreach (var deckData in preGameDecks.Values)
            {
                deckData?.chance = default;
            }
        }
    }

    public void OnPrepRulesUpdated(TriadNpc npc)
    {
        if (npc == null || !C.UseSimmedDeck)
        {
            return;
        }

        EnsurePreviewEvalForNpc(npc, preGameMods);
        if (ShouldBuildOptimizedDeck())
        {
            EnsureOptimizedDeckPreviewEval(npc);
        }

        if (ShouldBuildOptimizedDeck() || ShouldUseCachedOptimizedDeckIfAvailable())
        {
            EnsureExistingSaucyDeckForPrep();
        }
    }

    public void OnMatchPrepDetected(UIStateTriadPrep state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.npc))
        {
            return;
        }

        if (!TrySyncNpcFromPrepState(state))
        {
            var parseCtx = new GameUIParser();
            parseCtx.OnFailedNpcSilent(state.npc);
            return;
        }

        ApplyRunTargetNpc(preGameNpc!);
    }

    private static List<TriadGameModifier> ParsePrepRegionMods(UIStateTriadPrep state, GameUIParser parseCtx)
    {
        var mods = new List<TriadGameModifier>();
        if (state?.rules == null)
        {
            return mods;
        }

        foreach (var ruleIdx in new[] { TriadPrepRequestReader.RegionalRuleSlot0, TriadPrepRequestReader.RegionalRuleSlot1 })
        {
            if (ruleIdx < 0 || ruleIdx >= state.rules.Length)
            {
                continue;
            }

            var ruleOb = parseCtx.ParseModifier(state.rules[ruleIdx], false);
            if (ruleOb is not null and not TriadGameModifierNone)
            {
                mods.Add(ruleOb);
            }
        }

        return mods;
    }

    public void UpdateDecks(UIStateTriadPrep state)
    {
        var parseCtx = new GameUIParser();

        var newPreGameNpc = string.IsNullOrWhiteSpace(state.npc)
            ? preGameNpc
            : parseCtx.ParseNpc(state.npc, false) ?? parseCtx.ParseNpcNameStart(state.npc, false);
        if (newPreGameNpc == null && !string.IsNullOrWhiteSpace(state.npc))
        {
            parseCtx.OnFailedNpcSilent(state.npc);
        }

        var newPreGameMods = ParsePrepRegionMods(state, parseCtx);

        lastGameNpc = newPreGameNpc ?? preGameNpc;

        var canReadFromProfile = profileGS != null && !profileGS.HasErrors;
        var canProcessDecks = !parseCtx.HasErrors &&
                              (canReadFromProfile || (state.decks.Count > 0 && !canReadFromProfile));

        if (canProcessDecks)
        {
            var profileDecks = canReadFromProfile ? profileGS.GetPlayerDecks() : null;
            var numDecks = (profileDecks != null) ? profileDecks.Length : state.decks.Count;
            var newPreGameDecks = new Dictionary<int, DeckData>();

            TriadDeck anyDeckOb = null;
            for (var deckIdx = 0; deckIdx < numDecks; deckIdx++)
            {
                parseCtx.Reset();

                var deckData = (profileDecks != null)
                    ? ParseDeckDataFromProfile(profileDecks[deckIdx], parseCtx)
                    : ParseDeckDataFromUI(state.decks[deckIdx], parseCtx);

                if (!parseCtx.HasErrors && deckData != null)
                {
                    newPreGameDecks.Add(deckData.id, deckData);
                    anyDeckOb = deckData.solverDeck;
                }
            }

            var needsDeckEval = IsDeckEvalDataChanged(newPreGameNpc, newPreGameMods, newPreGameDecks);
            if (!needsDeckEval)
            {
                Logger.WriteLine("ignore deck eval, same input data");
                return;
            }

            var preserveOptimizedDeck = HasOptimizedDeckApplied && optimizerTargetDeckId >= 0;

            preGameNpc = newPreGameNpc ?? preGameNpc;
            preGameMods = newPreGameMods;
            preGameDecks = newPreGameDecks;

            if (preGameNpc == null)
            {
                return;
            }

            if (preGameNpc.Deck == null || preGameDecks == null || preGameDecks.Count == 0)
            {
                return;
            }

            if (preserveOptimizedDeck)
            {
                preGameBestId = optimizerTargetDeckId;
                return;
            }

            preGameBestId = -1;

            anyDeckOb ??= new(PlayerSettingsDB.Get().starterCards);
            DebugScreenMemory.UpdatePlayerDeck(anyDeckOb);

            var shouldManageDeck = C.UseSimmedDeck || TriadCardFarmSession.IsModeActive();
            if (shouldManageDeck && newPreGameNpc != null && !parseCtx.HasErrors)
            {
                if (ShouldBuildOptimizedDeck())
                {
                    lock (preGameLock)
                    {
                        TryEnsureOptimizedDeckForPrepLocked();
                    }
                }
                else
                {
                    if (OptimizerInProgress)
                    {
                        CancelDeckOptimizerJob(userCancelled: true);
                    }

                    if (C.UseSimmedDeck)
                    {
                        EnsurePreviewEvalForNpc(newPreGameNpc, newPreGameMods);
                    }
                }
            }
            else if (!shouldManageDeck)
            {
                ResetDeckOptimizer();
            }
        }
        else if (!C.UseSimmedDeck && !TriadCardFarmSession.IsModeActive())
        {
            ResetDeckOptimizer();
        }
    }
}
