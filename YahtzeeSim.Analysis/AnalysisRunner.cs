using System.Text;
using YahtzeeSim.Analysis.Data;
using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Profile;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Analysis.Rules;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis;

public sealed record AnalysisOptions
{
    public const int DefaultSimulations = 1000;
    public const int DefaultSeed = 20261020;

    /// <summary>Number of simulated games per baseline strategy.</summary>
    public int Simulations { get; init; } = DefaultSimulations;

    /// <summary>Seed for simulated dice. The same seed always gives the same report.</summary>
    public int Seed { get; init; } = DefaultSeed;

    /// <summary>File used to cache the single-card solver table, or null to always compute it.</summary>
    public string? SolverCachePath { get; init; }
}

/// <summary>A game in the export that was not analysed, and why.</summary>
public sealed record SkippedGame(string GameId, string Nickname, string Mode, string Reason);

/// <summary>Everything known about one player's Full Sheet strategy.</summary>
public sealed record PlayerAnalysis(
    string UserId,
    string Nickname,
    string Slug,
    IReadOnlyList<GradedGame> Games,
    StrategyProfile Profile,
    IReadOnlyList<RuleFinding> Findings)
{
    public IEnumerable<GradedDecision> Decisions => Games.SelectMany(g => g.Decisions);

    public double AverageTotal => Games.Average(g => g.Game.Total);

    public double AverageLuckFree => Games.Average(g => g.Game.Total - g.Luck);

    public double AverageCost => Games.Average(g => g.Cost);

    public double MatchRate
    {
        get
        {
            var all = Decisions.ToList();
            return all.Count == 0 ? 0 : (double)all.Count(d => d.MatchesReference) / all.Count;
        }
    }
}

public sealed record AnalysisResult(
    IReadOnlyList<PlayerAnalysis> Players,
    IReadOnlyList<SkippedGame> Skipped,
    ReferenceData ReferenceData,
    double CardStartValue)
{
    public SimulationSummary Reference => ReferenceData.Reference;

    public SimulationSummary Greedy => ReferenceData.Greedy;

    public string RulesVersion => StrategyRules.Version;

    public int GameCount => Players.Sum(p => p.Games.Count);

    public long? FirstPlayedAt => Players.SelectMany(p => p.Games).Select(g => (long?)g.Game.PlayedAt).Min();

    public long? LastPlayedAt => Players.SelectMany(p => p.Games).Select(g => (long?)g.Game.PlayedAt).Max();
}

