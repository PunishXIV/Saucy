#nullable disable
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public abstract class TriadGameAgentGraphExplorer : TriadGameAgent
{
    protected const int MaxStateCacheSize = 200_000;
    protected const int CacheEvictCount = 20_000;
    protected readonly LinkedList<long> cacheOrder = new();

    protected readonly Dictionary<long, (SolverResult result, LinkedListNode<long> orderNode)> stateCache = [];

    private Random failsafeRandStream;
    protected int sessionSeed;

    public override void Initialize(TriadGameSolver solver, int sessionSeed) => this.sessionSeed = sessionSeed;

    public override void OnSimulationStart() => ClearStateCache();

    public void ClearStateCache()
    {
        stateCache.Clear();
        cacheOrder.Clear();
    }

    internal static long ComputeStateHash(TriadGameSimulationState state)
    {
        var hash = unchecked((long)14695981039346656037UL);
        const long prime = 1099511628211L;

        for (var i = 0; i < state.board.Length; i++)
        {
            var cell = state.board[i];
            var cellBits = cell == null ? 0 : ((cell.card.Id << 1) | ((int)cell.owner & 1)) + 1;
            hash = (hash ^ cellBits) * prime;
        }

        hash = (hash ^ state.deckBlue.availableCardMask) * prime;
        hash = (hash ^ state.deckRed.availableCardMask) * prime;
        hash = (hash ^ (int)state.state) * prime;
        hash = (hash ^ (state.forcedCardIdx + 1)) * prime;
        hash = (hash ^ (int)state.resolvedSpecial) * prime;
        return hash;
    }

    protected void CacheResult(long key, SolverResult result)
    {
        if (stateCache.TryGetValue(key, out var existing))
        {
            cacheOrder.Remove(existing.orderNode);
            stateCache[key] = (result, cacheOrder.AddFirst(key));
            return;
        }

        if (stateCache.Count >= MaxStateCacheSize)
        {
            EvictOldestCacheEntries();
        }

        stateCache[key] = (result, cacheOrder.AddFirst(key));
    }

    private void EvictOldestCacheEntries()
    {
        var evict = Math.Min(CacheEvictCount, stateCache.Count);
        for (var i = 0; i < evict && cacheOrder.Last != null; i++)
        {
            var oldest = cacheOrder.Last.Value;
            cacheOrder.RemoveLast();
            stateCache.Remove(oldest);
        }
    }

    public override bool FindNextMove(TriadGameSolver solver, TriadGameSimulationState gameState, out int cardIdx, out int boardPos, out SolverResult solverResult)
    {
        cardIdx = -1;
        boardPos = -1;

        var isFinished = IsFinished(gameState, out solverResult);
        if (!isFinished && IsInitialized())
        {
            _ = SearchActionSpace(solver, gameState, 0, out cardIdx, out boardPos, out solverResult);
        }

        return (cardIdx >= 0) && (boardPos >= 0);
    }

    protected bool IsFinished(TriadGameSimulationState gameState, out SolverResult gameResult)
    {
        switch (gameState.state)
        {
            case ETriadGameState.BlueWins:
                gameResult = new(1, 0, 1);
                return true;

            case ETriadGameState.BlueDraw:
                gameResult = new(0, 1, 1);
                return true;

            case ETriadGameState.BlueLost:
                gameResult = new(0, 0, 1);
                return true;
        }

        gameResult = SolverResult.Zero;
        return false;
    }

    protected virtual SolverResult SearchActionSpace(TriadGameSolver solver, TriadGameSimulationState gameState, int searchLevel, out int bestCardIdx, out int bestBoardPos, out SolverResult bestActionResult)
    {
        bestCardIdx = -1;
        bestBoardPos = -1;
        bestActionResult = SolverResult.Zero;

        var isRootLevel = searchLevel == 0;

        long stateKey = 0;
        if (!isRootLevel)
        {
            stateKey = ComputeStateHash(gameState);
            if (stateCache.TryGetValue(stateKey, out var cached))
            {
                bestActionResult = cached.result;
                return cached.result;
            }
        }

        float numWinsTotal = 0;
        float numDrawsTotal = 0;
        long numGamesTotal = 0;

        solver.FindAvailableActions(gameState, out var availBoardMask, out var numAvailBoard, out var availCardsMask, out var numAvailCards);
        if (numAvailCards > 0 && numAvailBoard > 0)
        {
            var turnOwner = (gameState.state == ETriadGameState.InProgressBlue) ? ETriadCardOwner.Blue : ETriadCardOwner.Red;
            var currentDeck = (gameState.state == ETriadGameState.InProgressBlue) ? gameState.deckBlue : gameState.deckRed;
            var hasValidPlacements = false;

            for (var cardIdx = 0; cardIdx < TriadDeckInstance.maxAvailableCards; cardIdx++)
            {
                var cardNotAvailable = (availCardsMask & (1 << cardIdx)) == 0;
                if (cardNotAvailable)
                {
                    continue;
                }

                var cardDef = currentDeck.GetCard(cardIdx);
                var alreadyEvaluated = false;
                for (var priorIdx = 0; priorIdx < cardIdx; priorIdx++)
                {
                    if ((availCardsMask & (1 << priorIdx)) == 0)
                    {
                        continue;
                    }

                    var priorCard = currentDeck.GetCard(priorIdx);
                    if (priorCard != null && cardDef != null && priorCard.Id == cardDef.Id)
                    {
                        alreadyEvaluated = true;
                        break;
                    }
                }

                if (alreadyEvaluated)
                {
                    continue;
                }

                for (var boardIdx = 0; boardIdx < gameState.board.Length; boardIdx++)
                {
                    var boardNotAvailable = (availBoardMask & (1 << boardIdx)) == 0;
                    if (boardNotAvailable)
                    {
                        continue;
                    }

                    var gameStateCopy = new TriadGameSimulationState(gameState);
                    var useDeck = (gameStateCopy.state == ETriadGameState.InProgressBlue) ? gameStateCopy.deckBlue : gameStateCopy.deckRed;

                    var isPlaced = solver.simulation.PlaceCard(gameStateCopy, cardIdx, useDeck, turnOwner, boardIdx);
                    if (isPlaced)
                    {
                        var isFinished = IsFinished(gameStateCopy, out var branchResult);
                        if (!isFinished)
                        {
                            gameStateCopy.forcedCardIdx = -1;
                            branchResult = SearchActionSpace(solver, gameStateCopy, searchLevel + 1, out var _, out var _, out var _);
                        }

                        if (branchResult.IsBetterThan(bestActionResult) || !hasValidPlacements)
                        {
                            bestActionResult = branchResult;
                            bestCardIdx = cardIdx;
                            bestBoardPos = boardIdx;
                        }

                        numWinsTotal += branchResult.numWins;
                        numDrawsTotal += branchResult.numDraws;
                        numGamesTotal += branchResult.numGames;
                        hasValidPlacements = true;
                    }
                }
            }

            if (!hasValidPlacements)
            {
                failsafeRandStream ??= new(sessionSeed);

                bestCardIdx = TriadGameAgentRandom.PickRandomBitFromMask(availCardsMask, failsafeRandStream.Next(numAvailCards));
                bestBoardPos = TriadGameAgentRandom.PickRandomBitFromMask(availBoardMask, failsafeRandStream.Next(numAvailBoard));
            }
        }

        var isOwnerTurn = (searchLevel % 2) == 0;
        var finalResult = isOwnerTurn ? bestActionResult : new(numWinsTotal, numDrawsTotal, numGamesTotal);

        if (!isRootLevel)
        {
            CacheResult(stateKey, finalResult);
        }

        return finalResult;
    }
}
