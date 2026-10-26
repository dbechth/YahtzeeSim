using System.Diagnostics;
using YahtzeeSim.Engine;
using static YahtzeeSim.Engine.Tests.TestHelpers;

namespace YahtzeeSim.Engine.Tests;

// SW-PLT-07 (save/resume), SW-PRB-01 .. SW-PRB-05, SW-NFR-02
public class PersistenceAndProbabilityTests
{
    [Fact]
    public void ChallengeGame_KeepsItsChallengeThroughSaveAndResume()
    {
        var engine = NewGame();
        Assert.True(engine.StartChallenge(GameMode.FullSheet, "abcdefghijklmnop_-12"));
        Assert.Equal(GameMode.FullSheet, engine.Mode);
        Assert.Equal("abcdefghijklmnop_-12", engine.ChallengeId);

        var restored = new YahtzeeGameEngine();
        Assert.True(restored.TryLoad(engine.Serialize()));
        Assert.Equal("abcdefghijklmnop_-12", restored.ChallengeId);
        Assert.Equal(engine.GameId, restored.GameId);

        restored.Reset(GameMode.Single);
        Assert.Null(restored.ChallengeId);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces in it ok")]
    [InlineData("<script>alert(1)</script>")]
    public void ChallengeGame_RefusesMalformedIds(string id)
    {
        var engine = NewGame();
        Assert.False(engine.StartChallenge(GameMode.Single, id));
        Assert.Null(engine.ChallengeId);

        string tampered = engine.Serialize().Replace("\"ChallengeId\":null", $"\"ChallengeId\":\"{id}\"");
        var restored = new YahtzeeGameEngine();
        Assert.True(restored.TryLoad(tampered));
        Assert.Null(restored.ChallengeId);
    }

    [Fact]
    public void Save_RoundTripsFullState()
    {
        var engine = NewGame(GameMode.FullSheet, seed: 5);
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.TryApplyScore(2, ScoreCategory.Threes, "#ABCDEF");
        engine.SetRoll(1, 2, 3, 4, 6);
        Assert.True(engine.TryScratch(1, ScoreCategory.Yahtzee, Pen).Applied);
        engine.SetRoll(6, 6, 6, 6, 6);
        engine.ToggleHold(2);
        engine.Cards[0].YahtzeeBonusCount = 2;

        var restored = NewGame();
        Assert.True(restored.TryLoad(engine.Serialize()));

        Assert.Equal(engine.Mode, restored.Mode);
        Assert.Equal(engine.RollCount, restored.RollCount);
        Assert.Equal(engine.Dice.Select(d => (d.Id, d.Value, d.IsHeld)), restored.Dice.Select(d => (d.Id, d.Value, d.IsHeld)));
        Assert.Equal(2, restored.Cards[0].YahtzeeBonusCount);
        Assert.Equal(9, restored.Cards[2].Scores[ScoreCategory.Threes].Value);
        Assert.Equal("#ABCDEF", restored.Cards[2].Scores[ScoreCategory.Threes].PenColorHex);
        Assert.True(restored.Cards[1].Scores[ScoreCategory.Yahtzee].IsScratch);
        Assert.True(restored.CanUndo);
    }

    [Fact]
    public void Save_PreservesUndoSnapshot()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.TryApplyScore(0, ScoreCategory.Threes, Pen);

        var restored = NewGame();
        Assert.True(restored.TryLoad(engine.Serialize()));
        Assert.True(restored.Undo());

        Assert.False(restored.Cards[0].Scores[ScoreCategory.Threes].IsFilled);
        Assert.Equal(1, restored.RollCount);
    }

    [Fact]
    public void Save_PreservesGameId_AndResetIssuesANewOne()
    {
        var engine = NewGame();
        var restored = NewGame();
        Assert.True(restored.TryLoad(engine.Serialize()));
        Assert.Equal(engine.GameId, restored.GameId);

        var before = engine.GameId;
        engine.Reset(GameMode.Single);
        Assert.NotEqual(before, engine.GameId);
    }

    [Fact]
    public void Save_WithoutGameId_StillLoadsWithAFreshId()
    {
        var engine = NewGame();
        var json = System.Text.RegularExpressions.Regex.Replace(engine.Serialize(), "\"GameId\":\"[^\"]*\",?", string.Empty);
        Assert.DoesNotContain("GameId", json);

        var restored = NewGame();
        Assert.True(restored.TryLoad(json));
        Assert.NotEqual(Guid.Empty, restored.GameId);
    }

