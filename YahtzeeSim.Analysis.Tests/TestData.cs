using System.Text;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Solver;

namespace YahtzeeSim.Analysis.Tests;

/// <summary>Shared, computed-once solver and helpers for building synthetic games and exports.</summary>
internal static class TestData
{
    private static readonly Lazy<CardSolver> SharedSolver = new(() => CardSolver.Compute(DiceTables.Shared));

    public static CardSolver Solver => SharedSolver.Value;

    public static SheetEvaluator Evaluator => new(Solver, DiceTables.Shared);

    /// <summary>A complete Full Sheet game played by a bot, as action rows plus its score row.</summary>
    public static (ScoreRow Score, List<ActionRow> Actions) BotGame(
        PlacementScoring scoring,
        int seed,
        string nickname = "Bot",
        string userId = "bot",
        string? gameId = null,
        long playedAt = 1_800_000_000_000)
    {
        gameId ??= $"game-{scoring}-{seed}";
        var game = new BotSimulator(Evaluator).Play(scoring, new Random(seed), gameId: gameId);
        var actions = game.Actions!.Select(a => a with { Nickname = nickname, UserId = userId }).ToList();
        var score = new ScoreRow(seed, nickname, userId, gameId, "FullSheet", game.Total, playedAt);
        return (score, actions);
    }

    /// <summary>Renumbers seq (and id) from 1 so edited logs stay contiguous.</summary>
    public static List<ActionRow> Renumber(IEnumerable<ActionRow> rows) =>
        [.. rows.Select((a, i) => a with { Seq = i + 1, Id = i + 1 })];

    /// <summary>Writes users.csv, scores.csv and actions.csv in the same shape as scripts/export-game-data.ps1.</summary>
    public static void WriteExport(string directory, IEnumerable<ScoreRow> scores, IEnumerable<ActionRow> actions)
    {
        Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(false);
        var scoreList = scores.ToList();
        var users = new StringBuilder("id,nickname,created_at\n");
        foreach (var u in scoreList.GroupBy(s => s.UserId))
        {
            users.Append(u.Key).Append(',').Append(Csv.Escape(u.First().Nickname)).Append(",1\n");
        }

        var s = new StringBuilder("id,nickname,user_id,client_game_id,mode,total,played_at\n");
        foreach (var r in scoreList)
        {
            s.Append($"{r.Id},{Csv.Escape(r.Nickname)},{r.UserId},{r.GameId},{r.Mode},{r.Total},{r.PlayedAt}\n");
        }

        var a = new StringBuilder("id,nickname,user_id,client_game_id,seq,mode,action,occurred_at,received_at,turn,roll_number,dice,die_id,game_index,category,points,yahtzee_bonus\n");
        long id = 1;
        foreach (var r in actions)
        {
            a.Append($"{id++},{Csv.Escape(r.Nickname)},{r.UserId},{r.GameId},{r.Seq},{r.Mode},{r.Action},{r.OccurredAt},{r.OccurredAt},{r.Turn},{r.RollNumber},{r.Dice},{r.DieId},{r.GameIndex},{r.Category},{r.Points},{(r.YahtzeeBonus ? 1 : 0)}\n");
        }

        File.WriteAllText(Path.Combine(directory, "users.csv"), users.ToString(), utf8);
        File.WriteAllText(Path.Combine(directory, "scores.csv"), s.ToString(), utf8);
        File.WriteAllText(Path.Combine(directory, "actions.csv"), a.ToString(), utf8);
    }

    public static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "yahtzeesim-analysis-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
