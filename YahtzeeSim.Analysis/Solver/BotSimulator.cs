using System.Text.Json.Serialization;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Solver;

/// <summary>
/// Result of one simulated game. <see cref="LuckFree"/> is the total with dice luck removed, measured with the
/// strategy's own values (exact for the reference strategy, whose decisions cost nothing against itself).
/// </summary>
public sealed record SimulatedGame(int Total, double LuckFree, int[] CardTotals, int[] CategoryPoints, int UpperBonuses, IReadOnlyList<ActionRow>? Actions);

/// <summary>Summary of many simulated games for one strategy.</summary>
public sealed record SimulationSummary(
    string Strategy,
    int Games,
    int Cards,
    double[] SortedTotals,
    double[] SortedLuckFree,
    double[] AverageCategoryPoints,
    double UpperBonusRate)
{
    [JsonIgnore]
    public double Mean => Average(SortedTotals);

    [JsonIgnore]
    public double StandardDeviation => Deviation(SortedTotals);

    [JsonIgnore]
    public double LuckFreeMean => Average(SortedLuckFree);

    [JsonIgnore]
    public double LuckFreeStandardDeviation => Deviation(SortedLuckFree);

    /// <summary>Share of values strictly below <paramref name="value"/>, plus half of the ties.</summary>
    public static double PercentileOf(double[] sorted, double value)
    {
        int below = sorted.Count(t => t < value);
        int equal = sorted.Count(t => t == value);
        return sorted.Length == 0 ? 0 : (below + equal / 2.0) / sorted.Length;
    }

    public static double Quantile(double[] sorted, double q) => sorted.Length == 0
        ? 0
        : sorted[Math.Clamp((int)Math.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];

    private static double Average(double[] values) => values.Length == 0 ? 0 : values.Average();

    private static double Deviation(double[] values)
    {
        if (values.Length < 2)
        {
            return 0;
        }

        double mean = values.Average();
        return Math.Sqrt(values.Sum(t => (t - mean) * (t - mean)) / (values.Length - 1));
    }
}

/// <summary>
/// Plays Full Sheet games with a strategy, using seeded random dice so results are reproducible.
/// Optionally writes the game as an action log in the same shape as the export, so the replay can be tested on it.
/// </summary>
public sealed class BotSimulator(SheetEvaluator evaluator)
{
    public SimulationSummary Run(PlacementScoring scoring, int games, int seed, int cards = YahtzeeGameEngine.GameCount)
    {
        var totals = new double[games];
        var luckFree = new double[games];
        var categoryPoints = new long[games, DiceTables.CategoryCount];
        var bonuses = new int[games];
        Parallel.For(0, games, i =>
        {
            var game = Play(scoring, new Random(unchecked(seed * 7919 + i)), cards);
            totals[i] = game.Total;
            luckFree[i] = game.LuckFree;
            bonuses[i] = game.UpperBonuses;
            for (int c = 0; c < DiceTables.CategoryCount; c++)
            {
                categoryPoints[i, c] = game.CategoryPoints[c];
            }
        });

        int boxes = Math.Max(1, games * cards);
        var averages = new double[DiceTables.CategoryCount];
        for (int c = 0; c < averages.Length; c++)
        {
            long sum = 0;
            for (int i = 0; i < games; i++)
            {
                sum += categoryPoints[i, c];
            }

            averages[c] = (double)sum / boxes;
        }

        return new SimulationSummary(StrategyName(scoring), games, cards, [.. totals.Order()], [.. luckFree.Order()], averages, (double)bonuses.Sum() / boxes);
    }

    public static string StrategyName(PlacementScoring scoring) => scoring switch
    {
        PlacementScoring.CardValues => "Reference strategy",
        _ => "Greedy (points now)"
    };

    public SimulatedGame Play(PlacementScoring scoring, Random random, int cards = YahtzeeGameEngine.GameCount, string? gameId = null)
    {
        var tables = evaluator.Tables;
        var board = new CardPosition[cards];
        var cardPoints = new int[cards];
        var categoryPoints = new int[DiceTables.CategoryCount];
        var turnValues = new TurnValues(tables);
        List<ActionRow>? actions = gameId == null ? null : [];
        int seq = 0;
        int totalTurns = cards * DiceTables.CategoryCount;
        var dice = new int[5];
        var held = new bool[5];
        double luckFree = scoring == PlacementScoring.Greedy ? 0 : board.Sum(evaluator.Solver.Value);

        for (int turn = 1; turn <= totalTurns; turn++)
        {
            evaluator.Evaluate(board, scoring, turnValues);
            luckFree += turnValues.Start;
            Array.Clear(held);
            int rollNumber = 0;
            int roll;
            while (true)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (!held[i])
                    {
                        dice[i] = random.Next(1, 7);
                    }
                }

                rollNumber++;
                Add("Roll", turn, rollNumber, dice);
                roll = tables.RollIndex(dice);
                if (rollNumber == YahtzeeGameEngine.RollsPerTurn)
                {
                    break;
                }

                int keep = turnValues.ArgBestKeep(roll, rollNumber);
                if (keep == tables.FullKeepOfRoll[roll])
                {
                    break;
                }

                SetHolds(tables.KeepCounts[keep], turn, rollNumber);
            }

            int choice = turnValues.FinalChoice[roll];
            int card = choice / DiceTables.CategoryCount;
            int category = choice % DiceTables.CategoryCount;
            int points = tables.Points[roll * DiceTables.CategoryCount + category];
            bool valid = ScoreRules.EvaluateCategory((ScoreCategory)category, dice).IsValid;
            Add(valid ? "Score" : "Scratch", turn, rollNumber, dice, gameIndex: card, category: (ScoreCategory)category, points: points);
            cardPoints[card] += board[card].Reward(category, points);
            categoryPoints[category] += points;
            board[card] = board[card].After(category, points);
        }

        int bonuses = board.Count(p => p.UpperSum >= GameCardState.UpperBonusThreshold);
        return new SimulatedGame(cardPoints.Sum(), luckFree, cardPoints, categoryPoints, bonuses, actions);

        void SetHolds(int[] keepCounts, int turn, int rollNumber)
        {
            var remaining = (int[])keepCounts.Clone();
            var want = new bool[5];
            for (int i = 0; i < 5; i++)
            {
                if (remaining[dice[i] - 1] > 0)
                {
                    remaining[dice[i] - 1]--;
                    want[i] = true;
                }
            }

            for (int i = 0; i < 5; i++)
            {
                if (held[i] != want[i])
                {
                    held[i] = want[i];
                    Add(want[i] ? "Hold" : "Unhold", turn, rollNumber, dice, dieId: i + 1);
                }
            }
        }

        void Add(string action, int turn, int rollNumber, int[] values, int? dieId = null, int? gameIndex = null, ScoreCategory? category = null, int? points = null)
        {
            if (actions == null)
            {
                return;
            }

            seq++;
            actions.Add(new ActionRow(
                seq,
                "Bot",
                "bot",
                gameId!,
                seq,
                "FullSheet",
                action,
                1_000_000L + seq * 1000L,
                turn,
                rollNumber,
                string.Concat(values),
                dieId,
                gameIndex,
                category?.ToString(),
                points,
                false));
        }
    }
}
