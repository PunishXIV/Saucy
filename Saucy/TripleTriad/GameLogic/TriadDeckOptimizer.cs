#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.GameLogic;

public partial class TriadDeckOptimizer
{
    public delegate void FoundDeckDelegate(TriadDeck deck, float estWinChance);
    public delegate void UpdatePossibleCount(string numPossibleDesc);
    private const int DeckSlotCommon = -1;
    private const int DeckSlotLocked = -2;

    private const float optimizerScoreAvgSides = 1.0f;
    private const float optimizerScoreMaxSides = 0.75f;
    private const float optimizerScoreRarity = 0.2f;
    private const float optimizerMaxScore = optimizerScoreAvgSides + optimizerScoreMaxSides + optimizerScoreRarity;

    private readonly ETriadCardRarity commonRarity;
    private readonly bool debugMode;

    private readonly ManualResetEvent loopPauseEvent = new(true);
    private readonly Dictionary<ETriadCardRarity, int> maxSlotsPerRarity;
    private readonly int numCommonPctToDropPerPriSlot;
    private readonly int numCommonToBuild;

    private readonly int numGamesToPlay;
    private readonly int numPriorityToBuild;
    private readonly int[][] permutationList;
    private bool bAbort;
    private CardPool currentPool;
    private TriadGameSolver currentSolver;
    private bool isOrderImportant;

    private TriadNpc npc;
    private long numMsElapsed;
    private long numPossibleDecks;
    private long numTestedDecks;
    public TriadDeck optimizedDeck;

    public TriadDeckOptimizer()
    {
        numGamesToPlay = 2000;
        numPriorityToBuild = 10;
        numCommonToBuild = 20;
        numCommonPctToDropPerPriSlot = 10;

        maxSlotsPerRarity = new()
        {
            {
                ETriadCardRarity.Legendary, 1
            },
            {
                ETriadCardRarity.Epic, 2
            }
        };
        commonRarity = ETriadCardRarity.Rare;

        debugMode = false;
        bAbort = false;
#if DEBUG
        debugMode = true;
#endif

        permutationList = new int[120][];
        var ListIdx = 0;
        for (var IdxP0 = 0; IdxP0 < 5; IdxP0++)
        {
            for (var IdxP1 = 0; IdxP1 < 5; IdxP1++)
            {
                if (IdxP1 == IdxP0) { continue; }
                for (var IdxP2 = 0; IdxP2 < 5; IdxP2++)
                {
                    if (IdxP2 == IdxP0 || IdxP2 == IdxP1) { continue; }
                    for (var IdxP3 = 0; IdxP3 < 5; IdxP3++)
                    {
                        if (IdxP3 == IdxP0 || IdxP3 == IdxP1 || IdxP3 == IdxP2) { continue; }
                        for (var IdxP4 = 0; IdxP4 < 5; IdxP4++)
                        {
                            if (IdxP4 == IdxP0 || IdxP4 == IdxP1 || IdxP4 == IdxP2 || IdxP4 == IdxP3) { continue; }

                            permutationList[ListIdx] = [IdxP0, IdxP1, IdxP2, IdxP3, IdxP4];
                            ListIdx++;
                        }
                    }
                }
            }
        }
    }

    public bool IsPaused { get; private set; }
    public event FoundDeckDelegate OnFoundDeck;

    public void Initialize(TriadNpc npc, TriadGameModifier[] regionMods, List<TriadCard> lockedCards)
    {
        this.npc = npc;
        numPossibleDecks = 1;
        numTestedDecks = 0;
        numMsElapsed = 0;

        var playerDB = PlayerSettingsDB.Get();

        currentSolver = new(new TriadGameAgentRandom(null, 0));
        TriadNpcSimulationRules.InitializeSimulation(currentSolver, npc, regionMods);

        isOrderImportant = false;
        foreach (var mod in currentSolver.simulation.modifiers)
        {
            isOrderImportant = isOrderImportant || mod.IsDeckOrderImportant();
        }

        var foundCards = FindCardPool(playerDB.ownedCards, currentSolver.simulation.modifiers, lockedCards);
        if (foundCards)
        {
            UpdatePossibleDeckCount();
        }
    }

