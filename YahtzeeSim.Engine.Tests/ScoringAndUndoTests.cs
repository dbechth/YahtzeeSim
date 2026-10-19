using YahtzeeSim.Engine;
using static YahtzeeSim.Engine.Tests.TestHelpers;

namespace YahtzeeSim.Engine.Tests;

// SW-SCR-01 .. SW-SCR-08, SW-UND-01 .. SW-UND-03
public class ScoringAndUndoTests
{
    [Fact]
    public void Cell_DisplayText_BlankNumberOrX()
    {
        var empty = new ScoreCellState();
        var scored = new ScoreCellState { Value = 12 };
        var scratched = new ScoreCellState { Value = 0, IsScratch = true };
        Assert.Equal(string.Empty, empty.DisplayText);
        Assert.Equal("12", scored.DisplayText);
        Assert.Equal("X", scratched.DisplayText);
    }

    [Fact]
    public void FilledCell_RemembersPenColour()
    {
        var engine = NewGame();
        engine.SetRoll(1, 1, 1, 1, 2);
        engine.TryApplyScore(0, ScoreCategory.Aces, "#ABCDEF");
        Assert.Equal("#ABCDEF", engine.Cards[0].Scores[ScoreCategory.Aces].PenColorHex);
    }

    [Fact]
    public void Totals_AreUnavailableUntilCardIsComplete()
    {
        var engine = NewGame();
        engine.FillAllExcept(0, ScoreCategory.Chance);
        var card = engine.Cards[0];
        Assert.Null(card.UpperSubtotal);
        Assert.Null(card.LowerSubtotal);
        Assert.Null(card.UpperBonus);
        Assert.Null(card.GameTotal);
    }

    [Theory]
    [InlineData(63, 35)]
    [InlineData(64, 35)]
    [InlineData(62, 0)]
    public void UpperBonus_AwardedAtSixtyThree(int upperTotal, int expectedBonus)
    {
        var card = new GameCardState();
        foreach (var category in ScoreRules.ScoringCategories)
        {
            card.Scores[category].Value = 0;
        }

        card.Scores[ScoreCategory.Sixes].Value = upperTotal;
        Assert.Equal(upperTotal, card.UpperSubtotal);
        Assert.Equal(expectedBonus, card.UpperBonus);
    }

    [Fact]
    public void GameTotal_IsUpperPlusLowerPlusBonus()
    {
        var card = new GameCardState();
        foreach (var category in ScoreRules.ScoringCategories)
        {
            card.Scores[category].Value = 0;
        }

        // Upper: 3 of each face = 63 (bonus applies). Lower: 25 + 40 + 50 + 20.
        card.Scores[ScoreCategory.Aces].Value = 3;
        card.Scores[ScoreCategory.Twos].Value = 6;
        card.Scores[ScoreCategory.Threes].Value = 9;
        card.Scores[ScoreCategory.Fours].Value = 12;
        card.Scores[ScoreCategory.Fives].Value = 15;
        card.Scores[ScoreCategory.Sixes].Value = 18;
        card.Scores[ScoreCategory.FullHouse].Value = 25;
        card.Scores[ScoreCategory.LargeStraight].Value = 40;
        card.Scores[ScoreCategory.Yahtzee].Value = 50;
        card.Scores[ScoreCategory.Chance].Value = 20;

        Assert.Equal(63, card.UpperSubtotal);
        Assert.Equal(135, card.LowerSubtotal);
        Assert.Equal(35, card.UpperBonus);
        Assert.Equal(233, card.GameTotal);
    }

    [Fact]
    public void ScratchedCells_ContributeZero()
    {
        var card = new GameCardState();
        foreach (var category in ScoreRules.ScoringCategories)
        {
            card.Scores[category].Value = 0;
            card.Scores[category].IsScratch = true;
        }

        Assert.Equal(0, card.GameTotal);
    }

    [Fact]
    public void CardTotal_SumsGameTotals()
    {
        var engine = NewGame(GameMode.FullSheet);
        for (int i = 0; i < 6; i++)
        {
            engine.FillAllExcept(i, ScoreCategory.Chance);
            engine.Fill(i, ScoreCategory.Chance, 10 + i);
        }

        Assert.Equal(10 + 11 + 12 + 13 + 14 + 15, engine.Cards.Sum(c => c.GameTotal ?? 0));
        Assert.Equal(12, engine.GetCardTotal(2));
    }

    [Fact]
    public void UpperPace_IsCountMinusThreeTimesFace()
    {
        var engine = NewGame();
        var card = engine.Cards[0];
        engine.Fill(0, ScoreCategory.Fours, 16);   // four 4s: +4
        engine.Fill(0, ScoreCategory.Sixes, 12);   // two 6s: -6
        engine.Fill(0, ScoreCategory.Aces, 0, scratch: true); // scratch: -3
        engine.Fill(0, ScoreCategory.Threes, 9);   // on pace: 0

        Assert.Equal(4, ScoreRules.GetUpperPaceDelta(card, ScoreCategory.Fours));
        Assert.Equal(-6, ScoreRules.GetUpperPaceDelta(card, ScoreCategory.Sixes));
        Assert.Equal(-3, ScoreRules.GetUpperPaceDelta(card, ScoreCategory.Aces));
        Assert.Equal(0, ScoreRules.GetUpperPaceDelta(card, ScoreCategory.Threes));
        Assert.Equal(0, ScoreRules.GetUpperPaceDelta(card, ScoreCategory.Twos));
        Assert.Equal(0, ScoreRules.GetUpperPaceDelta(card, ScoreCategory.Chance));
        Assert.Equal(-5, ScoreRules.UpperCategories.Sum(c => ScoreRules.GetUpperPaceDelta(card, c)));
    }

