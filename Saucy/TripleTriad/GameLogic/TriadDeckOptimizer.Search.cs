#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
namespace Saucy.TripleTriad.GameLogic;

public partial class TriadDeckOptimizer
{
    private void FindDecksScored(TriadGameModifier[] regionMods, List<TriadCard> lockedCards)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        if (currentPool.commonList == null && currentPool.priorityLists == null)
        {
            stopwatch.Stop();
            Logger.WriteLine("Skip deck building, everything was locked");

            optimizedDeck = new(lockedCards);
            return;
        }

        var lockOb = new object();
        var bestScore = 0;
        var bestDeck = new TriadDeck(PlayerSettingsDB.Get().starterCards);

        var slotIterator = new SlotIterator(currentPool, lockedCards);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = SaucyParallelism.DeckOptimizerThreads
        };
        Logger.WriteLine(
            "Deck optimizer threads: {0} ({1} / {2} logical cores, configured {3})",
            options.MaxDegreeOfParallelism,
            SaucyParallelism.DeckOptimizerThreads,
            SaucyParallelism.LogicalProcessorCount,
            SaucyParallelism.DeckOptimizerConfiguredThreads <= 0
                ? "all"
                : SaucyParallelism.DeckOptimizerConfiguredThreads.ToString());

        long lowestPauseIdx = 0;
        bool canFinishLoop;
        using var threadSolvers = new ThreadLocal<TriadGameSolver>(() => currentSolver.CreateWorkerCopy());
        do
        {
            var loopResult = Parallel.ForEach(slotIterator.GetDecks(lowestPauseIdx), options, (deckInfo, state) =>
            {
                if (IsPaused)
                {
                    state.Break();
                    return;
                }

                if (bAbort)
                {
                    state.Stop();
                    return;
                }

                if (!deckInfo.IsValid())
                {
                    return;
                }

                var randomSeed = GetRandomSeed(deckInfo.Idx0, deckInfo.Idx1, deckInfo.Idx2, deckInfo.Idx3, deckInfo.Idx4);
                var testDeck = new TriadDeck(deckInfo.Cards);
                var testScore = GetDeckScore(threadSolvers.Value, testDeck, randomSeed, 1);
                if (testScore > bestScore)
                {
                    lock (lockOb)
                    {
                        if (testScore > bestScore)
                        {
                            bestScore = testScore;
                            bestDeck = testDeck;

                            var estWinChance = 1.0f * testScore / (numGamesToPlay * 2);
                            OnFoundDeck?.Invoke(testDeck, estWinChance);
                        }
                    }
                }

                Interlocked.Increment(ref numTestedDecks);
            });

            if (IsPaused)
            {
                loopPauseEvent.WaitOne();
                lowestPauseIdx += loopResult.LowestBreakIteration ?? 0;
                Interlocked.Exchange(ref numTestedDecks, Math.Min(lowestPauseIdx, numPossibleDecks));
            }

            canFinishLoop = bAbort || loopResult.IsCompleted || loopResult.LowestBreakIteration == null;
        }
        while (!canFinishLoop);

        stopwatch.Stop();
        Logger.WriteLine($"Building list of decks: {stopwatch.ElapsedMilliseconds}ms, num:{numPossibleDecks}");
        optimizedDeck = bestDeck;
    }

    public int GetProgress()
    {
        if (numPossibleDecks > 0)
        {
            var desc = (100 * Interlocked.Read(ref numTestedDecks) / numPossibleDecks).ToString();
            var progressPct = int.Parse(desc);
            return Math.Max(0, Math.Min(100, progressPct));
        }

        return 0;
    }

    public void SetPaused(bool wantsPaused)
    {
        if (wantsPaused && !IsPaused)
        {
            loopPauseEvent.Reset();
            IsPaused = true;
        }
        else if (!wantsPaused && IsPaused)
        {
            IsPaused = false;
            loopPauseEvent.Set();
        }
    }

    public string GetNumTestedDesc() =>
        Interlocked.Read(ref numTestedDecks).ToString("N0", CultureInfo.InvariantCulture);

    public string GetNumPossibleDecksDesc() =>
        numPossibleDecks.ToString("N0", CultureInfo.InvariantCulture);

    public int GetSecondsRemaining(int elapsedMs)
    {
        numMsElapsed += elapsedMs;

        var numTestedDecksSafe = Interlocked.Read(ref numTestedDecks);
        var numTestedPerMs = numTestedDecksSafe / (double)Math.Max(1, numMsElapsed);
        var numMsPerTest = numTestedDecksSafe == 0 ? 1 : numMsElapsed / numTestedDecksSafe;
        var numTestsRemaining = numPossibleDecks - numTestedDecksSafe;

        var numSecRemaining = numTestedPerMs > 0
            ? (long)((numTestsRemaining / numTestedPerMs) / 1000)
            : (numTestsRemaining * numMsPerTest) / 1000;

        return Math.Max(0, (int)Math.Min(int.MaxValue, numSecRemaining));
    }

    public static float GetCardScore(TriadCard card)
    {
        var numberMax = Math.Max(Math.Max(card.Sides[0], card.Sides[1]), Math.Max(card.Sides[2], card.Sides[3]));
        var numberSum = card.Sides[0] + card.Sides[1] + card.Sides[2] + card.Sides[3];
        var numberAvg = numberSum / 4.0f;

        var cardScore =
            ((numberAvg / 10.0f) * optimizerScoreAvgSides) +
            ((numberMax / 10.0f) * optimizerScoreMaxSides) +
            (((int)card.Rarity / (float)ETriadCardRarity.Legendary) * optimizerScoreRarity);

        return cardScore / optimizerMaxScore;
    }

    private struct CardPool
    {
        public TriadCard[][] priorityLists;
        public TriadCard[] commonList;

        public int[] deckSlotTypes;
    }

    private struct CardScoreData : IComparable<CardScoreData>
    {
        public TriadCard card;
        public float score;

        public readonly int CompareTo(CardScoreData other) => -score.CompareTo(other.score);

        public override readonly string ToString() => card.ToShortCodeString() + ", score: " + score;
    }

    private class SlotIterator
    {
        public const int numSlots = 5;
        private readonly bool[] isSlotCommon = new bool[numSlots];
        public TriadCard[][] slotLists = new TriadCard[numSlots][];

        public SlotIterator(CardPool cardPool, List<TriadCard> lockedCards)
        {
            for (var idx = 0; idx < numSlots; idx++)
            {
                slotLists[idx] =
                    (cardPool.deckSlotTypes[idx] == DeckSlotCommon) ? cardPool.commonList :
                    (cardPool.deckSlotTypes[idx] >= 0) ? cardPool.priorityLists[cardPool.deckSlotTypes[idx]] :
                    [lockedCards[idx]];

                isSlotCommon[idx] = (cardPool.deckSlotTypes[idx] == DeckSlotCommon);
            }
        }

        private int FindLoopStart(int SlotIdx, int IdxS0, int IdxS1, int IdxS2, int IdxS3)
        {
            if (!isSlotCommon[SlotIdx]) { return 0; }

            if (SlotIdx >= 4 && isSlotCommon[3]) { return IdxS3 + 1; }
            if (SlotIdx >= 3 && isSlotCommon[2]) { return IdxS2 + 1; }
            if (SlotIdx >= 2 && isSlotCommon[1]) { return IdxS1 + 1; }
            if (SlotIdx >= 1 && isSlotCommon[0]) { return IdxS0 + 1; }

            return 0;
        }

        public IEnumerable<ItemInfo> GetDecks(long skipIdx)
        {
            var skipCounter = skipIdx;
            for (var IdxS0 = 0; IdxS0 < slotLists[0].Length; IdxS0++)
            {
                var startS1 = FindLoopStart(1, IdxS0, -1, -1, -1);
                for (var IdxS1 = startS1; IdxS1 < slotLists[1].Length; IdxS1++)
                {
                    var startS2 = FindLoopStart(2, IdxS0, IdxS1, -1, -1);
                    for (var IdxS2 = startS2; IdxS2 < slotLists[2].Length; IdxS2++)
                    {
                        var startS3 = FindLoopStart(3, IdxS0, IdxS1, IdxS2, -1);
                        for (var IdxS3 = startS3; IdxS3 < slotLists[3].Length; IdxS3++)
                        {
                            var startS4 = FindLoopStart(4, IdxS0, IdxS1, IdxS2, IdxS3);
                            for (var IdxS4 = startS4; IdxS4 < slotLists[4].Length; IdxS4++)
                            {
                                if (skipCounter > 0)
                                {
                                    skipCounter--;
                                    continue;
                                }

                                yield return new(IdxS0, IdxS1, IdxS2, IdxS3, IdxS4, this);
                            }
                        }
                    }
                }
            }
        }

        public struct ItemInfo
        {
            public int Idx0;
            public int Idx1;
            public int Idx2;
            public int Idx3;
            public int Idx4;
            public TriadCard[] Cards;

            public ItemInfo(int idx0, int idx1, int idx2, int idx3, int idx4, SlotIterator iterator)
            {
                Idx0 = idx0;
                Idx1 = idx1;
                Idx2 = idx2;
                Idx3 = idx3;
                Idx4 = idx4;
                Cards = [iterator.slotLists[0][Idx0], iterator.slotLists[1][Idx1], iterator.slotLists[2][Idx2], iterator.slotLists[3][Idx3], iterator.slotLists[4][Idx4]];
            }

            public readonly bool IsValid() =>
                (Cards[0] != Cards[1]) && (Cards[0] != Cards[2]) && (Cards[0] != Cards[3]) && (Cards[0] != Cards[4]) &&
                (Cards[1] != Cards[2]) && (Cards[1] != Cards[3]) && (Cards[1] != Cards[4]) &&
                (Cards[2] != Cards[3]) && (Cards[2] != Cards[4]) &&
                (Cards[3] != Cards[4]);
        }
    }
}
