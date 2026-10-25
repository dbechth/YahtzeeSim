using System.Globalization;
using YahtzeeSim.Analysis.Replay;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Story;

/// <summary>
/// Play-style measurements from replayed games only (no solver), so the app can work out a badge cheaply.
/// Each value is a plain count or average of what happened in the games.
/// </summary>
public sealed record StyleMetrics(
    int Games,
    int Cards,
    int Turns,
    int ChoiceRolls,
    int KeptMatching,
    int KeptStraight,
    double ReferenceMatching,
    double ReferenceStraight,
    int UpperBonuses,
    double CardsInProgress,
    int EarlyStops,
    double MedianTurnSeconds,
    int YahtzeesScored)
{
    /// <summary>
    /// How much more often (in share of choice rolls) the player kept matching dice than the reference strategy does
    /// in the same situations. Positive = leans to sets.
    /// </summary>
    public double MatchingLean => ChoiceRolls == 0 ? 0 : (KeptMatching - ReferenceMatching) / ChoiceRolls;

    /// <summary>How much more often the player kept the straight draw than the reference strategy in the same situations.</summary>
    public double StraightLean => ChoiceRolls == 0 ? 0 : (KeptStraight - ReferenceStraight) / ChoiceRolls;

    public double UpperBonusRate => Cards == 0 ? 0 : (double)UpperBonuses / Cards;

    /// <summary>Share of turns scored before the third roll.</summary>
    public double EarlyStopRate => Turns == 0 ? 0 : (double)EarlyStops / Turns;

    /// <summary>
    /// Rolls that offer both a set and a straight: four in a row, or a pair with three in a row. With these the
    /// keep shows a preference.
    /// </summary>
    public static bool IsChoiceRoll(RollSituation situation) =>
        situation is RollSituation.FourInARow or RollSituation.PairAndRun;

    public static StyleMetrics From(IReadOnlyList<ReplayedGame> games)
    {
        var choices = KeepMoments.From(games).Where(m => IsChoiceRoll(m.Situation) && m.Response != KeepResponse.Stopped).ToList();
        var turns = games.SelectMany(g => g.Turns).ToList();
        int cards = games.Count * YahtzeeGameEngine.GameCount;
        var seconds = turns.Select(t => (t.Box!.At - t.Rolls[0].At) / 1000.0).Where(s => s >= 0);
        return new StyleMetrics(
            games.Count,
            cards,
            turns.Count,
            choices.Count,
            choices.Count(m => m.Response == KeepResponse.KeptMatching),
            choices.Count(m => m.Response == KeepResponse.KeptStraight),
            choices.Sum(m => BadgeRules.ReferenceMatchingRate(m.Situation)),
            choices.Sum(m => BadgeRules.ReferenceStraightRate(m.Situation)),
            games.Sum(g => Enumerable.Range(0, YahtzeeGameEngine.GameCount).Count(g.HasUpperBonus)),
            turns.Count == 0 ? 0 : turns.Average(t => t.BoardBefore.Count(p => p.Mask != 0 && !p.IsComplete)),
            turns.Count(t => t.Rolls.Count < YahtzeeGameEngine.RollsPerTurn),
            Profile.StrategyProfile.Median(seconds),
            turns.Count(t => t.Box!.Category == ScoreCategory.Yahtzee && !t.Box.Scratch));
    }
}

/// <summary>
/// A play-style badge. It qualifies when <see cref="Metric"/> passes <see cref="Threshold"/> in the given direction with
/// at least <see cref="MinOpportunities"/> chances; its strength is how far past the threshold it is, in units of
/// <see cref="Scale"/>.
/// </summary>
public sealed record Badge(
    string Id,
    string Name,
    string Description,
    string MetricName,
    Func<StyleMetrics, double> Metric,
    Func<StyleMetrics, int> Opportunities,
    int MinOpportunities,
    bool HigherIsStyle,
    double Threshold,
    double Scale,
    Func<double, string> Format)
{
    /// <summary>True for badges measured in seconds; simulated games have no real timing.</summary>
    public bool Timed { get; init; }

    public double Strength(StyleMetrics m)
    {
        if (Opportunities(m) < MinOpportunities)
        {
            return double.NegativeInfinity;
        }

        double past = HigherIsStyle ? Metric(m) - Threshold : Threshold - Metric(m);
        return past / Scale;
    }

    public string Rule => $"{MetricName} {(HigherIsStyle ? "at least" : "at most")} {Format(Threshold)}, with at least {MinOpportunities} chances.";
}

/// <summary>One badge's evaluation for a player, for the research view.</summary>
public sealed record BadgeScore(Badge Badge, double Value, int Opportunities, double Strength, bool Qualifies);

public sealed record BadgeResult(Badge Badge, IReadOnlyList<BadgeScore> Scores);

/// <summary>
/// The versioned badge table. A player gets the qualifying badge with the greatest strength; ties go to the earlier
/// badge in the list; with none qualifying the badge is All-Rounder. Badges describe style, never skill.
/// Change <see cref="Version"/> whenever a badge, threshold, reference rate or description changes.
/// </summary>
public static class BadgeRules
{
    public const string Version = "1";

