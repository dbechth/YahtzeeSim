namespace YahtzeeSim.Analysis.Solver;

/// <summary>
/// Expected values for one turn, given the value of scoring each final roll (<see cref="Final"/>).
/// Filled by <see cref="Compute"/> working backwards from the third roll:
/// Final (score now) -> KeepBeforeRoll3 -> Roll2 -> KeepBeforeRoll2 -> Roll1 -> Start.
/// </summary>
public sealed class TurnValues
{
    public TurnValues(DiceTables tables)
    {
        Tables = tables;
        Final = new double[tables.RollCount];
        FinalChoice = new int[tables.RollCount];
        KeepBeforeRoll3 = new double[tables.KeepCountTotal];
        Roll2 = new double[tables.RollCount];
        KeepBeforeRoll2 = new double[tables.KeepCountTotal];
        Roll1 = new double[tables.RollCount];
    }

    public DiceTables Tables { get; }

    /// <summary>Value of scoring roll r in the best open box now.</summary>
    public double[] Final { get; }

    /// <summary>Encoded best box for roll r (card * 13 + category), or -1 when no box is open.</summary>
    public int[] FinalChoice { get; }

    /// <summary>Expected value of keeping k after roll 2 and rolling the rest once more.</summary>
    public double[] KeepBeforeRoll3 { get; }

    /// <summary>Best expected value after seeing roll 2 (choose the keep, or stop by keeping all five).</summary>
    public double[] Roll2 { get; }

    /// <summary>Expected value of keeping k after roll 1 and rolling the rest.</summary>
    public double[] KeepBeforeRoll2 { get; }

    /// <summary>Best expected value after seeing roll 1.</summary>
    public double[] Roll1 { get; }

    /// <summary>Expected value of the turn before the first roll.</summary>
    public double Start { get; private set; }

    /// <summary>Value of each cell for each roll (index roll * cards * 13 + card * 13 + category); NaN for filled cells.</summary>
    public double[] CellValues { get; private set; } = [];

    public int Cards { get; private set; }

    public double CellValue(int roll, int card, int category) =>
        CellValues[(roll * Cards + card) * DiceTables.CategoryCount + category];

    internal void PrepareCells(int cards)
    {
        int size = Tables.RollCount * cards * DiceTables.CategoryCount;
        if (CellValues.Length != size)
        {
            CellValues = new double[size];
        }

        Cards = cards;
    }

    /// <summary>Fills every array from <see cref="Final"/>. Call after setting Final.</summary>
    public void Compute()
    {
        Expect(Final, KeepBeforeRoll3);
        BestKeep(KeepBeforeRoll3, Roll2);
        Expect(Roll2, KeepBeforeRoll2);
        BestKeep(KeepBeforeRoll2, Roll1);

        var t = Tables;
        double start = 0;
        for (int i = t.TransitionStart[t.EmptyKeep]; i < t.TransitionStart[t.EmptyKeep + 1]; i++)
        {
            start += t.TransitionProbability[i] * Roll1[t.TransitionRoll[i]];
        }

        Start = start;
    }

    /// <summary>The keep with the highest value among those possible from roll r (first one wins a tie).</summary>
    public int ArgBestKeep(int roll, int rollNumber)
    {
        var keepValues = rollNumber == 1 ? KeepBeforeRoll2 : KeepBeforeRoll3;
        var t = Tables;
        int best = -1;
        double bestValue = double.NegativeInfinity;
        for (int i = t.SubKeepStart[roll]; i < t.SubKeepStart[roll + 1]; i++)
        {
            int k = t.SubKeeps[i];
            if (keepValues[k] > bestValue + 1e-12)
            {
                bestValue = keepValues[k];
                best = k;
            }
        }

        return best;
    }

    private void Expect(double[] rollValues, double[] keepValues)
    {
        var t = Tables;
        for (int k = 0; k < keepValues.Length; k++)
        {
            double sum = 0;
            int end = t.TransitionStart[k + 1];
            for (int i = t.TransitionStart[k]; i < end; i++)
            {
                sum += t.TransitionProbability[i] * rollValues[t.TransitionRoll[i]];
            }

            keepValues[k] = sum;
        }
    }

    private void BestKeep(double[] keepValues, double[] rollValues)
    {
        var t = Tables;
        for (int r = 0; r < rollValues.Length; r++)
        {
            double best = double.NegativeInfinity;
            int end = t.SubKeepStart[r + 1];
            for (int i = t.SubKeepStart[r]; i < end; i++)
            {
                double v = keepValues[t.SubKeeps[i]];
                if (v > best)
                {
                    best = v;
                }
            }

            rollValues[r] = best;
        }
    }
}

