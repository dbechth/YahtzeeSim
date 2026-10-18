using YahtzeeSim.Engine;

namespace YahtzeeSim.Engine.Tests;

internal static class TestHelpers
{
    public const string Pen = "#123456";

    public static YahtzeeGameEngine NewGame(GameMode mode = GameMode.Single, int seed = 1)
    {
        var engine = new YahtzeeGameEngine(new Random(seed));
        engine.Reset(mode);
        return engine;
    }

    /// <summary>Rolls once, then overrides the dice so tests can score exact hands.</summary>
    public static void SetRoll(this YahtzeeGameEngine engine, params int[] dice)
    {
        if (engine.RollCount == 0)
        {
            Assert.True(engine.RollDice());
        }

        for (int i = 0; i < dice.Length; i++)
        {
            engine.Dice[i].Value = dice[i];
        }
    }

    public static void Fill(this YahtzeeGameEngine engine, int game, ScoreCategory category, int value, bool scratch = false)
    {
        var cell = engine.Cards[game].Scores[category];
        cell.Value = value;
        cell.IsScratch = scratch;
    }

    public static void FillAllExcept(this YahtzeeGameEngine engine, int game, params ScoreCategory[] open)
    {
        foreach (var category in ScoreRules.ScoringCategories)
        {
            if (!open.Contains(category))
            {
                engine.Fill(game, category, 0);
            }
        }
    }
}
