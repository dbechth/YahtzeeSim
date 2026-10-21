using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Grading;

public enum DecisionKind
{
    /// <summary>Which dice to keep after roll 1.</summary>
    KeepAfterRoll1,

    /// <summary>Which dice to keep after roll 2.</summary>
    KeepAfterRoll2,

    /// <summary>Scored after roll 1 instead of rolling again.</summary>
    StopAfterRoll1,

    /// <summary>Scored after roll 2 instead of rolling again.</summary>
    StopAfterRoll2,

    /// <summary>Which box the final dice went in.</summary>
    Box
}

/// <summary>A box choice: card (0-based), category and points.</summary>
public sealed record BoxChoice(int Card, ScoreCategory Category, int Points, bool Scratch)
{
    public string Text => $"Game {Card + 1} {ScoreRules.GetDisplayName(Category)} {(Scratch ? "X" : Points.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
}

/// <summary>
/// One graded decision. Values are expected points (this turn plus the change in the cards' expected future points)
/// under the reference strategy; <see cref="Cost"/> = best - chosen and is never negative.
/// </summary>
public sealed record GradedDecision(
    string GameId,
    string Player,
    int Turn,
    int RollNumber,
    DecisionKind Kind,
    string Dice,
    string Chosen,
    string Best,
    double ChosenValue,
    double BestValue,
    KeepPattern? ChosenPattern,
    KeepPattern? BestPattern,
    BoxChoice? ChosenBox,
    BoxChoice? BestBox,
    double ThinkSeconds)
{
    public const double MatchTolerance = 1e-6;

    public double Cost => Math.Max(0, BestValue - ChosenValue);

    public bool MatchesReference => Cost < MatchTolerance;

    public int TurnsRemaining => YahtzeeGameEngine.GameCount * 13 - Turn;

    public bool IsKeep => Kind is DecisionKind.KeepAfterRoll1 or DecisionKind.KeepAfterRoll2;

    public bool IsStop => Kind is DecisionKind.StopAfterRoll1 or DecisionKind.StopAfterRoll2;

    /// <summary>Dice kept by the player (5 when they stopped); 0 for box decisions.</summary>
    public int ChosenKeepSize { get; init; }

    /// <summary>Dice kept by the reference strategy (5 when it would stop); 0 for box decisions.</summary>
    public int BestKeepSize { get; init; }
}

/// <summary>
/// One turn split into parts that add up exactly:
/// <c>Realized = Expected + Luck - Cost</c>, where Realized is the points scored plus the change in that card's
/// expected future points, Expected is the turn's value before rolling, Luck is how much better or worse the dice
/// came out than expected, and Cost is the expected points given up by the player's choices.
/// </summary>
public sealed record GradedTurn(int Turn, double Expected, double Luck, double Cost, double Realized, IReadOnlyList<GradedDecision> Decisions);

/// <summary>
/// A graded game. <c>Total = Baseline + FullSheetChoice + Luck - Cost</c> holds exactly:
/// Baseline is six cards at the single-card optimum, FullSheetChoice is the extra value of choosing among all open
/// boxes every turn, Luck is the dice, Cost is the sum of decision costs.
/// </summary>
public sealed record GradedGame(ReplayedGame Game, double Baseline, IReadOnlyList<GradedTurn> Turns)
{
    public double FullSheetChoice => Turns.Sum(t => t.Expected);

    public double Luck => Turns.Sum(t => t.Luck);

    public double Cost => Turns.Sum(t => t.Cost);

    public IEnumerable<GradedDecision> Decisions => Turns.SelectMany(t => t.Decisions);

    /// <summary>Difference between the actual total and the sum of the parts (rounding only; checked in tests).</summary>
    public double Residual => Game.Total - (Baseline + FullSheetChoice + Luck - Cost);
}

