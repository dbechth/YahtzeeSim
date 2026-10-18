using YahtzeeSim.Engine;

namespace YahtzeeSim.Engine.Tests;

// SW-RUL-01 .. SW-RUL-08, SW-RUL-12
public class ScoreRulesTests
{
    [Fact]
    public void Categories_AreInOfficialOrder()
    {
        Assert.Equal(
            [
                ScoreCategory.Aces, ScoreCategory.Twos, ScoreCategory.Threes, ScoreCategory.Fours, ScoreCategory.Fives, ScoreCategory.Sixes,
                ScoreCategory.ThreeOfAKind, ScoreCategory.FourOfAKind, ScoreCategory.FullHouse, ScoreCategory.SmallStraight,
                ScoreCategory.LargeStraight, ScoreCategory.Yahtzee, ScoreCategory.Chance
            ],
            ScoreRules.ScoringCategories);
        Assert.Equal(6, ScoreRules.UpperCategories.Count);
        Assert.Equal(7, ScoreRules.LowerCategories.Count);
    }

    [Fact]
    public void DisplayNames_AreHumanReadable()
    {
        Assert.Equal("Three of a Kind", ScoreRules.GetDisplayName(ScoreCategory.ThreeOfAKind));
        Assert.Equal("Large Straight", ScoreRules.GetDisplayName(ScoreCategory.LargeStraight));
        Assert.Equal("Aces", ScoreRules.GetDisplayName(ScoreCategory.Aces));
    }

    [Theory]
    [InlineData(ScoreCategory.Aces, new[] { 1, 1, 3, 4, 6 }, true, 2)]
    [InlineData(ScoreCategory.Sixes, new[] { 6, 6, 6, 2, 1 }, true, 18)]
    [InlineData(ScoreCategory.Fours, new[] { 1, 2, 3, 5, 6 }, false, 0)]
    public void UpperCategory_ScoresFaceTimesCount(ScoreCategory category, int[] dice, bool valid, int score)
    {
        Assert.Equal((valid, score), ScoreRules.EvaluateCategory(category, dice));
    }

    [Theory]
    [InlineData(new[] { 3, 3, 3, 1, 2 }, true, 12)]
    [InlineData(new[] { 3, 3, 2, 1, 2 }, false, 11)]
    [InlineData(new[] { 6, 6, 6, 6, 6 }, true, 30)]
    public void ThreeOfAKind_ScoresSumOfAllDice(int[] dice, bool valid, int score)
    {
        var result = ScoreRules.EvaluateCategory(ScoreCategory.ThreeOfAKind, dice);
        Assert.Equal(valid, result.IsValid);
        if (valid)
        {
            Assert.Equal(score, result.Score);
        }
    }

    [Theory]
    [InlineData(new[] { 4, 4, 4, 4, 1 }, true, 17)]
    [InlineData(new[] { 4, 4, 4, 1, 1 }, false, 0)]
    [InlineData(new[] { 2, 2, 2, 2, 2 }, true, 10)]
    public void FourOfAKind_ScoresSumOfAllDice(int[] dice, bool valid, int score)
    {
        var result = ScoreRules.EvaluateCategory(ScoreCategory.FourOfAKind, dice);
        Assert.Equal(valid, result.IsValid);
        if (valid)
        {
            Assert.Equal(score, result.Score);
        }
    }

    [Theory]
    [InlineData(new[] { 2, 2, 3, 3, 3 }, true)]
    [InlineData(new[] { 5, 5, 5, 1, 1 }, true)]
    [InlineData(new[] { 5, 5, 5, 5, 1 }, false)]
    [InlineData(new[] { 5, 5, 5, 5, 5 }, false)] // house rule: five of a kind is not a Full House
    [InlineData(new[] { 1, 2, 3, 4, 5 }, false)]
    public void FullHouse_RequiresExactlyThreePlusTwo(int[] dice, bool valid)
    {
        var result = ScoreRules.EvaluateCategory(ScoreCategory.FullHouse, dice);
        Assert.Equal(valid, result.IsValid);
        Assert.Equal(25, result.Score);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 6 }, true)]
    [InlineData(new[] { 2, 3, 4, 5, 5 }, true)]
    [InlineData(new[] { 3, 4, 5, 6, 6 }, true)]
    [InlineData(new[] { 1, 2, 3, 5, 6 }, false)]
    [InlineData(new[] { 1, 1, 2, 2, 3 }, false)]
    public void SmallStraight_NeedsFourConsecutiveFaces(int[] dice, bool valid)
    {
        var result = ScoreRules.EvaluateCategory(ScoreCategory.SmallStraight, dice);
        Assert.Equal(valid, result.IsValid);
        Assert.Equal(30, result.Score);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, true)]
    [InlineData(new[] { 2, 3, 4, 5, 6 }, true)]
    [InlineData(new[] { 1, 2, 3, 4, 6 }, false)]
    [InlineData(new[] { 2, 3, 4, 5, 5 }, false)]
    public void LargeStraight_NeedsFiveConsecutiveFaces(int[] dice, bool valid)
    {
        var result = ScoreRules.EvaluateCategory(ScoreCategory.LargeStraight, dice);
        Assert.Equal(valid, result.IsValid);
        Assert.Equal(40, result.Score);
    }

    [Fact]
    public void Yahtzee_ScoresFiftyOnlyForFiveOfAKind()
    {
        Assert.Equal((true, 50), ScoreRules.EvaluateCategory(ScoreCategory.Yahtzee, new[] { 3, 3, 3, 3, 3 }));
        Assert.False(ScoreRules.EvaluateCategory(ScoreCategory.Yahtzee, new[] { 3, 3, 3, 3, 4 }).IsValid);
    }

    [Fact]
    public void Chance_IsAlwaysValidAndSumsDice()
    {
        Assert.Equal((true, 17), ScoreRules.EvaluateCategory(ScoreCategory.Chance, new[] { 1, 2, 4, 4, 6 }));
    }

    [Fact]
    public void IsFiveOfAKind_RejectsBlankAndMixedDice()
    {
        Assert.True(ScoreRules.IsFiveOfAKind(new[] { 2, 2, 2, 2, 2 }));
        Assert.False(ScoreRules.IsFiveOfAKind(new[] { 0, 0, 0, 0, 0 }));
        Assert.False(ScoreRules.IsFiveOfAKind(new[] { 2, 2, 2, 2, 3 }));
    }

    [Fact]
    public void JokerScoring_AwardsFullValueForFullHouseAndStraights()
    {
        int[] dice = [4, 4, 4, 4, 4];
        Assert.Equal((true, 25), ScoreRules.EvaluateJokerCategory(ScoreCategory.FullHouse, dice));
        Assert.Equal((true, 30), ScoreRules.EvaluateJokerCategory(ScoreCategory.SmallStraight, dice));
        Assert.Equal((true, 40), ScoreRules.EvaluateJokerCategory(ScoreCategory.LargeStraight, dice));
        Assert.Equal((true, 20), ScoreRules.EvaluateJokerCategory(ScoreCategory.ThreeOfAKind, dice));
        Assert.Equal((true, 20), ScoreRules.EvaluateJokerCategory(ScoreCategory.Chance, dice));
    }
}
