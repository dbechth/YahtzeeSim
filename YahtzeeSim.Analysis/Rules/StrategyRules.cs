using System.Globalization;
using YahtzeeSim.Analysis.Grading;
using YahtzeeSim.Analysis.Profile;
using YahtzeeSim.Analysis.Solver;
using YahtzeeSim.Engine;

namespace YahtzeeSim.Analysis.Rules;

/// <summary>Everything a rule may look at for one player.</summary>
public sealed record RuleContext(IReadOnlyList<GradedDecision> Decisions, StrategyProfile Profile, SimulationSummary Reference);

/// <summary>What a rule counted: how many times the condition held, out of how many chances, and the reference cost.</summary>
public sealed record RuleMatch(int Cases, int Opportunities, double PointsLost, IReadOnlyList<GradedDecision> Examples)
{
    public double Rate => Opportunities == 0 ? 0 : (double)Cases / Opportunities;

    public static RuleMatch From(IEnumerable<GradedDecision> opportunities, Func<GradedDecision, bool> isCase)
    {
        var all = opportunities.ToList();
        var cases = all.Where(isCase).ToList();
        return new RuleMatch(
            cases.Count,
            all.Count,
            cases.Sum(d => d.Cost),
            [.. cases.OrderByDescending(d => d.Cost).ThenBy(d => d.GameId, StringComparer.Ordinal).ThenBy(d => d.Turn).Take(3)]);
    }
}

/// <summary>
/// A fixed strategy rule. It fires only when <see cref="MinCases"/> and <see cref="MinRate"/> are both met, and its
/// sentence is <see cref="Template"/> with the counted numbers filled in ({cases}, {opportunities}, {rate}, {lost},
/// {reference}). No other wording is ever produced.
/// </summary>
public sealed record StrategyRule(
    string Id,
    string Title,
    string Condition,
    int MinCases,
    double MinRate,
    string Template,
    Func<RuleContext, RuleMatch> Evaluate,
    Func<RuleContext, string>? ReferenceText = null)
{
    public string Threshold => MinRate > 0
        ? string.Create(CultureInfo.InvariantCulture, $"Fires at {MinCases} or more cases and a rate of at least {MinRate:P0}.")
        : $"Fires at {MinCases} or more case{(MinCases == 1 ? string.Empty : "s")}.";
}

/// <summary>A rule's outcome for one player.</summary>
public sealed record RuleFinding(StrategyRule Rule, RuleMatch Match, bool Fired, string Sentence);

/// <summary>The versioned rule table. Change <see cref="Version"/> whenever a rule, threshold or template changes.</summary>
public static class StrategyRules
{
    public const string Version = "1";

    private const int EarlyTurnsRemaining = 20;
    private const int YahtzeeTurnsRemaining = 30;
    private const int LowChance = 20;

    public static IReadOnlyList<StrategyRule> All { get; } =
    [
        new(
            "UPPER-BELOW-PAR",
            "Upper boxes filled below par",
            $"An upper box (Aces to Sixes) was filled with fewer than three of its number while {EarlyTurnsRemaining} or more turns remained.",
            4,
            0.25,
            "You filled an upper box with fewer than three of its number {cases} times out of {opportunities} ({rate}) while 20 or more turns remained. Against the reference strategy these choices cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.Kind == DecisionKind.Box && IsUpper(d.ChosenBox!.Category) && d.TurnsRemaining >= EarlyTurnsRemaining),
                d => d.ChosenBox!.Points < 3 * Face(d.ChosenBox.Category))),

        new(
            "UPPER-BONUS-MISSED",
            "Upper bonus missed",
            "A card finished with an upper subtotal below 63, so the 35-point bonus was not earned.",
            1,
            1.0 / 6 + 1e-9,
            "You missed the 35-point upper bonus on {cases} of {opportunities} cards ({rate}). The reference strategy misses it on {reference} of cards.",
            c => new RuleMatch(
                c.Profile.Cards - c.Profile.UpperBonuses,
                c.Profile.Cards,
                GameCardState.UpperBonusValue * (c.Profile.Cards - c.Profile.UpperBonuses),
                []),
            c => Percent(1 - c.Reference.UpperBonusRate)),