    public const string AllRounderId = "all-rounder";

    /// <summary>
    /// How often the reference strategy keeps matching dice / the straight draw in each choice situation, measured from
    /// 300 simulated games (seed 20261020). A test checks these still match the simulation.
    /// </summary>
    public const double FourInARowMatching = 0.2302;

    public const double FourInARowStraight = 0.6665;

    public const double PairAndRunMatching = 0.6309;

    public const double PairAndRunStraight = 0.1745;

    private static readonly Func<double, string> Points = v => (v >= 0 ? "+" : "−") + Math.Abs(v * 100).ToString("0", CultureInfo.InvariantCulture) + " pts";
    private static readonly Func<double, string> Percent = v => v.ToString("0%", CultureInfo.InvariantCulture);
    private static readonly Func<double, string> OneDecimal = v => v.ToString("0.0", CultureInfo.InvariantCulture);
    private static readonly Func<double, string> Seconds = v => v.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    public static double ReferenceMatchingRate(RollSituation situation) => situation switch
    {
        RollSituation.FourInARow => FourInARowMatching,
        RollSituation.PairAndRun => PairAndRunMatching,
        _ => 0
    };

    public static double ReferenceStraightRate(RollSituation situation) => situation switch
    {
        RollSituation.FourInARow => FourInARowStraight,
        RollSituation.PairAndRun => PairAndRunStraight,
        _ => 0
    };

    public static Badge AllRounder { get; } = new(
        AllRounderId,
        "All-Rounder",
        "Plays a balanced game with no single habit standing out.",
        "Given when no other badge qualifies",
        _ => 0,
        _ => 0,
        0,
        true,
        0,
        1,
        OneDecimal);

    public static IReadOnlyList<Badge> All { get; } =
    [
        new(
            "yahtzee-hunter",
            "Yahtzee Hunter",
            "Keeps matching dice and chases big sets, even when a straight is on offer.",
            "Kept matching dice on rolls offering a set or a straight, vs the reference strategy",
            m => m.MatchingLean,
            m => m.ChoiceRolls,
            20,
            true,
            0.08,
            0.08,
            Points),
        new(
            "straight-shooter",
            "Straight Shooter",
            "Goes for the straight whenever the dice line up.",
            "Kept the straight draw on rolls offering a set or a straight, vs the reference strategy",
            m => m.StraightLean,
            m => m.ChoiceRolls,
            20,
            true,
            0.08,
            0.08,
            Points),
        new(
            "bonus-banker",
            "Bonus Banker",
            "Rarely misses the 35-point upper bonus.",
            "Cards with the upper bonus",
            m => m.UpperBonusRate,
            m => m.Cards,
            6,
            true,
            0.90,
            0.05,
            Percent),
        new(
            "card-closer",
            "Card Closer",
            "Likes to finish one card before starting the next.",
            "Cards in progress at once (average)",
            m => m.CardsInProgress,
            m => m.Turns,
            78,
            false,
            2.5,
            0.5,
            OneDecimal),
        new(
            "card-juggler",
            "Card Juggler",
            "Keeps lots of cards going at once and picks the best spot for every roll.",
            "Cards in progress at once (average)",
            m => m.CardsInProgress,
            m => m.Turns,
            78,
            true,
            4.5,
            0.5,
            OneDecimal),
        new(
            "sure-thing",
            "Sure Thing",
            "Takes good points as soon as they show up instead of rolling again.",
            "Turns scored before the third roll",
            m => m.EarlyStopRate,
            m => m.Turns,
            78,
            true,
            0.20,
            0.05,
            Percent),
        new(
            "quick-draw",
            "Quick Draw",
            "Plays fast and decides in a flash.",
            "Median time from first roll to score",
            m => m.MedianTurnSeconds,
            m => m.Turns,
            78,
            false,
            6.0,
            2.0,
            Seconds) { Timed = true },
        new(
            "deep-thinker",
            "Deep Thinker",
            "Takes time to weigh up every roll.",
            "Median time from first roll to score",
            m => m.MedianTurnSeconds,
            m => m.Turns,
            78,
            true,
            12.0,
            4.0,
            Seconds) { Timed = true },
    ];

    public static IEnumerable<Badge> AllWithDefault => All.Append(AllRounder);

    public static BadgeResult Choose(StyleMetrics metrics)
    {
        var scores = All.Select(b =>
        {
            double strength = b.Strength(metrics);
            return new BadgeScore(b, b.Metric(metrics), b.Opportunities(metrics), strength, strength > 0);
        }).ToList();

        // OrderByDescending is stable, so equal strengths keep the table order.
        var best = scores.Where(s => s.Qualifies).OrderByDescending(s => s.Strength).FirstOrDefault();
        return new BadgeResult(best?.Badge ?? AllRounder, scores);
    }

    public static Badge? Find(string id) => AllWithDefault.FirstOrDefault(b => b.Id == id);
}
