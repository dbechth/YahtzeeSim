namespace YahtzeeSim.Analysis.Solver;

/// <summary>How a strategy values putting the dice in a box.</summary>
public enum PlacementScoring
{
    /// <summary>Points now only (the greedy baseline).</summary>
    Greedy,

    /// <summary>
    /// Points now (plus any upper bonus reached) plus the change in that card's expected future points from the exact
    /// single-card solver. This is the reference strategy.
    /// </summary>
    CardValues
}

/// <summary>One open box for a roll, with the value the strategy gives it.</summary>
public readonly record struct Placement(int Card, int Category, int Points, double Value);

/// <summary>
/// Values Full Sheet decisions. Six cards are in play and each turn's dice may go in any open box on any card.
/// Every value is measured on the same yardstick, points now plus the change in the expected points still to come,
/// so the values of different choices can be compared and subtracted.
/// </summary>
public sealed class SheetEvaluator(CardSolver solver, DiceTables tables)
{
    public const PlacementScoring ReferenceScoring = PlacementScoring.CardValues;

    private const int Categories = DiceTables.CategoryCount;

    public CardSolver Solver { get; } = solver;

    public DiceTables Tables { get; } = tables;

    /// <summary>Value of every open cell for every roll, the best cell per roll, and the keep values built on them.</summary>
    public TurnValues Evaluate(IReadOnlyList<CardPosition> board, PlacementScoring scoring, TurnValues? reuse = null)
    {
        var turn = reuse ?? new TurnValues(Tables);
        int cards = board.Count;
        turn.PrepareCells(cards);

        // Value added by each cell apart from lower-section points, which depend on the roll:
        // upper cells per count of the face (0..5), lower cells once.
        var upperAdd = new double[cards * 6 * 6];
        var lowerAdd = new double[cards * Categories];
        for (int card = 0; card < cards; card++)
        {
            var position = board[card];
            double before = scoring == PlacementScoring.Greedy ? 0 : Solver.Value(position);
            for (int c = 0; c < Categories; c++)
            {
                if (position.IsFilled(c))
                {
                    continue;
                }

                if (c < 6)
                {
                    int face = c + 1;
                    for (int count = 0; count <= 5; count++)
                    {
                        upperAdd[(card * 6 + c) * 6 + count] = position.Reward(c, face * count) + Future(position, c, face * count, before);
                    }
                }
                else
                {
                    lowerAdd[card * Categories + c] = Future(position, c, 0, before);
                }
            }
        }

        var cells = turn.CellValues;
        int stride = cards * Categories;
        for (int r = 0; r < Tables.RollCount; r++)
        {
            double best = double.NegativeInfinity;
            int choice = -1;
            int pointsRow = r * Categories;
            int cellRow = r * stride;
            for (int card = 0; card < cards; card++)
            {
                var position = board[card];
                for (int c = 0; c < Categories; c++)
                {
                    if (position.IsFilled(c))
                    {
                        cells[cellRow + card * Categories + c] = double.NaN;
                        continue;
                    }

                    int points = Tables.Points[pointsRow + c];
                    double v = c < 6
                        ? upperAdd[(card * 6 + c) * 6 + points / (c + 1)]
                        : points + lowerAdd[card * Categories + c];
                    cells[cellRow + card * Categories + c] = v;
                    if (v > best + 1e-12)
                    {
                        best = v;
                        choice = card * Categories + c;
                    }
                }
            }

            turn.Final[r] = choice < 0 ? 0 : best;
            turn.FinalChoice[r] = choice;
        }

        turn.Compute();
        return turn;

        double Future(CardPosition position, int category, int points, double before) => scoring == PlacementScoring.Greedy
            ? 0
            : Solver.Value(position.After(category, points)) - before;
    }

    /// <summary>Every open box for a roll from an evaluated turn, best first (ties keep card then box order).</summary>
    public List<Placement> RankPlacements(TurnValues turn, IReadOnlyList<CardPosition> board, int roll)
    {
        var list = new List<Placement>();
        for (int card = 0; card < board.Count; card++)
        {
            for (int c = 0; c < Categories; c++)
            {
                if (!board[card].IsFilled(c))
                {
                    list.Add(new Placement(card, c, Tables.Points[roll * Categories + c], turn.CellValue(roll, card, c)));
                }
            }
        }

        return [.. list.OrderByDescending(p => p.Value)];
    }
}
