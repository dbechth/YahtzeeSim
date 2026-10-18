using YahtzeeSim.Engine;
using static YahtzeeSim.Engine.Tests.TestHelpers;

namespace YahtzeeSim.Engine.Tests;

// SW-RUL-11: Yahtzee bonus and Joker rules (Single mode only)
public class YahtzeeBonusAndJokerTests
{
    private static YahtzeeGameEngine GameWithYahtzeeScored(int yahtzeeValue = 50, bool scratch = false, GameMode mode = GameMode.Single)
    {
        var engine = NewGame(mode);
        engine.Fill(0, ScoreCategory.Yahtzee, yahtzeeValue, scratch);
        return engine;
    }

    [Fact]
    public void RollingFiveOfAKind_IsDetected()
    {
        var engine = NewGame();
        Assert.False(engine.IsYahtzeeRolled);
        engine.SetRoll(4, 4, 4, 4, 4);
        Assert.True(engine.IsYahtzeeRolled);
        engine.SetRoll(4, 4, 4, 4, 5);
        Assert.False(engine.IsYahtzeeRolled);
    }

    [Fact]
    public void FirstYahtzee_ScoresFiftyWithNoBonusAndNoJoker()
    {
        var engine = NewGame();
        engine.SetRoll(5, 5, 5, 5, 5);
        Assert.False(engine.IsJokerRoll);

        var result = engine.TryApplyScore(0, ScoreCategory.Yahtzee, Pen);

        Assert.True(result.Applied);
        Assert.Equal(50, result.Score);
        Assert.True(result.WasYahtzee);
        Assert.False(result.YahtzeeBonusAwarded);
        Assert.Equal(0, engine.Cards[0].YahtzeeBonusCount);
    }

