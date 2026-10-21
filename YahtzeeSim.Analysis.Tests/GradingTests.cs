using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Tests;

public class GradingTests
{
    [Fact]
    public void ReferenceBotMatchesItselfEverywhere()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 21);
        var graded = new DecisionGrader(TestData.Evaluator).Grade(GameReplayer.Replay(score, actions));
        Assert.All(graded.Decisions, d => Assert.True(d.Cost < 1e-9, $"turn {d.Turn} {d.Kind} cost {d.Cost}"));
        Assert.All(graded.Decisions, d => Assert.True(d.MatchesReference));
    }

    [Theory]
    [InlineData(PlacementScoring.CardValues, 22)]
    [InlineData(PlacementScoring.Greedy, 23)]
    public void TotalSplitsExactlyIntoBaselineChoiceLuckAndCost(PlacementScoring scoring, int seed)
    {
        var (score, actions) = TestData.BotGame(scoring, seed);
        var graded = new DecisionGrader(TestData.Evaluator).Grade(GameReplayer.Replay(score, actions));
        Assert.Equal(6 * TestData.Solver.StartValue, graded.Baseline, 9);
        Assert.True(Math.Abs(graded.Residual) < 1e-6, $"residual {graded.Residual}");
        foreach (var turn in graded.Turns)
        {
            Assert.Equal(turn.Realized, turn.Expected + turn.Luck - turn.Cost, 9);
        }
    }

    [Fact]
    public void GreedyPlayIsChargedForItsChoices()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.Greedy, 24);
        var graded = new DecisionGrader(TestData.Evaluator).Grade(GameReplayer.Replay(score, actions));
        Assert.All(graded.Decisions, d => Assert.True(d.Cost >= 0));
        Assert.True(graded.Cost > 50, $"cost {graded.Cost}");
        Assert.Contains(graded.Decisions, d => d.Kind == DecisionKind.Box && !d.MatchesReference);
    }

    [Fact]
    public void EveryTurnHasOneBoxDecisionAndOneDecisionPerExtraRollOrStop()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.Greedy, 25);
        var game = GameReplayer.Replay(score, actions);
        var graded = new DecisionGrader(TestData.Evaluator).Grade(game);
        foreach (var (turn, gradedTurn) in game.Turns.Zip(graded.Turns))
        {
            Assert.Single(gradedTurn.Decisions, d => d.Kind == DecisionKind.Box);
            int keeps = gradedTurn.Decisions.Count(d => d.IsKeep);
            int stops = gradedTurn.Decisions.Count(d => d.IsStop);
            Assert.Equal(turn.Rolls.Count - 1, keeps);
            Assert.Equal(turn.Rolls.Count < 3 ? 1 : 0, stops);
        }
    }

    [Theory]
    [InlineData(new int[0], KeepPattern.None)]
    [InlineData(new[] { 4 }, KeepPattern.Single)]
    [InlineData(new[] { 6, 6 }, KeepPattern.Pair)]
    [InlineData(new[] { 2, 2, 2 }, KeepPattern.ThreeOfAKind)]
    [InlineData(new[] { 5, 5, 5, 5 }, KeepPattern.FourOfAKind)]
    [InlineData(new[] { 3, 3, 5, 5 }, KeepPattern.TwoPair)]
    [InlineData(new[] { 2, 3, 4 }, KeepPattern.StraightDraw)]
    [InlineData(new[] { 1, 3, 4, 5 }, KeepPattern.StraightDraw)]
    [InlineData(new[] { 1, 2, 6 }, KeepPattern.Mixed)]
    [InlineData(new[] { 5, 6 }, KeepPattern.HighPair)]
    [InlineData(new[] { 3, 3, 3, 6 }, KeepPattern.Mixed)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, KeepPattern.AllFive)]
    public void KeepPatternsFollowTheFixedRules(int[] kept, KeepPattern expected)
    {
        Assert.Equal(expected, KeepPatterns.Classify(kept));
    }
}