    [Fact]
    public void UpperPace_IsTrackedForEveryGameInFullSheet()
    {
        var engine = NewGame(GameMode.FullSheet);
        engine.Fill(0, ScoreCategory.Fives, 20);
        engine.Fill(3, ScoreCategory.Fives, 5);

        Assert.Equal(5, ScoreRules.GetUpperPaceDelta(engine.Cards[0], ScoreCategory.Fives));
        Assert.Equal(-10, ScoreRules.GetUpperPaceDelta(engine.Cards[3], ScoreCategory.Fives));
    }

    [Fact]
    public void Undo_IsUnavailableUntilAScoreIsApplied()
    {
        var engine = NewGame();
        engine.SetRoll(1, 2, 3, 4, 5);
        Assert.False(engine.CanUndo);
        Assert.False(engine.Undo());
    }

    [Fact]
    public void Undo_RestoresScoreDiceHoldsAndRollCount()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.ToggleHold(1);
        engine.TryApplyScore(0, ScoreCategory.Threes, Pen);
        Assert.True(engine.CanUndo);

        Assert.True(engine.Undo());

        Assert.False(engine.Cards[0].Scores[ScoreCategory.Threes].IsFilled);
        Assert.Equal(1, engine.RollCount);
        Assert.Equal([3, 3, 3, 1, 2], engine.Dice.Select(d => d.Value).ToArray());
        Assert.True(engine.Dice[0].IsHeld);
        Assert.False(engine.CanUndo);
    }

    [Fact]
    public void Undo_SupportsOnlyOneLevel()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.TryApplyScore(0, ScoreCategory.Threes, Pen);
        engine.SetRoll(1, 1, 5, 5, 5);
        engine.TryApplyScore(0, ScoreCategory.Chance, Pen);

        Assert.True(engine.Undo());
        Assert.False(engine.Cards[0].Scores[ScoreCategory.Chance].IsFilled);
        Assert.True(engine.Cards[0].Scores[ScoreCategory.Threes].IsFilled);
        Assert.False(engine.Undo());
    }

    [Fact]
    public void Undo_RestoresScratch()
    {
        var engine = NewGame();
        engine.SetRoll(1, 2, 3, 4, 6);
        engine.TryScratch(0, ScoreCategory.Yahtzee, Pen);
        Assert.True(engine.Undo());
        Assert.False(engine.Cards[0].Scores[ScoreCategory.Yahtzee].IsFilled);
    }

    [Fact]
    public void Undo_SnapshotIsAnIndependentCopy()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.TryApplyScore(0, ScoreCategory.Threes, Pen);
        engine.Cards[0].Scores[ScoreCategory.Chance].Value = 99;

        engine.Undo();

        Assert.Null(engine.Cards[0].Scores[ScoreCategory.Chance].Value);
    }

    [Fact]
    public void NewGame_ClearsUndo()
    {
        var engine = NewGame();
        engine.SetRoll(3, 3, 3, 1, 2);
        engine.TryApplyScore(0, ScoreCategory.Threes, Pen);
        engine.Reset(GameMode.FullSheet);
        Assert.False(engine.CanUndo);
        Assert.Equal(GameMode.FullSheet, engine.Mode);
    }

    [Fact]
    public void RollsCannotBeUndone()
    {
        var engine = NewGame();
        engine.RollDice();
        Assert.False(engine.Undo());
        Assert.Equal(1, engine.RollCount);
    }

    [Fact]
    public void TurnNumber_CountsBoxesFilledOnAnyCardAndFollowsUndo()
    {
        var engine = NewGame(GameMode.FullSheet);
        Assert.Equal(1, engine.TurnNumber);

        engine.SetRoll(1, 1, 1, 1, 2);
        engine.TryApplyScore(3, ScoreCategory.Aces, Pen);
        Assert.Equal(2, engine.TurnNumber);

        engine.SetRoll(2, 2, 2, 5, 6);
        engine.TryApplyScore(5, ScoreCategory.Twos, Pen);
        Assert.Equal(3, engine.TurnNumber);

        Assert.True(engine.Undo());
        Assert.Equal(2, engine.TurnNumber);

        engine.TryApplyScore(5, ScoreCategory.Twos, Pen);
        Assert.Equal(3, engine.TurnNumber);
    }

    [Fact]
    public void TurnNumber_IsNullWhenTheGameIsComplete()
    {
        var engine = NewGame();
        engine.FillAllExcept(0, ScoreCategory.Chance);
        Assert.Equal(13, engine.TurnNumber);
        engine.SetRoll(1, 2, 3, 4, 6);
        engine.TryApplyScore(0, ScoreCategory.Chance, Pen);
        Assert.Null(engine.TurnNumber);
    }
}