/// <summary>
/// Runs the whole analysis: replay finished Full Sheet games, grade every decision against the reference strategy,
/// simulate the reference and greedy strategies, build each player's profile and apply the rule table.
/// Deterministic: the same export and options always give the same result.
/// </summary>
public static class AnalysisRunner
{
    /// <param name="solver">Precomputed single-card table (the app loads it from a file); computed or cached when null.</param>
    /// <param name="referenceData">Precomputed simulations (the app loads them from a file); simulated with the options when null.</param>
    public static AnalysisResult Run(
        ExportData data,
        AnalysisOptions options,
        Action<string>? log = null,
        CardSolver? solver = null,
        ReferenceData? referenceData = null)
    {
        var tables = DiceTables.Shared;
        solver ??= CardSolver.LoadOrCompute(tables, options.SolverCachePath, log);
        var evaluator = new SheetEvaluator(solver, tables);

        var skipped = new List<SkippedGame>();
        var actionsByGame = data.Actions
            .GroupBy(a => (a.UserId, a.GameId))
            .ToDictionary(g => g.Key, g => g.ToList());
        var scores = data.Scores
            .GroupBy(s => (s.UserId, s.GameId))
            .Select(g => g.OrderBy(s => s.Id).First())
            .OrderBy(s => s.PlayedAt)
            .ThenBy(s => s.GameId, StringComparer.Ordinal)
            .ToList();
        var scoredKeys = scores.Select(s => (s.UserId, s.GameId)).ToHashSet();

        var toReplay = new List<(ScoreRow Score, List<ActionRow> Actions)>();
        foreach (var score in scores)
        {
            if (score.Mode != nameof(GameMode.FullSheet))
            {
                skipped.Add(new SkippedGame(score.GameId, score.Nickname, score.Mode, "Single mode is not analysed yet."));
            }
            else if (!actionsByGame.TryGetValue((score.UserId, score.GameId), out var actions))
            {
                skipped.Add(new SkippedGame(score.GameId, score.Nickname, score.Mode, "Finished, but no actions were recorded for it."));
            }
            else
            {
                toReplay.Add((score, actions));
            }
        }

        foreach (var (key, actions) in actionsByGame.Where(kv => !scoredKeys.Contains(kv.Key)))
        {
            skipped.Add(new SkippedGame(key.GameId, actions[0].Nickname, actions[0].Mode, "Not finished (no submitted score)."));
        }

        var replayed = new ReplayedGame?[toReplay.Count];
        for (int i = 0; i < toReplay.Count; i++)
        {
            try
            {
                replayed[i] = GameReplayer.Replay(toReplay[i].Score, toReplay[i].Actions);
            }
            catch (ReplayException ex)
            {
                var s = toReplay[i].Score;
                skipped.Add(new SkippedGame(s.GameId, s.Nickname, s.Mode, "Replay check failed: " + ex.Message));
            }
        }

        var games = replayed.OfType<ReplayedGame>().ToList();
        log?.Invoke($"Replayed {games.Count} finished Full Sheet games; skipped {skipped.Count}.");

        var graded = new GradedGame[games.Count];
        if (OperatingSystem.IsBrowser())
        {
            // The browser runs on one thread, where Parallel.For cannot wait for its workers.
            for (int i = 0; i < games.Count; i++)
            {
                graded[i] = new DecisionGrader(evaluator).Grade(games[i]);
            }
        }
        else
        {
            Parallel.For(0, games.Count, i => graded[i] = new DecisionGrader(evaluator).Grade(games[i]));
        }

        if (referenceData == null)
        {
            referenceData = ReferenceData.Simulate(evaluator, options.Simulations, options.Seed);
            log?.Invoke($"Simulated {options.Simulations} games per strategy: reference {referenceData.Reference.Mean:0.0}, greedy {referenceData.Greedy.Mean:0.0}.");
        }

        var reference = referenceData.Reference;

        var referencePoints = ScoreRules.ScoringCategories.ToDictionary(c => c, c => reference.AverageCategoryPoints[(int)c]);
        var players = graded
            .GroupBy(g => g.Game.UserId)
            .Select(group =>
            {
                var playerGames = group.OrderBy(g => g.Game.PlayedAt).ThenBy(g => g.Game.GameId, StringComparer.Ordinal).ToList();
                var profile = StrategyProfile.Build(playerGames, referencePoints);
                var findings = StrategyRules.Evaluate(new RuleContext([.. playerGames.SelectMany(g => g.Decisions)], profile, reference));
                return (UserId: group.Key, Nickname: playerGames[^1].Game.Nickname, Games: playerGames, Profile: profile, Findings: findings);
            })
            .OrderBy(p => p.Nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.UserId, StringComparer.Ordinal)
            .ToList();

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var analyses = players.Select(p =>
        {
            string slug = Slugify(p.Nickname);
            if (!slugs.Add(slug))
            {
                string id = Slugify(p.UserId);
                slug = $"{slug}-{id[..Math.Min(8, id.Length)]}";
                slugs.Add(slug);
            }

            return new PlayerAnalysis(p.UserId, p.Nickname, slug, p.Games, p.Profile, p.Findings);
        }).ToList();

        skipped = [.. skipped.OrderBy(s => s.Nickname, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.GameId, StringComparer.Ordinal)];
        return new AnalysisResult(analyses, skipped, referenceData, solver.StartValue);
    }

    public static string Slugify(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        string slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "player" : slug;
    }
}
