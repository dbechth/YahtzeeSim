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
    private const int AssetVersion = 1;

    private static readonly byte[] AssetMagic = "YSCV"u8.ToArray();

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

        // Stored as single precision (about 0.00002 points), the same as the table shipped with the app,
        // so the script and the app give identical results.
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (float)values[i];
        }

        return new CardSolver(values);
    }

    /// <summary>Loads the table from <paramref name="cachePath"/> when it matches the current rules, otherwise computes and saves it.</summary>
    public static CardSolver LoadOrCompute(DiceTables tables, string? cachePath, Action<string>? log = null)
    {
        if (cachePath != null && File.Exists(cachePath))
        {
            try
            {
                using var stream = File.OpenRead(cachePath);
                var cached = ReadAsset(stream, tables);
                log?.Invoke($"Card values loaded from {cachePath}");
                return cached;
            }
            catch (InvalidDataException)
            {
                // Built under other rules or damaged: compute again below.
            }
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var solver = Compute(tables);
        log?.Invoke($"Card values computed in {started.Elapsed.TotalSeconds:0.0} s");
        if (cachePath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
            using var stream = File.Create(cachePath);
            solver.WriteAsset(stream, tables);
        }

        return solver;
    }

    /// <summary>
    /// Writes the table in the compact form shipped with the app: a header (magic, version, rules fingerprint, count)
    /// then one little-endian float per reachable position, in mask order and ascending upper sum.
    /// </summary>
    public void WriteAsset(Stream stream, DiceTables tables)
    {
        var reachable = ReachableUpperSums();
        int count = ReachableCount(reachable);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(AssetMagic);
        writer.Write(AssetVersion);
        writer.Write(RulesHash(tables));
        writer.Write(count);
        for (int mask = 0; mask < MaskCount; mask++)
        {
            foreach (int upper in reachable[mask & 0x3F])
            {
                writer.Write((float)_values[Index(mask, upper)]);
            }
        }
    }

    /// <summary>Reads a table written by <see cref="WriteAsset"/>. Throws <see cref="InvalidDataException"/> if it does not match the current rules.</summary>
    public static CardSolver ReadAsset(Stream stream, DiceTables tables)
    {
        var reachable = ReachableUpperSums();
        int expectedCount = ReachableCount(reachable);
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            if (!reader.ReadBytes(AssetMagic.Length).AsSpan().SequenceEqual(AssetMagic)
                || reader.ReadInt32() != AssetVersion
                || reader.ReadInt32() != RulesHash(tables)
                || reader.ReadInt32() != expectedCount)
            {
                throw new InvalidDataException("The card value table was built for other rules or is damaged.");
            }

            var values = new double[MaskCount * UpperStates];
            for (int mask = 0; mask < MaskCount; mask++)
            {
                foreach (int upper in reachable[mask & 0x3F])
                {
                    values[Index(mask, upper)] = reader.ReadSingle();
                }
            }

            return new CardSolver(values);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The card value table is truncated.", ex);
        }
    }

    /// <summary>Fingerprint of the scoring rules, so values built under different rules are never reused.</summary>
    public static int RulesHash(DiceTables tables)
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

    private static int ReachableCount(List<int>[] reachable)
    {
        int count = 0;
        for (int mask = 0; mask < MaskCount; mask++)
        {
            count += reachable[mask & 0x3F].Count;
        }

        return count;
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

