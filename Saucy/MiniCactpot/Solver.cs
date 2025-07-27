using System;
using System.Collections.Generic;
using System.Linq;

namespace Saucy.MiniCactpot;

/// <summary>
/// https://super-aardvark.github.io/yuryu/
/// 0 = top row
/// 1 = middle row
/// 2 = bottom row
/// 3 = left column
/// 4 = center column
/// 5 = right column
/// 6 = major diagonal
/// 7 = minor diagonal
/// </summary>
public sealed class CactpotSolver
{
    public const int TotalNumbers = 9;
    public const int TotalLanes = 8;

    private const double EPS = 0.00001;

    private static int[] Payouts => [0, 0, 0, 0, 0, 0, 10000, 36, 720, 360, 80, 252, 108, 72, 54, 180, 72, 180, 119, 36, 306, 1080, 144, 1800, 3600,];

    private readonly Dictionary<string, (double Value, bool[] Tiles)> PrecalculatedOpenings = new() {
        { "100000000", (1677.7854166666664, [false, false, true,  false, false, false, true,  false, false]) },
        { "200000000", (1665.8127976190476, [false, false, true,  false, false, false, true,  false, false]) },
        { "300000000", (1662.5047619047620, [false, false, true,  false, false, false, true,  false, false]) },
        { "400000000", (1365.0047619047618, [false, false, false, false, true,  false, false, false, false]) },
        { "500000000", (1359.5589285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "600000000", (1364.3044642857142, [false, false, false, false, true,  false, false, false, false]) },
        { "700000000", (1454.5455357142855, [false, false, false, false, true,  false, false, false, false]) },
        { "800000000", (1527.0875000000000, [false, false, true,  false, true,  false, true,  false, false]) },
        { "900000000", (1517.7214285714285, [false, false, true,  false, true,  false, true,  false, false]) },
        { "010000000", (1411.3541666666665, [false, false, false, false, true,  false, false, false, false]) },
        { "020000000", (1414.9401785714288, [false, false, false, false, true,  false, false, false, false]) },
        { "030000000", (1406.4190476190477, [false, false, false, false, true,  false, false, false, false]) },
        { "040000000", (1443.3062499999999, [false, false, false, false, false, false, true,  false, true]) },
        { "050000000", (1444.3172619047618, [false, false, false, false, true,  false, true,  false, true]) },
        { "060000000", (1441.3663690476192, [false, false, false, false, true,  false, false, false, false]) },
        { "070000000", (1485.6839285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "080000000", (1512.9279761904760, [true,  false, true,  false, false, false, false, false, false]) },
        { "090000000", (1518.4663690476190, [true,  false, true,  false, false, false, false, false, false]) },
        { "001000000", (1677.7854166666664, [true,  false, false, false, false, false, false, false, true]) },
        { "002000000", (1665.8127976190476, [true,  false, false, false, false, false, false, false, true]) },
        { "003000000", (1662.5047619047620, [true,  false, false, false, false, false, false, false, true]) },
        { "004000000", (1365.0047619047618, [false, false, false, false, true,  false, false, false, false]) },
        { "005000000", (1359.5589285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "006000000", (1364.3044642857142, [false, false, false, false, true,  false, false, false, false]) },
        { "007000000", (1454.5455357142855, [false, false, false, false, true,  false, false, false, false]) },
        { "008000000", (1527.0875000000000, [true,  false, false, false, true,  false, false, false, true]) },
        { "009000000", (1517.7214285714285, [true,  false, false, false, true,  false, false, false, true]) },
        { "000100000", (1411.3541666666665, [false, false, false, false, true,  false, false, false, false]) },
        { "000200000", (1414.9401785714288, [false, false, false, false, true,  false, false, false, false]) },
        { "000300000", (1406.4190476190477, [false, false, false, false, true,  false, false, false, false]) },
        { "000400000", (1443.3062499999999, [false, false, true,  false, false, false, false, false, true]) },
        { "000500000", (1444.3172619047618, [false, false, true,  false, true,  false, false, false, true]) },
        { "000600000", (1441.3663690476192, [false, false, false, false, true,  false, false, false, false]) },
        { "000700000", (1485.6839285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "000800000", (1512.9279761904760, [true,  false, false, false, false, false, true,  false, false]) },
        { "000900000", (1518.4663690476190, [true,  false, false, false, false, false, true,  false, false]) },
        { "000010000", (1860.4401785714285, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000020000", (1832.5413690476191, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000030000", (1834.1797619047620, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000040000", (1171.9669642857143, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000050000", (1176.2047619047619, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000060000", (1234.6142857142856, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000070000", (1427.3583333333331, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000080000", (1544.7607142857144, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000090000", (1509.1976190476190, [true,  false, true,  false, false, false, true,  false, true]) },
        { "000001000", (1411.3541666666665, [false, false, false, false, true,  false, false, false, false]) },
        { "000002000", (1414.9401785714288, [false, false, false, false, true,  false, false, false, false]) },
        { "000003000", (1406.4190476190477, [false, false, false, false, true,  false, false, false, false]) },
        { "000004000", (1443.3062499999999, [true,  false, false, false, false, false, true,  false, false]) },
        { "000005000", (1444.3172619047618, [true,  false, true,  false, false, false, true,  false, false]) },
        { "000006000", (1441.3663690476192, [false, false, false, false, true,  false, false, false, false]) },
        { "000007000", (1485.6839285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "000008000", (1512.9279761904760, [false, false, true,  false, false, false, false, false, true]) },
        { "000009000", (1518.4663690476190, [false, false, true,  false, false, false, false, false, true]) },
        { "000000100", (1677.7854166666664, [true,  false, false, false, false, false, false, false, true]) },
        { "000000200", (1665.8127976190476, [true,  false, false, false, false, false, false, false, true]) },
        { "000000300", (1662.5047619047620, [true,  false, false, false, false, false, false, false, true]) },
        { "000000400", (1365.0047619047618, [false, false, false, false, true,  false, false, false, false]) },
        { "000000500", (1359.5589285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "000000600", (1364.3044642857142, [false, false, false, false, true,  false, false, false, false]) },
        { "000000700", (1454.5455357142855, [false, false, false, false, true,  false, false, false, false]) },
        { "000000800", (1527.0875000000000, [true,  false, false, false, true,  false, false, false, true]) },
        { "000000900", (1517.7214285714285, [true,  false, false, false, true,  false, false, false, true]) },
        { "000000010", (1411.3541666666665, [false, false, false, false, true,  false, false, false, false]) },
        { "000000020", (1414.9401785714288, [false, false, false, false, true,  false, false, false, false]) },
        { "000000030", (1406.4190476190477, [false, false, false, false, true,  false, false, false, false]) },
        { "000000040", (1443.3062499999999, [true,  false, true,  false, false, false, false, false, false]) },
        { "000000050", (1444.3172619047618, [true,  false, true,  false, true,  false, false, false, false]) },
        { "000000060", (1441.3663690476192, [false, false, false, false, true,  false, false, false, false]) },
        { "000000070", (1485.6839285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "000000080", (1512.9279761904760, [false, false, false, false, false, false, true,  false, true]) },
        { "000000090", (1518.4663690476190, [false, false, false, false, false, false, true,  false, true]) },
        { "000000001", (1677.7854166666664, [false, false, true,  false, false, false, true,  false, false]) },
        { "000000002", (1665.8127976190476, [false, false, true,  false, false, false, true,  false, false]) },
        { "000000003", (1662.5047619047620, [false, false, true,  false, false, false, true,  false, false]) },
        { "000000004", (1365.0047619047618, [false, false, false, false, true,  false, false, false, false]) },
        { "000000005", (1359.5589285714286, [false, false, false, false, true,  false, false, false, false]) },
        { "000000006", (1364.3044642857142, [false, false, false, false, true,  false, false, false, false]) },
        { "000000007", (1454.5455357142855, [false, false, false, false, true,  false, false, false, false]) },
        { "000000008", (1527.0875000000000, [false, false, true,  false, true,  false, true,  false, false]) },
        { "000000009", (1517.7214285714285, [false, false, true,  false, true,  false, true,  false, false]) },
    };

    internal bool[] Solve(int[] state)
    {
        // Count how many are visible
        var num_revealed = state.Count(x => x > 0);

        // If four are visible, we are picking between eight rows. Otherwise, we are picking
        // between nine tiles (although we'll never be picking revealed tiles)
        var num_options = 9;
        if (num_revealed == 4)
            num_options = 8;

        double value;
        var which_to_flip = new bool[num_options];

        switch (num_revealed)
        {
            case 0:
                // You don't get to choose the first spot, but here's the answer anyway
                return [true, false, true, false, false, false, true, false, true];

            case 1:
                {
                    // This will take a long time, but we have no choice
                    // value = SolveAny(ref state, ref tiles);

                    // Using our pre-calculated library, this is much faster
                    var stateStr = string.Join("", state);
                    (value, which_to_flip) = PrecalculatedOpenings[stateStr];
                    break;
                }

            default:
                value = SolveAny(ref state, ref which_to_flip);
                break;
        }

        PluginLog.Verbose($"Expected value: {value} MGP");

        return which_to_flip;
    }

    private double SolveAny(ref int[] state, ref bool[] options)
    {
        var dummy_array = new bool[options.Length];
        var hiddenNumbers = new List<int>();
        var ids = new List<int>();
        var has = new int[10];
        var tot_win = new List<double>();
        for (var i = 0; i < 9; i++)
        {
            if (state[i] == 0)
            {
                // Storing the ids of all locations which are currently unrevealed
                ids.Add(i);
                tot_win.Add(0);
            }
            else
            {
                // Checking which numbers are currently visible
                has[state[i]] = 1;
            }
        }

        var num_hidden = tot_win.Count;
        var num_revealed = 9 - num_hidden;

        // From the previous step, we know which numbers are not yet visible:
        //  these are the possible unknowns
        for (var i = 1; i <= 9; i++)
        {
            if (has[i] == 0)
            {
                hiddenNumbers.Add(i);
            }
        }

        if (num_revealed >= 4)
        {
            // We've revealed as many numbers as we can -- time for the final assessment
            var permutations = 0;
            tot_win = [0, 0, 0, 0, 0, 0, 0, 0,];
            // One for each row, column, and diagonal
            // Loop over all possible permutations on the unknowns
            do
            {
                permutations++;
                for (var i = 0; i < ids.Count; i++)
                {
                    state[ids[i]] = hiddenNumbers[i];
                }

                // For each row, cumulatively sum the winnings for picking that row
                tot_win[0] += Payouts[state[0] + state[1] + state[2]];
                tot_win[1] += Payouts[state[3] + state[4] + state[5]];
                tot_win[2] += Payouts[state[6] + state[7] + state[8]];
                tot_win[3] += Payouts[state[0] + state[3] + state[6]];
                tot_win[4] += Payouts[state[1] + state[4] + state[7]];
                tot_win[5] += Payouts[state[2] + state[5] + state[8]];
                tot_win[6] += Payouts[state[0] + state[4] + state[8]];
                tot_win[7] += Payouts[state[2] + state[4] + state[6]];
            } while (NextPermutation(hiddenNumbers));

            // Find the maximum. Start by assuming option 0 is best.
            var currentMax = tot_win[0];
            options[0] = true;
            for (var i = 1; i < 8; i++)
            {
                // If another row yielded a higher expected value:
                if (tot_win[i] > currentMax)
                {
                    // Mark all the previous rows as FALSE (not optimal) and the current one as TRUE
                    currentMax = tot_win[i];
                    for (var j = 0; j < i; j++)
                        options[j] = false;
                    options[i] = true;
                }
                else if (Math.Abs(tot_win[i] - currentMax) < 0.1f)
                {
                    // For a tie, mark the current one as TRUE, and leave the previous ones intact
                    options[i] = true;
                }
            }
            // The current totals are for a number of possible configurations.
            // Divide by that number to get the actual expected value.
            return currentMax / permutations;
        }
        else
        {
            // Determine which tile to reveal next.
            // Loop over every unknown tile and every possible value that could appear.
            // Solve the resulting cases with a recursive call to solve_any.
            for (var i = 0; i < num_hidden; i++)
            {
                for (var j = 0; j < num_hidden; j++)
                {
                    state[ids[i]] = hiddenNumbers[j];
                    tot_win[i] += SolveAny(ref state, ref dummy_array);
                    for (var k = 0; k < num_hidden; k++)
                        state[ids[k]] = 0;
                }
            }
            var currentMax = tot_win[0];
            options[ids[0]] = true;
            for (var i = 1; i < tot_win.Count; i++)
            {
                if (tot_win[i] > currentMax + EPS)
                {
                    currentMax = tot_win[i];
                    for (var j = 0; j < i; j++)
                        options[ids[j]] = false;
                    options[ids[i]] = true;
                }
                else if (tot_win[i] > currentMax - EPS)
                {
                    options[ids[i]] = true;
                }
            }

            // Each tile can be flipped to reveal one of num_hidden values (one number per space).
            // Divide by num_hidden to get the true expected value.
            return currentMax / num_hidden;
        }
    }

    private static bool NextPermutation(List<int> list)
    {
        var begin = 0;
        var end = list.Count;

        if (list.Count <= 1)
            return false;

        var i = list.Count - 1;

        while (true)
        {
            var j = i;
            i--;

            if (list[i] < list[j])
            {
                var k = end;

                while (list[i] >= list[--k]) { }

                Swap(list, i, k);
                Reverse(list, j, end);
                return true;
            }

            if (i == begin)
            {
                Reverse(list, begin, end);
                return false;
            }
        }
    }

    private static void Reverse<T>(List<T> list, int begin, int end)
    {
        var count = end - begin;

        var reversedSlice = list.GetRange(begin, count);
        reversedSlice.Reverse();

        for (var i = 0; i < reversedSlice.Count; i++)
        {
            list[begin + i] = reversedSlice[i];
        }
    }

    private static void Swap<T>(IList<T> list, int i1, int i2)
    {
        (list[i1], list[i2]) = (list[i2], list[i1]);
    }
}