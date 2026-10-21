using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Tests;

public class SolverTests
{
    private static readonly DiceTables Tables = DiceTables.Shared;

    [Fact]
    public void DiceTablesHaveEveryRollAndKeep()
    {
        Assert.Equal(252, Tables.RollCount);
        Assert.Equal(462, Tables.KeepCountTotal);
    }

    [Fact]
    public void TransitionProbabilitiesSumToOne()
    {
        for (int k = 0; k < Tables.KeepCountTotal; k++)
        {
            double sum = 0;
            for (int i = Tables.TransitionStart[k]; i < Tables.TransitionStart[k + 1]; i++)
            {
                sum += Tables.TransitionProbability[i];
            }

            Assert.Equal(1.0, sum, 12);
        }
    }

    [Fact]
    public void KeepingAllFiveDiceKeepsTheRoll()
    {
        int roll = Tables.RollIndex([2, 2, 3, 5, 6]);
        int keep = Tables.FullKeepOfRoll[roll];
        Assert.Equal(1, Tables.TransitionStart[keep + 1] - Tables.TransitionStart[keep]);
        Assert.Equal(roll, Tables.TransitionRoll[Tables.TransitionStart[keep]]);
    }

    [Theory]
    [InlineData(new[] { 3, 3, 3, 3, 3 }, 11, 50)]
    [InlineData(new[] { 3, 3, 3, 3, 3 }, 8, 0)]
    [InlineData(new[] { 1, 2, 3, 4, 6 }, 6, 0)]
    [InlineData(new[] { 2, 3, 4, 5, 6 }, 10, 40)]
    [InlineData(new[] { 2, 2, 5, 5, 5 }, 8, 25)]
    [InlineData(new[] { 6, 6, 1, 2, 6 }, 5, 18)]
    public void PointsFollowTheFullSheetRules(int[] dice, int category, int expected)
    {
        Assert.Equal(expected, Tables.Points[Tables.RollIndex(dice) * 13 + category]);
    }

    [Fact]
    public void SingleCardOptimumMatchesThePublishedValue()
    {
        // Optimal solitaire Yahtzee without Yahtzee bonuses or Joker rules scores 245.87 on average.
        Assert.Equal(245.87, TestData.Solver.StartValue, 2);
    }

    [Fact]
    public void FullCardIsWorthNothing()
    {
        Assert.Equal(0, TestData.Solver.Value(new CardPosition(CardPosition.FullMask, 70)));
    }

    [Fact]
    public void OnlyChanceOpenIsWorthTheBestThreeRollAverage()
    {
        // Each die is kept on 5 or 6 after roll 1 and on 4 or more after roll 2, which averages 14/3.
        var position = new CardPosition(CardPosition.FullMask & ~(1 << 12), 70);
        Assert.Equal(5 * 14.0 / 3, TestData.Solver.Value(position), 4);
    }

    [Fact]
    public void OnlyYahtzeeOpenIsFiftyTimesTheYahtzeeChance()
    {
        // Chasing five of a kind with three rolls succeeds 4.6029% of the time.
        var position = new CardPosition(CardPosition.FullMask & ~(1 << 11), 70);
        Assert.Equal(50 * 0.046029, TestData.Solver.Value(position), 3);
    }

    [Fact]
    public void ReachingSixtyThreeAddsTheUpperBonus()
    {
        var position = new CardPosition(0b111110, 60);
        Assert.Equal(3 + 35, position.Reward(0, 3));
        Assert.Equal(2, position.Reward(0, 2));
        Assert.Equal(1, new CardPosition(0b111110, 63).Reward(0, 1));
    }

    [Fact]
    public void SimulatedSingleCardAveragesTheSolverValue()
    {
        var summary = new BotSimulator(TestData.Evaluator).Run(PlacementScoring.CardValues, 2000, 7, cards: 1);
        double standardError = summary.StandardDeviation / Math.Sqrt(summary.Games);
        Assert.InRange(summary.Mean, TestData.Solver.StartValue - 4 * standardError, TestData.Solver.StartValue + 4 * standardError);
    }

    [Fact]
    public void ReferenceStrategyBeatsGreedy()
    {
        var simulator = new BotSimulator(TestData.Evaluator);
        var reference = simulator.Run(PlacementScoring.CardValues, 100, 3);
        var greedy = simulator.Run(PlacementScoring.Greedy, 100, 3);
        Assert.True(reference.Mean > greedy.Mean + 150, $"reference {reference.Mean}, greedy {greedy.Mean}");
    }

    [Fact]
    public void SimulationIsReproducible()
    {
        var simulator = new BotSimulator(TestData.Evaluator);
        var first = simulator.Run(PlacementScoring.CardValues, 20, 11);
        var second = simulator.Run(PlacementScoring.CardValues, 20, 11);
        Assert.Equal(first.SortedTotals, second.SortedTotals);
        Assert.Equal(first.SortedLuckFree, second.SortedLuckFree);
    }

    [Fact]
    public void EvaluatorBestCellMatchesRankedPlacements()
    {
        var board = new CardPosition[6];
        board[2] = new CardPosition(0b1_0000_0000_0001, 3);
        var values = TestData.Evaluator.Evaluate(board, PlacementScoring.CardValues);
        int roll = Tables.RollIndex([4, 4, 4, 2, 6]);
        var ranked = TestData.Evaluator.RankPlacements(values, board, roll);
        Assert.Equal(6 * 13 - 2, ranked.Count);
        Assert.Equal(values.Final[roll], ranked[0].Value, 12);
        Assert.DoesNotContain(ranked, p => p.Card == 2 && (p.Category == 0 || p.Category == 12));
    }
}
