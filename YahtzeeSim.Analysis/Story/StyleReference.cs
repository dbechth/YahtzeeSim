using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Story;

/// <summary>Style metrics of the reference strategy itself, from seeded simulated games, for calibrating badge thresholds.</summary>
public static class StyleReference
{
    public static StyleMetrics Simulate(SheetEvaluator evaluator, int games, int seed)
    {
        var simulator = new BotSimulator(evaluator);
        var replayed = new ReplayedGame[games];
        for (int i = 0; i < games; i++)
        {
            string id = "reference-" + i;
            var game = simulator.Play(SheetEvaluator.ReferenceScoring, new Random(unchecked(seed + i)), gameId: id);
            replayed[i] = GameReplayer.Replay(new ScoreRow(i, "Reference", "reference", id, "FullSheet", game.Total, 0), game.Actions!);
        }

        return StyleMetrics.From(replayed);
    }
}