    [Fact]
    public void FinalScore_IsNullUntilComplete_ThenSumsAllCards()
    {
        var engine = NewGame(GameMode.FullSheet);
        Assert.Null(engine.FinalScore);

        for (int g = 0; g < YahtzeeGameEngine.GameCount; g++)
        {
            engine.FillAllExcept(g);
            engine.Fill(g, ScoreCategory.Chance, 20);
        }

        Assert.True(engine.IsGameComplete);
        Assert.Equal(6 * 20, engine.FinalScore);
    }

    [Fact]
    public void Save_OfANewGameRestoresABlankGame()
    {
        var engine = NewGame();
        var restored = NewGame(GameMode.FullSheet);
        Assert.True(restored.TryLoad(engine.Serialize()));
        Assert.Equal(GameMode.Single, restored.Mode);
        Assert.Equal(0, restored.RollCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Version\":99}")]
    [InlineData("[1,2,3]")]
    public void Load_RejectsUnusableDataAndLeavesStateUnchanged(string? json)
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);

        Assert.False(engine.TryLoad(json));

        Assert.Equal(1, engine.RollCount);
        Assert.Equal(3, engine.Dice[0].Value);
    }

    [Fact]
    public void Load_RejectsTamperedState()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        string json = engine.Serialize();

        Assert.False(NewGame().TryLoad(json.Replace("\"RollCount\":1", "\"RollCount\":9")));
        Assert.False(NewGame().TryLoad(json.Replace("\"Value\":3", "\"Value\":42")));
    }

    [Fact]
    public void Probabilities_CoverEveryCategoryAndStayInRange()
    {
        var engine = NewGame();
        engine.SetRoll(1, 2, 3, 5, 5);
        var probabilities = engine.CalculateProbabilities();

        Assert.Equal(13, probabilities.Count);
        Assert.All(probabilities.Values, p => Assert.InRange(p, 0d, 1d));
    }

    [Fact]
    public void Probabilities_WithNoRollsLeftAreExactlyZeroOrOne()
    {
        var engine = NewGame();
        engine.RollDice();
        engine.RollDice();
        engine.RollDice();
        engine.SetRoll(3, 3, 3, 3, 2);
        var probabilities = engine.CalculateProbabilities();

        Assert.Equal(1d, probabilities[ScoreCategory.FourOfAKind]);
        Assert.Equal(1d, probabilities[ScoreCategory.Chance]);
        Assert.Equal(0d, probabilities[ScoreCategory.Yahtzee]);
        Assert.Equal(0d, probabilities[ScoreCategory.LargeStraight]);
    }

    [Fact]
    public void Probabilities_AreIdenticalForTheSameState()
    {
        var first = NewGame(seed: 1);
        var second = NewGame(seed: 999);
        first.SetRoll(1, 2, 4, 4, 6);
        second.SetRoll(1, 2, 4, 4, 6);

        Assert.Equal(first.CalculateProbabilities(), second.CalculateProbabilities());
        Assert.Equal(first.CalculateProbabilities(), first.CalculateProbabilities());
    }

    [Fact]
    public void Probabilities_UpperCategoriesUseThreeOfAKindExactly()
    {
        // Holding nothing useful with two rolls left: P(at least three of a face in 5 dice over two rolls with holds).
        var probabilities = ProbabilityCalculator.Calculate([1, 2, 3, 4, 6], 2);
        Assert.Equal(probabilities[ScoreCategory.Twos], probabilities[ScoreCategory.Threes], 12);
        Assert.Equal(probabilities[ScoreCategory.Aces], probabilities[ScoreCategory.Sixes], 12);
    }

    [Fact]
    public void Probabilities_ImproveWithMoreMatchingDice()
    {
        var weak = ProbabilityCalculator.Calculate([1, 2, 3, 4, 6], 2);
        var strong = ProbabilityCalculator.Calculate([5, 5, 5, 4, 6], 2);
        Assert.True(strong[ScoreCategory.Fives] > weak[ScoreCategory.Fives]);
        Assert.Equal(1d, strong[ScoreCategory.ThreeOfAKind]);
    }

    [Fact]
    public void Probabilities_CalculateWellUnderOneHundredMilliseconds()
    {
        // Worst case: a hand that satisfies nothing, with two rolls left, so every lower category is simulated.
        int[] dice = [1, 2, 4, 6, 6];
        ProbabilityCalculator.Calculate(dice, 2); // warm up the JIT

        double best = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            ProbabilityCalculator.Calculate(dice, 2);
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        Assert.True(best < 100, $"Probability update took {best:F1} ms (limit 100 ms)");
    }
}