    public Task Process(TriadNpc npc, TriadGameModifier[] regionMods, List<TriadCard> lockedCards)
    {
        this.npc = npc;
        numTestedDecks = 0;
        bAbort = false;

        return Task.Run(() => { FindDecksScored(regionMods, lockedCards); });
    }

    public void AbortProcess() => bAbort = true;

    public bool IsAborted() => bAbort;

    public void GuessDeck(List<TriadCard> lockedCards)
    {
        if (currentPool.commonList == null && currentPool.priorityLists == null)
        {
            Logger.WriteLine("Skip deck building, everything was locked");

            optimizedDeck = new(lockedCards);
        }
        else
        {
            var slotIterator = new SlotIterator(currentPool, lockedCards);
            optimizedDeck = null;

            long skipCounter = 0;
            var randomSkipRange = numPossibleDecks / 100;
            if (randomSkipRange > 0)
            {
                var rand = new Random();
                skipCounter = rand.Next((int)randomSkipRange);

                Logger.WriteLine("GuessDeck: {0} / {1}", skipCounter, randomSkipRange);
            }

            var deckList = slotIterator.GetDecks(skipCounter);
            foreach (var deckInfo in deckList)
            {
                if (deckInfo.IsValid())
                {
                    optimizedDeck = new(deckInfo.Cards);
                    break;
                }
            }

            optimizedDeck ??= new(PlayerSettingsDB.Get().starterCards);
        }
    }

    private void UpdatePossibleDeckCount()
    {
        numPossibleDecks = 1;

        var numCommonSlots = 0;
        for (var idx = 0; idx < currentPool.deckSlotTypes.Length; idx++)
        {
            var slotType = currentPool.deckSlotTypes[idx];
            if (slotType == DeckSlotCommon)
            {
                numCommonSlots++;
            }
            else if (slotType >= 0)
            {
                numPossibleDecks *= currentPool.priorityLists[slotType].Length;
            }
        }

        if (numCommonSlots > 0)
        {
            var FactNumCommon = 1;
            for (var Idx = 0; Idx < numCommonSlots; Idx++)
            {
                numPossibleDecks *= (currentPool.commonList.Length - Idx);
                FactNumCommon *= (Idx + 1);
            }

            numPossibleDecks /= FactNumCommon;
        }
    }

    private int GetRandomSeed(int Idx0, int Idx1, int Idx2, int Idx3, int Idx4)
    {
        var Hash = 13;
        Hash = (Hash * 37) + Idx0;
        Hash = (Hash * 37) + Idx1;
        Hash = (Hash * 37) + Idx2;
        Hash = (Hash * 37) + Idx3;
        Hash = (Hash * 37) + Idx4;

        return Hash;
    }

    private int GetDeckScore(TriadGameSolver solver, TriadDeck testDeck, int randomSeed, int numGamesDiv)
    {
        var agentRandom = new TriadGameAgentRandom(solver, randomSeed);
        var deckScore = 0;
        var maxGames = (numGamesToPlay / numGamesDiv) / 2;

        for (var idxGame = 0; idxGame < maxGames; idxGame++)
        {
            var gameStateR = solver.StartSimulation(testDeck, npc.Deck, ETriadGameState.InProgressRed);
            solver.RunSimulation(gameStateR, agentRandom, agentRandom);
            deckScore += gameStateR.state == ETriadGameState.BlueWins ? 2 :
                gameStateR.state == ETriadGameState.BlueDraw ? 1 : 0;

            var gameStateB = solver.StartSimulation(testDeck, npc.Deck, ETriadGameState.InProgressBlue);
            solver.RunSimulation(gameStateB, agentRandom, agentRandom);
            deckScore += gameStateB.state == ETriadGameState.BlueWins ? 2 :
                gameStateB.state == ETriadGameState.BlueDraw ? 1 : 0;
        }

        return deckScore;
    }
}
