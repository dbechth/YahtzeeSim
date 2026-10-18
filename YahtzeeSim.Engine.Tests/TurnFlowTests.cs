using YahtzeeSim.Engine;
using static YahtzeeSim.Engine.Tests.TestHelpers;

namespace YahtzeeSim.Engine.Tests;

// SW-RUL-01, SW-RUL-09, SW-RUL-10, SW-TRN-01 .. SW-TRN-11, SW-MOD-04, SW-MOD-05
public class TurnFlowTests
{
    [Fact]
    public void NewGame_StartsBlank()
    {
        var engine = NewGame();
        Assert.Equal(0, engine.RollCount);
        Assert.Equal(5, engine.Dice.Count);
        Assert.All(engine.Dice, d => { Assert.Equal(0, d.Value); Assert.False(d.IsHeld); });
        Assert.Equal(6, engine.Cards.Count);
        Assert.False(engine.CanUndo);
    }

    [Fact]
    public void Roll_ProducesValuesBetweenOneAndSix()
    {
        var engine = NewGame(seed: 42);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(engine.RollDice());
            Assert.All(engine.Dice, d => Assert.InRange(d.Value, 1, 6));
        }
    }

    [Fact]
    public void Roll_IsLimitedToThreePerTurn()
    {
        var engine = NewGame();
        Assert.True(engine.RollDice());
        Assert.True(engine.RollDice());
        Assert.True(engine.RollDice());
        Assert.False(engine.CanRoll);
        Assert.False(engine.RollDice());
        Assert.Equal(3, engine.RollCount);
    }

    [Fact]
    public void Roll_OnlyRerollsDiceThatAreNotHeld()
    {
        var engine = NewGame(seed: 7);
        engine.RollDice();
        engine.SetRoll(1, 2, 3, 4, 5);
        Assert.True(engine.ToggleHold(1));
        Assert.True(engine.ToggleHold(3));

        for (int i = 0; i < 20 && engine.RollCount < 2; i++)
        {
            engine.RollDice();
        }

        Assert.Equal(1, engine.Dice[0].Value);
        Assert.Equal(3, engine.Dice[2].Value);
        Assert.Equal(2, engine.RollCount);
    }

    [Fact]
    public void Hold_NotAllowedBeforeFirstRoll()
    {
        var engine = NewGame();
        Assert.False(engine.ToggleHold(1));
    }

    [Fact]
    public void Hold_TogglesAfterFirstRollAndNotUnknownDice()
    {
        var engine = NewGame();
        engine.RollDice();
        Assert.True(engine.ToggleHold(2));
        Assert.True(engine.Dice[1].IsHeld);
        Assert.True(engine.ToggleHold(2));
        Assert.False(engine.Dice[1].IsHeld);
        Assert.False(engine.ToggleHold(99));
    }

    [Fact]
    public void ThirdRoll_HoldsAllDiceAndDisablesHolding()
    {
        var engine = NewGame();
        engine.RollDice();
        engine.RollDice();
        engine.RollDice();
        Assert.True(engine.AwaitingScoreSelection);
        Assert.All(engine.Dice, d => Assert.True(d.IsHeld));
        Assert.False(engine.ToggleHold(1));
    }

    [Fact]
    public void ScoreCells_AreUnavailableBeforeFirstRoll()
    {
        var engine = NewGame();
        Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, ScoreCategory.Chance));
        Assert.False(engine.TryApplyScore(0, ScoreCategory.Chance, Pen).Applied);
    }

    [Fact]
    public void ValidPick_ScoresAndEndsTurnEarly()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        Assert.Equal(PickStatus.Valid, engine.GetPickStatus(0, ScoreCategory.Threes));

        var result = engine.TryApplyScore(0, ScoreCategory.Threes, Pen);

        Assert.True(result.Applied);
        Assert.False(result.WasScratch);
        Assert.Equal(9, result.Score);
        Assert.Equal(9, engine.Cards[0].Scores[ScoreCategory.Threes].Value);
        Assert.Equal(0, engine.RollCount);
        Assert.All(engine.Dice, d => { Assert.Equal(0, d.Value); Assert.False(d.IsHeld); });
    }

    [Fact]
    public void InvalidPick_IsOfferedAsScratchAndNotScoredByTryApplyScore()
    {
        var engine = NewGame();
        engine.SetRoll(1, 2, 3, 4, 6);
        Assert.Equal(PickStatus.Scratch, engine.GetPickStatus(0, ScoreCategory.Yahtzee));

        var result = engine.TryApplyScore(0, ScoreCategory.Yahtzee, Pen);

        Assert.False(result.Applied);
        Assert.False(engine.Cards[0].Scores[ScoreCategory.Yahtzee].IsFilled);
        Assert.Equal(1, engine.RollCount);
        Assert.False(engine.CanUndo);
    }

    [Fact]
    public void Scratch_IsAllowedAfterFirstRollRecordsXAndEndsTurn()
    {
        var engine = NewGame();
        engine.SetRoll(1, 2, 3, 4, 6);

        var result = engine.TryScratch(0, ScoreCategory.Yahtzee, Pen);

        Assert.True(result.Applied);
        Assert.True(result.WasScratch);
        var cell = engine.Cards[0].Scores[ScoreCategory.Yahtzee];
        Assert.Equal(0, cell.Value);
        Assert.True(cell.IsScratch);
        Assert.Equal("X", cell.DisplayText);
        Assert.Equal(0, engine.RollCount);
    }

    [Fact]
    public void Scratch_IsNotAllowedForACellThatIsValid()
    {
        var engine = NewGame();
        engine.SetRoll(1, 2, 3, 4, 6);

        var result = engine.TryScratch(0, ScoreCategory.Chance, Pen);

        Assert.False(result.Applied);
        Assert.False(engine.Cards[0].Scores[ScoreCategory.Chance].IsFilled);
        Assert.Equal(1, engine.RollCount);
    }

    [Fact]
    public void Scratch_WorksAfterThirdRollToo()
    {
        var engine = NewGame();
        engine.RollDice();
        engine.RollDice();
        engine.RollDice();
        engine.SetRoll(1, 2, 3, 4, 6);

        Assert.True(engine.TryScratch(0, ScoreCategory.Yahtzee, Pen).Applied);
    }

    [Fact]
    public void FilledCell_CannotBeFilledAgain()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.TryApplyScore(0, ScoreCategory.Threes, Pen);
        engine.SetRoll(3, 3, 3, 3, 2);

        Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(0, ScoreCategory.Threes));
        Assert.False(engine.TryApplyScore(0, ScoreCategory.Threes, Pen).Applied);
        Assert.False(engine.TryScratch(0, ScoreCategory.Threes, Pen).Applied);
        Assert.Equal(9, engine.Cards[0].Scores[ScoreCategory.Threes].Value);
    }

    [Fact]
    public void Card_IsCompleteWhenAllThirteenCellsAreFilled()
    {
        var engine = NewGame();
        engine.FillAllExcept(0, ScoreCategory.Chance);
        Assert.False(engine.Cards[0].IsComplete);
        engine.Fill(0, ScoreCategory.Chance, 20);
        Assert.True(engine.Cards[0].IsComplete);
    }

    [Fact]
    public void RollIsDisabledWhenGameIsComplete()
    {
        var engine = NewGame();
        engine.FillAllExcept(0, ScoreCategory.Chance);
        engine.SetRoll(1, 2, 3, 4, 5);
        Assert.True(engine.TryApplyScore(0, ScoreCategory.Chance, Pen).Applied);

        Assert.True(engine.IsGameComplete);
        Assert.False(engine.CanRoll);
        Assert.False(engine.RollDice());
    }

    [Fact]
    public void SingleMode_EnablesOnlyFirstCard()
    {
        var engine = NewGame(GameMode.Single);
        Assert.True(engine.IsCardEnabled(0));
        for (int i = 1; i < 6; i++)
        {
            Assert.False(engine.IsCardEnabled(i));
        }

        engine.SetRoll(1, 2, 3, 4, 5);
        Assert.Equal(PickStatus.Unavailable, engine.GetPickStatus(1, ScoreCategory.Chance));
    }

    [Fact]
    public void SingleMode_CompletesWhenFirstCardIsComplete()
    {
        var engine = NewGame(GameMode.Single);
        engine.FillAllExcept(0);
        Assert.True(engine.IsGameComplete);
    }

    [Fact]
    public void FullSheet_EnablesAllCardsAndAllowsAnyOrder()
    {
        var engine = NewGame(GameMode.FullSheet);
        for (int i = 0; i < 6; i++)
        {
            Assert.True(engine.IsCardEnabled(i));
        }

        engine.SetRoll(1, 2, 3, 4, 5);
        Assert.True(engine.TryApplyScore(4, ScoreCategory.LargeStraight, Pen).Applied);
        Assert.Equal(40, engine.Cards[4].Scores[ScoreCategory.LargeStraight].Value);
        Assert.False(engine.Cards[0].Scores[ScoreCategory.LargeStraight].IsFilled);
    }

    [Fact]
    public void FullSheet_CompletesOnlyWhenAllSixCardsAreComplete()
    {
        var engine = NewGame(GameMode.FullSheet);
        for (int i = 0; i < 5; i++)
        {
            engine.FillAllExcept(i);
        }

        Assert.False(engine.IsGameComplete);
        engine.FillAllExcept(5);
        Assert.True(engine.IsGameComplete);
    }
}