        new(
            "YAHTZEE-SCRATCHED-EARLY",
            "Yahtzee box scratched early",
            $"A Yahtzee box was scratched while {YahtzeeTurnsRemaining} or more turns remained.",
            1,
            0,
            "You scratched a Yahtzee box {cases} times with 30 or more turns left. Against the reference strategy these choices cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.Kind == DecisionKind.Box && d.ChosenBox!.Category == ScoreCategory.Yahtzee),
                d => d.ChosenBox!.Scratch && d.TurnsRemaining >= YahtzeeTurnsRemaining)),

        new(
            "SCRATCH-AVOIDABLE",
            "Scratches where points were available",
            "A box was scratched (0 points) when the reference strategy's best box scored points, and the choice cost 2 or more points.",
            2,
            0,
            "{cases} of your {opportunities} scratches came when the reference strategy would have scored points instead. Together they cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.Kind == DecisionKind.Box && d.ChosenBox!.Scratch),
                d => !d.BestBox!.Scratch && d.Cost >= 2)),

        new(
            "CHANCE-LOW",
            "Chance used for a low total",
            $"Chance was filled with fewer than {LowChance} points.",
            2,
            0,
            "You used Chance for fewer than 20 points {cases} times out of {opportunities}. Against the reference strategy these choices cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.Kind == DecisionKind.Box && d.ChosenBox!.Category == ScoreCategory.Chance),
                d => d.ChosenBox!.Points < LowChance)),

        new(
            "STOPPED-EARLY",
            "Stopped rolling too soon",
            "Scored after roll 1 or 2 when rolling again was worth 1 or more expected points.",
            3,
            0,
            "You scored before using all three rolls {opportunities} times; {cases} of those gave up 1 or more expected points by not rolling again ({lost} points in total).",
            c => RuleMatch.From(c.Decisions.Where(d => d.IsStop), d => d.Cost >= 1)),

        new(
            "KIND-OVER-STRAIGHT",
            "Kept matching dice instead of a straight draw",
            "The reference strategy kept a straight draw, the player kept a pair or more of one number, and the keep cost 0.5 or more points.",
            3,
            0,
            "When the reference strategy would chase a straight, you kept matching dice instead {cases} times out of {opportunities}. Together they cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.IsKeep && d.BestPattern is { } p && KeepPatterns.FamilyOf(p) == KeepFamily.Straight),
                d => d.ChosenPattern is { } p && KeepPatterns.FamilyOf(p) == KeepFamily.OfAKind && d.Cost >= 0.5)),

        new(
            "STRAIGHT-OVER-KIND",
            "Chased a straight instead of matching dice",
            "The reference strategy kept a pair or more of one number, the player kept a straight draw, and the keep cost 0.5 or more points.",
            3,
            0,
            "When the reference strategy would keep matching dice, you chased a straight instead {cases} times out of {opportunities}. Together they cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.IsKeep && d.BestPattern is { } p && KeepPatterns.FamilyOf(p) == KeepFamily.OfAKind),
                d => d.ChosenPattern is { } p && KeepPatterns.FamilyOf(p) == KeepFamily.Straight && d.Cost >= 0.5)),

        new(
            "KEPT-TOO-FEW",
            "Rerolled dice worth keeping",
            "The player kept fewer dice than the reference strategy and the keep cost 1 or more points.",
            5,
            0,
            "You kept fewer dice than the reference strategy {cases} times out of {opportunities} keeps where it cost 1 or more points ({lost} points in total).",
            c => RuleMatch.From(c.Decisions.Where(d => d.IsKeep), d => d.Cost >= 1 && d.ChosenKeepSize < d.BestKeepSize)),

        new(
            "KEPT-TOO-MANY",
            "Kept dice worth rerolling",
            "The player kept more dice than the reference strategy and the keep cost 1 or more points.",
            5,
            0,
            "You kept more dice than the reference strategy {cases} times out of {opportunities} keeps where it cost 1 or more points ({lost} points in total).",
            c => RuleMatch.From(c.Decisions.Where(d => d.IsKeep), d => d.Cost >= 1 && d.ChosenKeepSize > d.BestKeepSize)),

        new(
            "SAME-BOX-OTHER-CARD",
            "Right box, different card",
            "The player chose the same box as the reference strategy but on a different card, and the choice cost 2 or more points.",
            3,
            0,
            "{cases} times you chose the same box as the reference strategy but on a different card. Each card has its own upper bonus and open boxes, and these choices cost {lost} points.",
            c => RuleMatch.From(
                c.Decisions.Where(d => d.Kind == DecisionKind.Box),
                d => d.ChosenBox!.Category == d.BestBox!.Category && d.ChosenBox.Card != d.BestBox.Card && d.Cost >= 2)),

        new(
            "BIG-BOX-COST",
            "Costly box choices",
            "A box choice cost 5 or more points against the reference strategy.",
            3,
            0,
            "{cases} of your {opportunities} box choices cost 5 or more points each against the reference strategy ({lost} points in total).",
            c => RuleMatch.From(c.Decisions.Where(d => d.Kind == DecisionKind.Box), d => d.Cost >= 5)),

        new(
            "UNDO-AFTER-NEXT-ROLL",
            "Undo after the next roll",
            "Undo was pressed after the next turn's dice had already been rolled, so those dice were discarded.",
            1,
            0,
            "You pressed Undo after already rolling the next turn {cases} times out of {opportunities} undos; the dice from that roll were discarded and rolled again.",
            c => new RuleMatch(c.Profile.UndosAfterNextRoll, c.Profile.Undos, 0, [])),
    ];

    public static IReadOnlyList<RuleFinding> Evaluate(RuleContext context)
    {
        var findings = All.Select(rule =>
        {
            var match = rule.Evaluate(context);
            bool fired = match.Cases >= rule.MinCases && match.Rate >= rule.MinRate;
            string sentence = rule.Template
                .Replace("{cases}", match.Cases.ToString(CultureInfo.InvariantCulture))
                .Replace("{opportunities}", match.Opportunities.ToString(CultureInfo.InvariantCulture))
                .Replace("{rate}", Percent(match.Rate))
                .Replace("{lost}", match.PointsLost.ToString("0.0", CultureInfo.InvariantCulture))
                .Replace("{reference}", rule.ReferenceText?.Invoke(context) ?? string.Empty);
            return new RuleFinding(rule, match, fired, sentence);
        });

        return [.. findings
            .OrderByDescending(f => f.Fired)
            .ThenByDescending(f => f.Fired ? f.Match.PointsLost : 0)
            .ThenBy(f => f.Rule.Id, StringComparer.Ordinal)];
    }

    private static bool IsUpper(ScoreCategory category) => category < ScoreCategory.ThreeOfAKind;

    private static int Face(ScoreCategory category) => (int)category + 1;

    private static string Percent(double rate) => rate.ToString("0%", CultureInfo.InvariantCulture);
}