    [Fact]
    public void FirstYahtzee_MayBePlacedElsewhereWithNormalRules()
    {
        var engine = NewGame();
        engine.SetRoll(5, 5, 5, 5, 5);

        Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, ScoreCategory.Fives));
        Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, ScoreCategory.ThreeOfAKind));
        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.FullHouse));
        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.LargeStraight));
        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.Aces));
    }

    [Fact]
    public void ExtraYahtzee_AfterFifty_AwardsBonusAndMustGoInMatchingUpperBox()
    {
        var engine = GameWithYahtzeeScored(50);
        engine.SetRoll(4, 4, 4, 4, 4);
        Assert.True(engine.IsJokerRoll);

        Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, ScoreCategory.Fours));
        foreach (var category in ScoreRules.ScoringCategories.Where(c => c != ScoreCategory.Fours))
        {
            Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, category));
        }

        var result = engine.TryApplyScore(0, ScoreCategory.Fours, Pen);

        Assert.True(result.Applied);
        Assert.Equal(20, result.Score);
        Assert.True(result.YahtzeeBonusAwarded);
        Assert.Equal(1, engine.Cards[0].YahtzeeBonusCount);
    }

    [Fact]
    public void ExtraYahtzee_WhenMatchingUpperIsFilled_GoesToAnyOpenLowerBoxWithFullValues()
    {
        var engine = GameWithYahtzeeScored(50);
        engine.Fill(0, ScoreCategory.Fours, 8);
        engine.SetRoll(4, 4, 4, 4, 4);

        foreach (var upper in ScoreRules.UpperCategories)
        {
            Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, upper));
        }

        Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, ScoreCategory.Yahtzee));
        foreach (var lower in ScoreRules.LowerCategories.Where(c => c != ScoreCategory.Yahtzee))
        {
            Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, lower));
        }

        var result = engine.TryApplyScore(0, ScoreCategory.LargeStraight, Pen);

        Assert.True(result.Applied);
        Assert.Equal(40, result.Score);
        Assert.True(result.YahtzeeBonusAwarded);
    }

    [Theory]
    [InlineData(ScoreCategory.FullHouse, 25)]
    [InlineData(ScoreCategory.SmallStraight, 30)]
    [InlineData(ScoreCategory.LargeStraight, 40)]
    [InlineData(ScoreCategory.ThreeOfAKind, 15)]
    [InlineData(ScoreCategory.FourOfAKind, 15)]
    [InlineData(ScoreCategory.Chance, 15)]
    public void Joker_LowerBoxScores(ScoreCategory category, int expected)
    {
        var engine = GameWithYahtzeeScored(50);
        engine.Fill(0, ScoreCategory.Threes, 9);
        engine.SetRoll(3, 3, 3, 3, 3);
        Assert.Equal(expected, engine.TryApplyScore(0, category, Pen).Score);
    }

    [Fact]
    public void ExtraYahtzee_WhenNoLowerBoxIsOpen_ForcesAZeroInAnOpenUpperBox()
    {
        var engine = GameWithYahtzeeScored(50);
        foreach (var lower in ScoreRules.LowerCategories.Where(c => c != ScoreCategory.Yahtzee))
        {
            engine.Fill(0, lower, 10);
        }

        engine.Fill(0, ScoreCategory.Sixes, 18);
        engine.SetRoll(6, 6, 6, 6, 6);

        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.Aces));
        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.Fives));
        Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, ScoreCategory.Sixes));

        Assert.False(engine.TryApplyScore(0, ScoreCategory.Aces, Pen).Applied);
        var result = engine.TryScratch(0, ScoreCategory.Aces, Pen);

        Assert.True(result.Applied);
        Assert.Equal(0, result.Score);
        Assert.True(result.YahtzeeBonusAwarded);
        Assert.Equal(1, engine.Cards[0].YahtzeeBonusCount);
    }

    [Fact]
    public void ExtraYahtzee_AfterScratchedYahtzeeBox_GetsJokerPlacementButNoBonus()
    {
        var engine = GameWithYahtzeeScored(0, scratch: true);
        engine.SetRoll(2, 2, 2, 2, 2);
        Assert.True(engine.IsJokerRoll);
        Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, ScoreCategory.Twos));
        Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, ScoreCategory.Chance));

        var result = engine.TryApplyScore(0, ScoreCategory.Twos, Pen);

        Assert.True(result.Applied);
        Assert.False(result.YahtzeeBonusAwarded);
        Assert.Equal(0, engine.Cards[0].YahtzeeBonusCount);
    }

    [Fact]
    public void Bonus_IsAddedToLowerSubtotalAndGameTotal()
    {
        var card = new GameCardState { YahtzeeBonusCount = 2 };
        foreach (var category in ScoreRules.ScoringCategories)
        {
            card.Scores[category].Value = 0;
        }

        Assert.Equal(200, card.YahtzeeBonusTotal);
        Assert.Equal(200, card.LowerSubtotal);
        Assert.Equal(200, card.GameTotal);
    }

    [Fact]
    public void Undo_RestoresTheYahtzeeBonusCount()
    {
        var engine = GameWithYahtzeeScored(50);
        engine.SetRoll(4, 4, 4, 4, 4);
        engine.TryApplyScore(0, ScoreCategory.Fours, Pen);
        Assert.Equal(1, engine.Cards[0].YahtzeeBonusCount);

        Assert.True(engine.Undo());

        Assert.Equal(0, engine.Cards[0].YahtzeeBonusCount);
        Assert.False(engine.Cards[0].Scores[ScoreCategory.Fours].IsFilled);
    }

    [Fact]
    public void FullSheet_HasNoBonusAndNoJoker()
    {
        var engine = GameWithYahtzeeScored(50, mode: GameMode.FullSheet);
        engine.SetRoll(4, 4, 4, 4, 4);
        Assert.False(engine.IsJokerRoll);

        // Five of a kind is not a Full House in Full Sheet mode, so it can only be scratched there.
        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.FullHouse));
        Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, ScoreCategory.Chance));

        var result = engine.TryApplyScore(0, ScoreCategory.Chance, Pen);
        Assert.True(result.Applied);
        Assert.False(result.YahtzeeBonusAwarded);
        Assert.Equal(0, engine.Cards[0].YahtzeeBonusCount);
    }

    [Fact]
    public void ScoringAYahtzeeAnywhere_ReportsWasYahtzeeUnlessScratched()
    {
        var engine = NewGame();
        engine.SetRoll(1, 1, 1, 1, 1);
        Assert.True(engine.TryApplyScore(0, ScoreCategory.Aces, Pen).WasYahtzee);

        engine.SetRoll(1, 1, 1, 1, 1);
        Assert.False(engine.TryScratch(0, ScoreCategory.FullHouse, Pen).WasYahtzee);
    }
}
