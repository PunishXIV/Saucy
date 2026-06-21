#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.UI;

public partial class TriadSession
{
    private void SchedulePreviewEvalForDeck(
        string cacheKey,
        DeckData deckData,
        TriadNpc npc,
        IEnumerable<TriadGameModifier> regionMods,
        bool syncPreGameDeck,
        bool forceResim = false)
    {
        if (string.IsNullOrEmpty(cacheKey) || deckData?.solverDeck == null || npc == null)
        {
            return;
        }

        var flightKey = $"{cacheKey}:{deckData.id}";
        int evalGeneration;
        lock (_preGameLock)
        {
            if (_previewEvalInFlight.Contains(flightKey))
            {
                return;
            }

            if (!forceResim &&
                npcEvalSnapshots.TryGetValue(cacheKey, out var deckMap) &&
                deckMap.TryGetValue(deckData.id, out var existing) &&
                existing.chance.numGames > 0 &&
                !IsStaleZeroPreview(existing.chance))
            {
                return;
            }

            _previewEvalInFlight.Add(flightKey);
            evalGeneration = _previewEvalGeneration;
            if (!npcEvalSnapshots.TryGetValue(cacheKey, out deckMap))
            {
                deckMap = [];
                npcEvalSnapshots[cacheKey] = deckMap;
            }

            deckMap[deckData.id] = deckData;
        }

        var rules = regionMods ?? ResolvePreviewRulesForNpc(npc);
        var capturedDeck = deckData;

        Task.Run(() =>
        {
            try
            {
                if (TriadUiState.IsBoardVisible())
                {
                    return;
                }

                var chance = TriadDeckEvaluator.EvaluateOpeningMoveThrottled(
                    capturedDeck.solverDeck,
                    npc,
                    rules);

                lock (_preGameLock)
                {
                    if (evalGeneration != _previewEvalGeneration)
                    {
                        return;
                    }

                    capturedDeck.chance = chance;

                    if (npcEvalSnapshots.TryGetValue(cacheKey, out var deckMap))
                    {
                        deckMap[capturedDeck.id] = capturedDeck;
                    }

                    if (syncPreGameDeck && preGameDecks.TryGetValue(capturedDeck.id, out var preGameDeck))
                    {
                        preGameDeck.chance = capturedDeck.chance;
                    }

                    if (C.UseSimmedDeck && !ShouldBuildOptimizedDeck())
                    {
                        RefreshPreGameBestFromPreviewEval(npc, rules);
                    }
                }

                if (chance.numGames > 0)
                {
                    var mods = regionMods as List<TriadGameModifier> ?? preGameMods;
                    var deckId = capturedDeck.id;
                    var solverDeck = capturedDeck.solverDeck;
                    var winChance = chance.winChance;
                    var capturedGeneration = evalGeneration;
                    Svc.Framework.Run(() =>
                    {
                        lock (_preGameLock)
                        {
                            if (capturedGeneration != _previewEvalGeneration)
                            {
                                return;
                            }
                        }

                        TryRefreshGeneratedDeckCacheEntryLocked(
                            npc,
                            mods,
                            deckId,
                            solverDeck,
                            winChance);
                    });
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "[Saucy] Preview deck eval failed for deck {DeckId} vs {Npc}", capturedDeck.id, npc.Name);
            }
            finally
            {
                lock (_preGameLock)
                {
                    _previewEvalInFlight.Remove(flightKey);
                }
            }
        });
    }

    private DeckData ParseDeckDataFromProfile(TriadProfileDeckReader.PlayerDeck deckOb, GameUIParser ctx)
    {
        if (deckOb == null)
        {
            return null;
        }

        var cards = new TriadCard[5];
        for (var cardIdx = 0; cardIdx < 5; cardIdx++)
        {
            var cardId = deckOb.cardIds[cardIdx];
            if (cardId <= 0)
            {
                return null;
            }

            cards[cardIdx] = ctx.cards.FindById(cardId);
            if (cards[cardIdx] == null)
            {
                ctx.OnFailedCard($"id:{cardId}");
                return null;
            }
        }

        return new()
        {
            id = deckOb.id, name = deckOb.name, solverDeck = new(cards)
        };
    }

    private DeckData ParseDeckDataFromUI(UIStateTriadPrepDeck deckOb, GameUIParser ctx)
    {
        var numValidCards = 0;
        for (var cardIdx = 0; cardIdx < 5; cardIdx++)
        {
            numValidCards += string.IsNullOrEmpty(deckOb.cardTexPaths[cardIdx]) ? 0 : 1;
        }

        DeckData deckData = null;
        if (numValidCards == 5)
        {
            deckData = new()
            {
                id = deckOb.id, name = deckOb.name
            };

            var cards = new TriadCard[5];
            for (var cardIdx = 0; cardIdx < 5; cardIdx++)
            {
                cards[cardIdx] = ctx.ParseCard(deckOb.cardTexPaths[cardIdx]);
            }

            deckData.solverDeck = ctx.HasErrors ? null : new TriadDeck(cards);
        }

        return deckData;
    }

    private static bool IsStaleZeroPreview(SolverResult chance) =>
        chance.numGames > 0 &&
        chance.winChance <= 0f &&
        chance.drawChance <= 0f &&
        chance.expectedResult == ETriadGameState.BlueLost;
}