/// <summary>Grades every keep, stop and box decision of a replayed game against the reference strategy.</summary>
public sealed class DecisionGrader(SheetEvaluator evaluator)
{
    public GradedGame Grade(ReplayedGame game)
    {
        var tables = evaluator.Tables;
        var values = new TurnValues(tables);
        var turns = new List<GradedTurn>(game.Turns.Count);
        double baseline = game.Turns[0].BoardBefore.Sum(p => evaluator.Solver.Value(p));

        foreach (var turn in game.Turns)
        {
            var board = turn.BoardBefore;
            evaluator.Evaluate(board, SheetEvaluator.ReferenceScoring, values);
            var decisions = new List<GradedDecision>();
            double luck = 0;
            double cost = 0;

            int roll = tables.RollIndex(turn.Rolls[0].Dice);
            luck += values.Roll1[roll] - values.Start;

            for (int i = 1; i < turn.Rolls.Count; i++)
            {
                var previous = turn.Rolls[i - 1];
                var step = turn.Rolls[i];
                int previousRoll = tables.RollIndex(previous.Dice);
                int[] keptDice = step.KeptValues(previous.Dice);
                int keep = tables.KeepIndex(keptDice);
                bool afterFirst = previous.RollNumber == 1;
                double chosen = afterFirst ? values.KeepBeforeRoll2[keep] : values.KeepBeforeRoll3[keep];
                double best = afterFirst ? values.Roll1[previousRoll] : values.Roll2[previousRoll];
                int bestKeep = values.ArgBestKeep(previousRoll, previous.RollNumber);
                decisions.Add(KeepDecision(
                    turn.Turn,
                    afterFirst ? DecisionKind.KeepAfterRoll1 : DecisionKind.KeepAfterRoll2,
                    previous,
                    keptDice,
                    DiceTables.ToDice(tables.KeepCounts[bestKeep]),
                    chosen,
                    best,
                    Seconds(previous.At, step.At)));
                cost += Math.Max(0, best - chosen);

                roll = tables.RollIndex(step.Dice);
                luck += (step.RollNumber == 2 ? values.Roll2[roll] : values.Final[roll]) - chosen;
            }

            var last = turn.Rolls[^1];
            var box = turn.Box!;
            if (last.RollNumber < YahtzeeGameEngine.RollsPerTurn)
            {
                double best = last.RollNumber == 1 ? values.Roll1[roll] : values.Roll2[roll];
                int bestKeep = values.ArgBestKeep(roll, last.RollNumber);
                decisions.Add(KeepDecision(
                    turn.Turn,
                    last.RollNumber == 1 ? DecisionKind.StopAfterRoll1 : DecisionKind.StopAfterRoll2,
                    last,
                    last.Dice,
                    DiceTables.ToDice(tables.KeepCounts[bestKeep]),
                    values.Final[roll],
                    best,
                    Seconds(last.At, box.At)));
                cost += Math.Max(0, best - values.Final[roll]);
            }

            double chosenBox = values.CellValue(roll, box.Card, (int)box.Category);
            int bestChoice = values.FinalChoice[roll];
            var bestBox = ToBox(bestChoice / 13, bestChoice % 13, roll);
            var chosenChoice = new BoxChoice(box.Card, box.Category, box.Points, box.Scratch);
            decisions.Add(new GradedDecision(
                game.GameId,
                game.Nickname,
                turn.Turn,
                last.RollNumber,
                DecisionKind.Box,
                tables.RollText(roll),
                chosenChoice.Text,
                bestBox.Text,
                chosenBox,
                values.Final[roll],
                null,
                null,
                chosenChoice,
                bestBox,
                Seconds(last.At, box.At)));
            cost += Math.Max(0, values.Final[roll] - chosenBox);

            turns.Add(new GradedTurn(turn.Turn, values.Start, luck, cost, chosenBox, decisions));
        }

        return new GradedGame(game, baseline, turns);

        GradedDecision KeepDecision(int turnNumber, DecisionKind kind, RollStep from, int[] kept, int[] bestKept, double chosen, double best, double seconds)
        {
            bool stop = kind is DecisionKind.StopAfterRoll1 or DecisionKind.StopAfterRoll2;
            var bestPattern = KeepPatterns.Classify(bestKept);
            return new GradedDecision(
                game.GameId,
                game.Nickname,
                turnNumber,
                from.RollNumber,
                kind,
                tables.RollText(tables.RollIndex(from.Dice)),
                stop ? "Score now" : KeepText(kept),
                bestKept.Length == 5 ? "Score now" : KeepText(bestKept),
                chosen,
                best,
                stop ? KeepPattern.AllFive : KeepPatterns.Classify(kept),
                bestPattern,
                null,
                null,
                seconds)
            {
                ChosenKeepSize = kept.Length,
                BestKeepSize = bestKept.Length
            };
        }

        BoxChoice ToBox(int card, int category, int roll)
        {
            int points = tables.Points[roll * 13 + category];
            return new BoxChoice(card, (ScoreCategory)category, points, points == 0);
        }
    }

    private static string KeepText(IEnumerable<int> dice)
    {
        var sorted = dice.Order().ToList();
        return sorted.Count == 0 ? "Reroll all" : "Keep " + string.Concat(sorted);
    }

    private static double Seconds(long from, long to) => Math.Max(0, (to - from) / 1000.0);
}
