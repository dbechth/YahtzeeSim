using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Solver;

/// <summary>
/// Precomputed dice combinatorics. Dice order never matters, so every hand is a multiset:
/// 252 five-dice rolls and 462 keeps (0 to 5 dice). Keep k leads to roll r with probability
/// <see cref="TransitionProbability"/>, and <see cref="SubKeeps"/> lists every keep possible from a roll.
/// Points come from <see cref="ScoreRules.EvaluateCategory"/>, so the app's own rules are used.
/// </summary>
public sealed class DiceTables
{
    public const int CategoryCount = 13;

    private static readonly Lazy<DiceTables> SharedInstance = new(() => new DiceTables());

    public static DiceTables Shared => SharedInstance.Value;

    /// <summary>Count of each face 1..6 for every roll, index [roll][face-1].</summary>
    public int[][] RollCounts { get; }

    /// <summary>Count of each face 1..6 for every keep, index [keep][face-1].</summary>
    public int[][] KeepCounts { get; }

    public int RollCount => RollCounts.Length;

    public int KeepCountTotal => KeepCounts.Length;

    /// <summary>Points for roll r in category c (0 when the dice do not fit, i.e. a scratch), index r * 13 + c.</summary>
    public int[] Points { get; }

    /// <summary>Transitions from keep k: entries TransitionStart[k] .. TransitionStart[k + 1] - 1.</summary>
    public int[] TransitionStart { get; }

    public int[] TransitionRoll { get; }

    public double[] TransitionProbability { get; }

    /// <summary>Distinct keeps available from roll r: entries SubKeepStart[r] .. SubKeepStart[r + 1] - 1.</summary>
    public int[] SubKeepStart { get; }

    public int[] SubKeeps { get; }

    /// <summary>Index of the empty keep (roll all five dice).</summary>
    public int EmptyKeep { get; }

    /// <summary>For every roll, the keep that holds all five dice (stop rolling).</summary>
    public int[] FullKeepOfRoll { get; }

    private readonly Dictionary<int, int> _rollIndex = [];
    private readonly Dictionary<int, int> _keepIndex = [];

    public DiceTables()
    {
        var keeps = new List<int[]>();
        var rolls = new List<int[]>();
        for (int size = 0; size <= 5; size++)
        {
            foreach (var counts in Multisets(size))
            {
                _keepIndex[Key(counts)] = keeps.Count;
                keeps.Add(counts);
                if (size == 5)
                {
                    _rollIndex[Key(counts)] = rolls.Count;
                    rolls.Add(counts);
                }
            }
        }

        KeepCounts = [.. keeps];
        RollCounts = [.. rolls];
        EmptyKeep = _keepIndex[0];

        Points = new int[rolls.Count * CategoryCount];
        for (int r = 0; r < rolls.Count; r++)
        {
            int[] dice = ToDice(rolls[r]);
            for (int c = 0; c < CategoryCount; c++)
            {
                var (valid, score) = ScoreRules.EvaluateCategory((ScoreCategory)c, dice);
                Points[r * CategoryCount + c] = valid ? score : 0;
            }
        }

        var start = new List<int>();
        var target = new List<int>();
        var probability = new List<double>();
        for (int k = 0; k < keeps.Count; k++)
        {
            start.Add(target.Count);
            int free = 5 - keeps[k].Sum();
            double total = Math.Pow(6, free);
            foreach (var extra in Multisets(free))
            {
                var counts = new int[6];
                for (int f = 0; f < 6; f++)
                {
                    counts[f] = keeps[k][f] + extra[f];
                }

                target.Add(_rollIndex[Key(counts)]);
                probability.Add(Multinomial(extra) / total);
            }
        }

        start.Add(target.Count);
        TransitionStart = [.. start];
        TransitionRoll = [.. target];
        TransitionProbability = [.. probability];

        var subStart = new List<int>();
        var sub = new List<int>();
        FullKeepOfRoll = new int[rolls.Count];
        for (int r = 0; r < rolls.Count; r++)
        {
            subStart.Add(sub.Count);
            foreach (var keep in SubMultisets(rolls[r]))
            {
                sub.Add(_keepIndex[Key(keep)]);
            }

            FullKeepOfRoll[r] = _keepIndex[Key(rolls[r])];
        }

        subStart.Add(sub.Count);
        SubKeepStart = [.. subStart];
        SubKeeps = [.. sub];
    }

    public int RollIndex(IReadOnlyList<int> dice) => _rollIndex[Key(CountsOf(dice))];

    public int KeepIndex(IReadOnlyList<int> dice) => _keepIndex[Key(CountsOf(dice))];

    public int KeepSize(int keep) => KeepCounts[keep].Sum();

    /// <summary>Sorted dice of a roll or keep, e.g. "13366" (empty string for no dice).</summary>
    public string RollText(int roll) => Text(RollCounts[roll]);

    public string KeepText(int keep) => Text(KeepCounts[keep]);

    public static int[] ToDice(int[] counts)
    {
        var dice = new List<int>(5);
        for (int f = 0; f < 6; f++)
        {
            for (int n = 0; n < counts[f]; n++)
            {
                dice.Add(f + 1);
            }
        }

        return [.. dice];
    }

    public static int[] CountsOf(IReadOnlyList<int> dice)
    {
        var counts = new int[6];
        foreach (int d in dice)
        {
            if (d < 1 || d > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(dice), $"Die value {d} is not 1..6.");
            }

            counts[d - 1]++;
        }

        return counts;
    }

    private static string Text(int[] counts) => string.Concat(ToDice(counts));

    private static int Key(int[] counts)
    {
        int key = 0;
        for (int f = 5; f >= 0; f--)
        {
            key = key * 6 + counts[f];
        }

        return key;
    }

    private static double Multinomial(int[] counts)
    {
        double result = Factorial(counts.Sum());
        foreach (int c in counts)
        {
            result /= Factorial(c);
        }

        return result;
    }

    private static double Factorial(int n)
    {
        double result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }

        return result;
    }

    /// <summary>All multisets of <paramref name="size"/> dice as face counts, in a fixed order.</summary>
    private static List<int[]> Multisets(int size)
    {
        var results = new List<int[]>();
        var counts = new int[6];
        Fill(0, size);
        return results;

        void Fill(int face, int remaining)
        {
            if (face == 5)
            {
                counts[5] = remaining;
                results.Add((int[])counts.Clone());
                return;
            }

            for (int n = remaining; n >= 0; n--)
            {
                counts[face] = n;
                Fill(face + 1, remaining - n);
            }
        }
    }

    private static List<int[]> SubMultisets(int[] of)
    {
        var results = new List<int[]>();
        var counts = new int[6];
        Fill(0);
        return results;

        void Fill(int face)
        {
            if (face == 6)
            {
                results.Add((int[])counts.Clone());
                return;
            }

            for (int n = 0; n <= of[face]; n++)
            {
                counts[face] = n;
                Fill(face + 1);
            }
        }
    }
}
