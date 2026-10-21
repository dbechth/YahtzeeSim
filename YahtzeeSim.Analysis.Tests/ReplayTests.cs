using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Tests;

public class ReplayTests
{
    [Fact]
    public void BotGameReplaysToItsTotal()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 1);
        var game = GameReplayer.Replay(score, actions);
        Assert.Equal(78, game.Turns.Count);
        Assert.Equal(score.Total, game.Total);
        Assert.Equal(Enumerable.Range(1, 78), game.Turns.Select(t => t.Turn));
        Assert.All(game.Turns, t => Assert.NotNull(t.Box));
    }

    [Fact]
    public void ActionOrderInTheFileDoesNotMatter()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 2);
        var shuffled = actions.OrderByDescending(a => a.Seq).ToList();
        Assert.Equal(score.Total, GameReplayer.Replay(score, shuffled).Total);
    }

    [Fact]
    public void KeptDiceAreRebuiltFromHoldPresses()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 3);
        var game = GameReplayer.Replay(score, actions);
        foreach (var turn in game.Turns)
        {
            Assert.All(turn.Rolls[0].Kept, Assert.False);
            for (int i = 1; i < turn.Rolls.Count; i++)
            {
                for (int d = 0; d < 5; d++)
                {
                    if (turn.Rolls[i].Kept[d])
                    {
                        Assert.Equal(turn.Rolls[i - 1].Dice[d], turn.Rolls[i].Dice[d]);
                    }
                }
            }
        }
    }

    [Fact]
    public void UndoThenSameScoreKeepsTheTotal()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 4);
        int index = actions.FindIndex(a => a.Action is "Score" or "Scratch" && a.Turn == 10);
        var scored = actions[index];
        var edited = actions.Take(index + 1)
            .Append(scored with { Action = "Undo", GameIndex = null, Category = null, Points = null })
            .Append(scored)
            .Concat(actions.Skip(index + 1));
        var game = GameReplayer.Replay(score, TestData.Renumber(edited));
        Assert.Equal(score.Total, game.Total);
        Assert.Equal(1, game.UndoCount);
        Assert.Equal(0, game.UndoChangedBox);
        Assert.Equal(0, game.UndoAfterNextRoll);
    }

    [Fact]
    public void UndoAfterTheNextRollDiscardsThatRoll()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 5);
        int index = actions.FindIndex(a => a.Action is "Score" or "Scratch" && a.Turn == 20);
        var scored = actions[index];
        var nextRoll = actions[index + 1];
        Assert.Equal("Roll", nextRoll.Action);
        var thrownAway = nextRoll with { Dice = nextRoll.Dice == "11111" ? "22222" : "11111" };
        var edited = actions.Take(index + 1)
            .Append(thrownAway)
            .Append(scored with { Action = "Undo", GameIndex = null, Category = null, Points = null })
            .Append(scored)
            .Concat(actions.Skip(index + 1));
        var game = GameReplayer.Replay(score, TestData.Renumber(edited));
        Assert.Equal(score.Total, game.Total);
        Assert.Equal(1, game.UndoAfterNextRoll);
        Assert.Equal(nextRoll.Dice, string.Concat(game.Turns[20].Rolls[0].Dice));
    }

    [Fact]
    public void PressingTheSameDieAgainCountsAsAChangeOfMind()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 6);
        int index = actions.FindIndex(a => a.Action == "Hold");
        var hold = actions[index];
        var edited = actions.Take(index + 1)
            .Append(hold with { Action = "Unhold" })
            .Append(hold)
            .Concat(actions.Skip(index + 1));
        var game = GameReplayer.Replay(score, TestData.Renumber(edited));
        Assert.Equal(score.Total, game.Total);
        Assert.Equal(2, game.Turns.Sum(t => t.Rolls.Sum(r => r.ChangesOfMind)));
    }

    [Fact]
    public void ChangedHeldDieIsRejected()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 7);
        int hold = actions.FindIndex(a => a.Action == "Hold");
        int nextRoll = actions.FindIndex(hold, a => a.Action == "Roll");
        int die = actions[hold].DieId!.Value - 1;
        var chars = actions[nextRoll].Dice.ToCharArray();
        chars[die] = chars[die] == '6' ? '1' : (char)(chars[die] + 1);
        actions[nextRoll] = actions[nextRoll] with { Dice = new string(chars) };
        var ex = Assert.Throws<ReplayException>(() => GameReplayer.Replay(score, actions));
        Assert.Contains("held die", ex.Message);
    }

    [Fact]
    public void WrongTotalIsRejected()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 8);
        var ex = Assert.Throws<ReplayException>(() => GameReplayer.Replay(score with { Total = score.Total + 1 }, actions));
        Assert.Contains("does not match the submitted total", ex.Message);
    }

    [Fact]
    public void MissingActionIsRejected()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 9);
        actions.RemoveAt(40);
        var ex = Assert.Throws<ReplayException>(() => GameReplayer.Replay(score, actions));
        Assert.Contains("missing actions", ex.Message);
    }

    [Fact]
    public void UnfinishedGameIsRejected()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 10);
        int lastScore = actions.FindLastIndex(a => a.Action is "Score" or "Scratch");
        var ex = Assert.Throws<ReplayException>(() => GameReplayer.Replay(score, actions.Take(lastScore)));
        Assert.Contains("77 boxes filled", ex.Message);
    }

    [Fact]
    public void ScoreThatDoesNotMatchTheDiceIsRejected()
    {
        var (score, actions) = TestData.BotGame(PlacementScoring.CardValues, 12);
        int index = actions.FindIndex(a => a.Action == "Score");
        actions[index] = actions[index] with { Points = actions[index].Points + 1 };
        Assert.Throws<ReplayException>(() => GameReplayer.Replay(score, actions));
    }

    [Fact]
    public void CsvParserHandlesQuotesAndLineEndings()
    {
        var rows = Csv.Parse("a,b,c\r\n\"x, y\",\"say \"\"hi\"\"\",\n1,2,3");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["x, y", "say \"hi\"", ""], rows[1]);
        Assert.Equal(["1", "2", "3"], rows[2]);
    }
}
