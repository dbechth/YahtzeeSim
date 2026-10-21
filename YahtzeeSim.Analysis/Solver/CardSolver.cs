using System.Numerics;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Solver;

/// <summary>Where one card stands: which boxes are filled (bit c = category c) and the upper-section sum so far.</summary>
public readonly record struct CardPosition(int Mask, int UpperSum)
{
    public const int FullMask = (1 << DiceTables.CategoryCount) - 1;

    public bool IsFilled(int category) => (Mask & (1 << category)) != 0;

    public bool IsComplete => Mask == FullMask;

    public int FilledCount => BitOperations.PopCount((uint)Mask);

    /// <summary>Points earned by scoring <paramref name="points"/> in a box, including the 35 upper bonus when it is reached now.</summary>
    public int Reward(int category, int points)
    {
        if (category >= 6)
        {
            return points;
        }

        bool reachesBonus = UpperSum < GameCardState.UpperBonusThreshold
            && UpperSum + points >= GameCardState.UpperBonusThreshold;
        return points + (reachesBonus ? GameCardState.UpperBonusValue : 0);
    }

    public CardPosition After(int category, int points) => new(
        Mask | (1 << category),
        category < 6 ? UpperSum + points : UpperSum);
}

/// <summary>
/// Exact optimal strategy for one card under Full Sheet rules (no Yahtzee bonus, no Joker).
/// <see cref="Value"/> is the expected points still to come (including the upper bonus) from a card position,
/// found by backward induction over every filled-box mask and upper sum (capped at 63, where the bonus is secured).
/// </summary>
public sealed class CardSolver
{
    public const int UpperCap = 63;
    private const int UpperStates = UpperCap + 1;
    private const int MaskCount = 1 << DiceTables.CategoryCount;
    private const string CacheMagic = "YSCARDV1";

    private readonly double[] _values;

    private CardSolver(double[] values)
    {
        _values = values;
    }

    public double Value(CardPosition position) => _values[Index(position.Mask, position.UpperSum)];

    /// <summary>Expected score of a whole card played alone with perfect play.</summary>
    public double StartValue => _values[Index(0, 0)];

    public static CardSolver Compute(DiceTables tables)
    {
        var values = new double[MaskCount * UpperStates];
        var reachable = ReachableUpperSums();

        for (int filled = DiceTables.CategoryCount - 1; filled >= 0; filled--)
        {
            var states = new List<(int Mask, int Upper)>();
            for (int mask = 0; mask < MaskCount; mask++)
            {
                if (BitOperations.PopCount((uint)mask) != filled)
                {
                    continue;
                }

                foreach (int upper in reachable[mask & 0x3F])
                {
                    states.Add((mask, upper));
                }
            }

            Parallel.For(
                0,
                states.Count,
                () => new TurnValues(tables),
                (i, _, turn) =>
                {
                    var (mask, upper) = states[i];
                    var position = new CardPosition(mask, upper);
                    for (int r = 0; r < tables.RollCount; r++)
                    {
                        double best = double.NegativeInfinity;
                        for (int c = 0; c < DiceTables.CategoryCount; c++)
                        {
                            if (position.IsFilled(c))
                            {
                                continue;
                            }

                            int points = tables.Points[r * DiceTables.CategoryCount + c];
                            var next = position.After(c, points);
                            double v = position.Reward(c, points) + values[Index(next.Mask, next.UpperSum)];
                            if (v > best)
                            {
                                best = v;
                            }
                        }

                        turn.Final[r] = best;
                    }

                    turn.Compute();
                    values[Index(mask, upper)] = turn.Start;
                    return turn;
                },
                _ => { });
        }

        return new CardSolver(values);
    }

    /// <summary>Loads the table from <paramref name="cachePath"/> when it matches the current rules, otherwise computes and saves it.</summary>
    public static CardSolver LoadOrCompute(DiceTables tables, string? cachePath, Action<string>? log = null)
    {
        int rulesHash = RulesHash(tables);
        if (cachePath != null && TryLoad(cachePath, rulesHash) is { } cached)
        {
            log?.Invoke($"Card values loaded from {cachePath}");
            return cached;
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var solver = Compute(tables);
        log?.Invoke($"Card values computed in {started.Elapsed.TotalSeconds:0.0} s");
        if (cachePath != null)
        {
            solver.Save(cachePath, rulesHash);
        }

        return solver;
    }

    private void Save(string path, int rulesHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(CacheMagic);
        writer.Write(rulesHash);
        writer.Write(_values.Length);
        foreach (double v in _values)
        {
            writer.Write(v);
        }
    }

    private static CardSolver? TryLoad(string path, int rulesHash)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadString() != CacheMagic || reader.ReadInt32() != rulesHash)
            {
                return null;
            }

            int length = reader.ReadInt32();
            if (length != MaskCount * UpperStates)
            {
                return null;
            }

            var values = new double[length];
            for (int i = 0; i < length; i++)
            {
                values[i] = reader.ReadDouble();
            }

            return new CardSolver(values);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            return null;
        }
    }

    /// <summary>Fingerprint of the scoring table, so a cache built under different rules is never reused.</summary>
    private static int RulesHash(DiceTables tables)
    {
        unchecked
        {
            int hash = 17;
            foreach (int p in tables.Points)
            {
                hash = hash * 31 + p;
            }

            hash = hash * 31 + GameCardState.UpperBonusThreshold;
            return hash * 31 + GameCardState.UpperBonusValue;
        }
    }

    private static int Index(int mask, int upperSum) => mask * UpperStates + Math.Min(upperSum, UpperCap);

    /// <summary>For each set of filled upper boxes (6-bit mask), the upper sums (capped) that can occur.</summary>
    private static List<int>[] ReachableUpperSums()
    {
        var result = new List<int>[64];
        for (int upperMask = 0; upperMask < 64; upperMask++)
        {
            var sums = new HashSet<int> { 0 };
            for (int face = 1; face <= 6; face++)
            {
                if ((upperMask & (1 << (face - 1))) == 0)
                {
                    continue;
                }

                var next = new HashSet<int>();
                foreach (int s in sums)
                {
                    for (int n = 0; n <= 5; n++)
                    {
                        next.Add(Math.Min(UpperCap, s + face * n));
                    }
                }

                sums = next;
            }

            result[upperMask] = [.. sums.Order()];
        }

        return result;
    }
}

